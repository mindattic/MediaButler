using System.Text.Json;
using System.Text.Json.Nodes;
using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.Mcp;

/// <summary>
/// Model Context Protocol server core: JSON-RPC 2.0 message dispatch plus the
/// three MediaButler tools (<c>scan</c>, <c>status</c>, <c>run</c>). Transport
/// lives in <c>McpCommand</c> (newline-delimited JSON over stdio); this class
/// is transport-free so tests can drive it message-by-message.
///
/// <para>One engine, many front doors (HOUSE-LAW-6): every tool call routes
/// through the same <see cref="PipelineRunner"/>/<see cref="MediaScanner"/>
/// the CLI and menu use — the MCP surface adds no behaviour of its own.</para>
///
/// <para>Safety: <c>scan</c> and <c>status</c> are read-only; <c>run</c>
/// defaults to <c>dryRun=true</c> and mutates disk only when the caller
/// explicitly passes <c>dryRun=false</c> (MB-LAW-1 applies as usual).</para>
/// </summary>
public sealed class McpServer
{
    public const string ProtocolVersion = "2025-06-18";

    private readonly Func<MediaButlerSettings> loadSettings;

    public McpServer(SettingsService settingsService)
        : this(() => new PipelineRunner(settingsService).LoadEffective()) { }

    /// <summary>Test seam: inject the settings loader so tests never touch %APPDATA%.</summary>
    internal McpServer(Func<MediaButlerSettings> loadSettings) => this.loadSettings = loadSettings;

    /// <summary>
    /// Handle one JSON-RPC message. Returns the serialized response, or null
    /// when no response is due (notifications, responses to unknown ids).
    /// </summary>
    public string? HandleMessage(string json)
    {
        // Some hosts (and shell pipes) prepend a UTF-8 BOM to the first frame.
        json = json.TrimStart('\uFEFF', ' ', '\t');
        JsonNode? msg;
        try { msg = JsonNode.Parse(json); }
        catch (JsonException) { return Error(null, -32700, "Parse error"); }

        var id = msg?["id"];
        var method = msg?["method"]?.GetValue<string>();
        if (method is null) return null; // a response or malformed frame — nothing to do
        if (method.StartsWith("notifications/", StringComparison.Ordinal)) return null;

        JsonNode result;
        try
        {
            switch (method)
            {
                case "initialize":  result = Initialize(msg!["params"]); break;
                case "ping":        result = new JsonObject(); break;
                case "tools/list":  result = ToolsList(); break;
                case "tools/call":  result = ToolsCall(msg!["params"]); break;
                default:            return id is null ? null : Error(id, -32601, $"Method not found: {method}");
            }
        }
        catch (Exception ex)
        {
            return id is null ? null : Error(id, -32603, ex.Message);
        }

        if (id is null) return null;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        }.ToJsonString();
    }

    private static string Error(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    }.ToJsonString();

    private static JsonNode Initialize(JsonNode? p) => new JsonObject
    {
        // Echo the client's requested protocol version when given — the wire
        // shapes this server uses are stable across published revisions.
        ["protocolVersion"] = p?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "mediabutler",
            ["version"] = AppVersion(),
        },
    };

    private static string AppVersion() =>
        typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static JsonNode ToolsList() => new JsonObject
    {
        ["tools"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "scan",
                ["description"] = "Classify every item in the configured inboxes (read-only, no disk mutation). " +
                                  "Returns one JSON entry per root item with its kind (Movie, TvSeason, TvEpisode, " +
                                  "MoviePack, MovieCollection, MultiSeasonParent, Music, Extras, Empty, Unknown) and canonical target name.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["source"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Scan only this inbox path instead of the configured sources.",
                        },
                    },
                    ["additionalProperties"] = false,
                },
            },
            new JsonObject
            {
                ["name"] = "status",
                ["description"] = "MediaButler configuration snapshot: sources, destinations, mode, duplicate policy, FileBot availability.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                    ["additionalProperties"] = false,
                },
            },
            new JsonObject
            {
                ["name"] = "run",
                ["description"] = "Run the full organize pipeline (rename → FileBot → move). dryRun=true (the default) " +
                                  "only reports what would happen; pass dryRun=false to actually rename, move, and delete. " +
                                  "Returns the pipeline log and exit code (0 clean, 1 errors, 2 needs-manual).",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["dryRun"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Plan only (true, default) or mutate disk (false).",
                            ["default"] = true,
                        },
                        ["source"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Process only this inbox path instead of the configured sources.",
                        },
                    },
                    ["additionalProperties"] = false,
                },
            }),
    };

    private JsonNode ToolsCall(JsonNode? p)
    {
        var name = p?["name"]?.GetValue<string>() ?? "";
        var args = p?["arguments"] as JsonObject;
        string text;
        var isError = false;
        try
        {
            text = name switch
            {
                "scan"   => ScanTool(args),
                "status" => StatusTool(),
                "run"    => RunTool(args),
                _        => throw new InvalidOperationException($"Unknown tool: {name}"),
            };
        }
        catch (Exception ex)
        {
            text = ex.Message;
            isError = true;
        }
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };
    }

    private MediaButlerSettings LoadSettings(JsonObject? args)
    {
        var s = loadSettings();
        var source = args?["source"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(source))
        {
            s.SourcePath   = source!.Trim();
            s.ExtraSources = Array.Empty<string>();
            s.Recursive    = false;
        }
        return s;
    }

    private string ScanTool(JsonObject? args)
    {
        var s = LoadSettings(args);
        var result = new JsonArray();
        foreach (var source in PipelineRunner.EffectiveSources(s))
        {
            if (!Directory.Exists(source))
            {
                result.Add(new JsonObject { ["source"] = source, ["error"] = "source path not found" });
                continue;
            }
            s.SourcePath = source;
            foreach (var item in new MediaScanner(s).Scan())
                result.Add(ItemJson(source, item));
        }
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject ItemJson(string source, MediaItem item)
    {
        var o = new JsonObject
        {
            ["source"] = source,
            ["name"] = item.OriginalName,
            ["kind"] = item.Kind.ToString(),
            ["isFile"] = item.IsFile,
        };
        var target = item.Kind switch
        {
            MediaKind.Movie     => NameParser.FormatMovieFolder(item.MovieTitle ?? "?", item.MovieYear),
            MediaKind.TvSeason  => NameParser.FormatSeasonFolder(item.ShowName ?? "?", item.SeasonNumber ?? 0),
            MediaKind.TvEpisode => NameParser.FormatSeasonFolder(item.ShowName ?? "?", item.SeasonNumber ?? 0),
            _ => null,
        };
        if (target is not null) o["target"] = target;
        if (item.Kind == MediaKind.TvEpisode) o["episode"] = item.EpisodeNumber;
        if (item.Kind == MediaKind.MoviePack) o["packMovies"] = item.PackMovies.Count;
        return o;
    }

    private string StatusTool()
    {
        var s = loadSettings();
        return new JsonObject
        {
            ["version"] = AppVersion(),
            ["mode"] = s.DryRun ? "dry-run" : "live",
            ["source"] = s.SourcePath,
            ["extraSources"] = new JsonArray(s.ExtraSources.Select(x => (JsonNode)x).ToArray()),
            ["recursive"] = s.Recursive,
            ["tvDestination"] = s.TvDestination,
            ["moviesDestination"] = s.MoviesDestination,
            ["musicDestination"] = string.IsNullOrWhiteSpace(s.MusicDestination) ? null : s.MusicDestination,
            ["duplicateMovieAction"] = s.DuplicateMovieAction.ToString(),
            ["duplicateEpisodeAction"] = s.DuplicateEpisodeAction.ToString(),
            ["fileBot"] = FileBotClient.TryLocate(s.FileBotPath) ?? "NOT FOUND",
            ["enableSubtitles"] = s.EnableSubtitles,
            ["enableLlmFallback"] = s.EnableLlmFallback,
            ["llmProvider"] = s.LlmProvider,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private string RunTool(JsonObject? args)
    {
        var s = LoadSettings(args);
        // Default to the safe mode: an agent must say dryRun=false out loud.
        s.DryRun = args?["dryRun"]?.GetValue<bool>() ?? true;

        // The pipeline narrates via Console.Write; stdout is the protocol
        // channel here, so capture the narration and return it as the result.
        var prev = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        int exit;
        // RunFull never touches the runner's SettingsService (that's only for
        // LoadEffective/persistence), so a fresh facade here is inert.
        try { exit = new PipelineRunner(new SettingsService()).RunFull(s); }
        finally { Console.SetOut(prev); }

        var meaning = exit switch
        {
            PipelineRunner.ExitOk          => "clean",
            PipelineRunner.ExitErrors      => "errors",
            PipelineRunner.ExitNeedsManual => "needs manual review",
            _ => "unknown",
        };
        return $"exit {exit} ({meaning}), mode {(s.DryRun ? "DRY RUN" : "LIVE")}\n{buffer}";
    }
}
