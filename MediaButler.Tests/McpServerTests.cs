using System.Text.Json.Nodes;
using MediaButler.Mcp;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// The MCP front door (MB-A6): JSON-RPC 2.0 dispatch and the scan/status/run
/// tools, driven message-by-message without a transport. The settings loader
/// is injected so nothing here reads %APPDATA% or the real inboxes.
/// </summary>
[TestFixture]
public class McpServerTests
{
    private TempDir tmp = null!;
    private MediaButlerSettings settings = null!;
    private McpServer server = null!;

    [SetUp]
    public void SetUp()
    {
        tmp = new TempDir();
        settings = new MediaButlerSettings
        {
            SourcePath           = tmp.MakeDir("Torrents"),
            TvDestination        = tmp.MakeDir("TV"),
            MoviesDestination    = tmp.MakeDir("Movies"),
            VariationCatalogPath = Path.Combine(tmp.Path, "variations.json"),
            FileBotPath          = Path.Combine(tmp.Path, "no-filebot-here.exe"),
            EnableLlmFallback    = false,
            EnableSubtitles      = false,
            // TryLocate falls back to the machine-wide FileBot install, so a
            // bogus FileBotPath alone doesn't keep tests offline — disable
            // every stage that would invoke it.
            RenameEpisodes       = false,
            RenameMovies         = false,
            FetchArtwork         = false,
        };
        server = new McpServer(() => settings);
    }

    [TearDown]
    public void TearDown() => tmp.Dispose();

    private JsonNode Send(string method, string? paramsJson = null, int id = 1)
    {
        var msg = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\"" +
                  (paramsJson is null ? "}" : $",\"params\":{paramsJson}}}");
        var response = server.HandleMessage(msg);
        Assert.That(response, Is.Not.Null, $"no response to {method}");
        return JsonNode.Parse(response!)!;
    }

    [Test]
    public void Initialize_reports_server_info_and_tools_capability()
    {
        var r = Send("initialize", "{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{}}");
        Assert.Multiple(() =>
        {
            Assert.That(r["result"]!["serverInfo"]!["name"]!.GetValue<string>(), Is.EqualTo("mediabutler"));
            Assert.That(r["result"]!["protocolVersion"]!.GetValue<string>(), Is.EqualTo("2025-03-26"),
                "the client's protocol version must be echoed");
            Assert.That(r["result"]!["capabilities"]!["tools"], Is.Not.Null);
        });
    }

    [Test]
    public void Notifications_get_no_response()
    {
        var response = server.HandleMessage("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        Assert.That(response, Is.Null);
    }

    [Test]
    public void Unknown_method_returns_method_not_found()
    {
        var r = Send("resources/list");
        Assert.That(r["error"]!["code"]!.GetValue<int>(), Is.EqualTo(-32601));
    }

    [Test]
    public void Malformed_json_returns_parse_error()
    {
        var response = server.HandleMessage("{nope");
        Assert.That(JsonNode.Parse(response!)!["error"]!["code"]!.GetValue<int>(), Is.EqualTo(-32700));
    }

    [Test]
    public void ToolsList_exposes_scan_status_run_with_safe_run_default()
    {
        var r = Send("tools/list");
        var tools = r["result"]!["tools"]!.AsArray();
        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(new[] { "scan", "status", "run" }));
            var run = tools.First(t => t!["name"]!.GetValue<string>() == "run")!;
            Assert.That(run["inputSchema"]!["properties"]!["dryRun"]!["default"]!.GetValue<bool>(), Is.True,
                "run must default to dry-run — an agent opts into mutation explicitly");
        });
    }

    [Test]
    public void Scan_tool_classifies_a_movie_folder()
    {
        var movie = Path.Combine(settings.SourcePath, "Weapons (2025)");
        Directory.CreateDirectory(movie);
        File.WriteAllBytes(Path.Combine(movie, "Weapons (2025).mkv"), new byte[10]);

        var r = Send("tools/call", "{\"name\":\"scan\",\"arguments\":{}}");
        var result = r["result"]!;
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        var items = JsonNode.Parse(text)!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(result["isError"]!.GetValue<bool>(), Is.False);
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0]!["kind"]!.GetValue<string>(), Is.EqualTo("Movie"));
            Assert.That(items[0]!["target"]!.GetValue<string>(), Is.EqualTo("Weapons (2025)"));
        });
    }

    [Test]
    public void Status_tool_reports_mode_and_duplicate_policy()
    {
        var r = Send("tools/call", "{\"name\":\"status\",\"arguments\":{}}");
        var text = r["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var status = JsonNode.Parse(text)!;
        Assert.Multiple(() =>
        {
            Assert.That(status["mode"]!.GetValue<string>(), Is.EqualTo("live"));
            Assert.That(status["duplicateMovieAction"]!.GetValue<string>(), Is.EqualTo("KeepLargest"));
            // TryLocate falls back to machine-wide installs, so the value is
            // environment-specific — only its presence is contractual.
            Assert.That(status["fileBot"]!.GetValue<string>(), Is.Not.Empty);
        });
    }

    [Test]
    public void Run_tool_defaults_to_dry_run_and_mutates_nothing()
    {
        var movie = Path.Combine(settings.SourcePath, "Weapons.2025.1080p.WEBRip");
        Directory.CreateDirectory(movie);
        File.WriteAllBytes(Path.Combine(movie, "Weapons.2025.1080p.WEBRip.mkv"), new byte[10]);

        var r = Send("tools/call", "{\"name\":\"run\",\"arguments\":{}}");
        var text = r["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("exit"));
            Assert.That(text, Does.Contain("DRY RUN"));
            Assert.That(Directory.Exists(movie), Is.True, "dry-run must not touch the inbox");
            Assert.That(Directory.EnumerateFileSystemEntries(settings.MoviesDestination), Is.Empty,
                "dry-run must not populate the destination");
        });
    }

    [Test]
    public void Unknown_tool_reports_isError_instead_of_a_protocol_fault()
    {
        var r = Send("tools/call", "{\"name\":\"nuke\",\"arguments\":{}}");
        Assert.Multiple(() =>
        {
            Assert.That(r["error"], Is.Null, "tool-level failures ride inside the result");
            Assert.That(r["result"]!["isError"]!.GetValue<bool>(), Is.True);
        });
    }
}
