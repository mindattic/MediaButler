using System.Text.Json;
using MindAttic.Legion;
using MediaButler.Settings;

namespace MediaButler.Llm;

/// <summary>
/// LLM-backed fallback for folder names the regex parser in
/// <see cref="Media.NameParser"/> can't classify. Asks the configured Legion
/// provider for a strict JSON answer, then maps it back into the same shape
/// the regex parser would have returned. Off unless
/// <see cref="MediaButlerSettings.EnableLlmFallback"/> is true.
/// </summary>
public sealed class LegionFallbackParser
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly MediaButlerSettings settings;
    private readonly LegionClient client;

    public LegionFallbackParser(MediaButlerSettings settings)
    {
        this.settings = settings;
        client = new LegionClient(SharedHttp);
    }

    /// <summary>
    /// Best-effort classification. Returns null if the LLM call fails, the
    /// fallback is disabled, or the response isn't parseable. Callers MUST
    /// tolerate null — we'd rather skip a folder than rename it wrong.
    /// </summary>
    public async Task<LlmGuess?> ClassifyAsync(string folderName, IReadOnlyList<string> sampleFileNames, CancellationToken ct = default)
    {
        if (!settings.EnableLlmFallback) return null;

        var sample = string.Join("\n", sampleFileNames.Take(6).Select(s => "  - " + s));
        var prompt = $$"""
            Classify this media folder. Reply with ONLY a JSON object, no prose.

            Folder name: {{folderName}}
            Sample file names inside:
            {{sample}}

            Schema:
              {
                "kind": "movie" | "tv_season" | "unknown",
                "title": "<show or movie title, properly capitalized>",
                "year": <number or null>,
                "season": <number or null>
              }

            Rules:
              - "kind"="tv_season" when there is a season indicator (Season N, SxxEyy, S0x).
              - "kind"="movie" when there is a single feature film with a year.
              - Strip release-group tags, codec tags (x264, HEVC), and resolution tags from "title".
              - Use sentence case for the title (e.g. "Better Call Saul", "The Matrix").
              - If unsure, use "unknown" and leave the optional fields null.
            """;

        try
        {
            var raw = await client.CallAsync(
                providerId: settings.LlmProvider,
                systemPrompt: "You are a media library classifier. Respond ONLY with the requested JSON object.",
                userMessage: prompt,
                maxTokens: 256,
                temperature: 0.0,
                ct: ct);

            var json = ExtractJsonObject(raw);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kindStr = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var title   = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var year    = root.TryGetProperty("year",   out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : (int?)null;
            var season  = root.TryGetProperty("season", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : (int?)null;

            var kind = kindStr switch
            {
                "movie"     => LlmKind.Movie,
                "tv_season" => LlmKind.TvSeason,
                _           => LlmKind.Unknown,
            };

            if (kind == LlmKind.Unknown || string.IsNullOrWhiteSpace(title)) return null;

            return new LlmGuess { Kind = kind, Title = title!.Trim(), Year = year, Season = season };
        }
        catch
        {
            // LLM failures are non-fatal — MediaButler just skips the folder.
            return null;
        }
    }

    /// <summary>
    /// Some providers wrap their JSON in ```json fences or prose, or echo the
    /// schema object before the real answer. Extract the first <i>balanced</i>
    /// <c>{...}</c> block by tracking brace depth — a naive first-brace to
    /// last-brace slice would merge two objects into invalid JSON. Returns null
    /// if no complete object is present.
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            // Braces inside a JSON string value are literal text, not structure —
            // a title like "Spinal Tap {Live}" must not throw off the depth count.
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
                return raw.Substring(start, i - start + 1);
        }
        return null;
    }
}

public enum LlmKind { Unknown, Movie, TvSeason }

public sealed record LlmGuess
{
    public required LlmKind Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
}
