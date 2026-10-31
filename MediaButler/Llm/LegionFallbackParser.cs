using System.Text.Json;
using MindAttic.Legion;
using MindAttic.Vault.Credentials;
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

    // The key KeySelector picked for the call in flight. LegionClient's keyResolver wins outright
    // when it returns a key, so model resolution, retries and the breaker stay exactly Legion's.
    private static readonly AsyncLocal<string?> CurrentKey = new();
    private static readonly LegionClient Client = new(SharedHttp, options: null, keyResolver: _ => CurrentKey.Value);

    /// <summary>
    /// The shape of a single "send prompt, get text" round trip, factored out
    /// of <see cref="LegionClient"/> so tests can substitute a canned/throwing
    /// stub instead of a real provider call — no live network, no credentials.
    /// </summary>
    internal delegate Task<string> Transport(string providerId, string systemPrompt, string userMessage, int maxTokens, double temperature, CancellationToken ct);

    /// <summary>One provider call with an explicit key (the seam <see cref="KeySelector"/> fails over across).</summary>
    internal delegate Task<string> KeyedCall(string providerId, string apiKey, string systemPrompt, string userMessage, int maxTokens, double temperature, CancellationToken ct);

    private readonly MediaButlerSettings settings;
    private readonly Transport transport;

    public LegionFallbackParser(MediaButlerSettings settings)
        : this(settings, BuildDefaultTransport())
    {
    }

    /// <summary>Test seam — lets NUnit tests inject a fake <see cref="Transport"/> instead of hitting a real provider.</summary>
    internal LegionFallbackParser(MediaButlerSettings settings, Transport transport)
    {
        this.settings = settings;
        this.transport = transport;
    }

    /// <summary>
    /// LAMP (MB-T27): keys come from the shared named lists through <see cref="KeySelector"/>,
    /// MediaButler's assigned order first (Vault.Desktop's Keys section), failing over to the next
    /// key on 401/403/402/429. No per-app key copy is kept.
    /// </summary>
    private static Transport BuildDefaultTransport() =>
        BuildTransport(KeySelector.ForCurrentUser(ClassifyLegionFailure), CallLegionAsync);

    /// <summary>Wraps <paramref name="call"/> in key selection and failover for MediaButler.</summary>
    internal static Transport BuildTransport(KeySelector keys, KeyedCall call) =>
        (providerId, systemPrompt, userMessage, maxTokens, temperature, ct) =>
            keys.ExecuteAsync(providerId, MediaButlerSettings.AppId,
                (key, token) => call(providerId, key, systemPrompt, userMessage, maxTokens, temperature, token), ct);

    private static async Task<string> CallLegionAsync(string providerId, string apiKey, string systemPrompt,
        string userMessage, int maxTokens, double temperature, CancellationToken ct)
    {
        // Set inside this async method, so the value is scoped to this call's flow only.
        CurrentKey.Value = apiKey;
        return await Client.CallAsync(providerId, systemPrompt, userMessage, maxTokens, temperature, ct: ct);
    }

    /// <summary>Legion may wrap the provider's HTTP failure (retry/breaker); find the status underneath.</summary>
    internal static KeyFailureException? ClassifyLegionFailure(Exception ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
            if (e is HttpRequestException { StatusCode: { } status } && KeySelector.Classify(status) is { } kind)
                return new KeyFailureException(kind, e.GetType().Name);
        return null;
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
            var raw = await transport(
                settings.LlmProvider,
                "You are a media library classifier. Respond ONLY with the requested JSON object.",
                prompt,
                256,
                0.0,
                ct);

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
    /// Best-effort classification of a single FILE name that matched no known
    /// pattern (no year, no episode marker). Same contract as
    /// <see cref="ClassifyAsync"/>: null on disabled / failure / "unknown" —
    /// the caller leaves the file alone rather than renaming it wrong
    /// (see MB-LAW-6).
    /// </summary>
    public async Task<LlmFileGuess?> ClassifyFileAsync(string fileName, CancellationToken ct = default)
    {
        if (!settings.EnableLlmFallback) return null;

        var prompt = $$"""
            Classify this media FILE name. Reply with ONLY a JSON object, no prose.

            File name: {{fileName}}

            Schema:
              {
                "kind": "movie" | "tv_episode" | "unknown",
                "title": "<movie title or show name, properly capitalized>",
                "year": <number or null>,
                "season": <number or null>,
                "episode": <number or null>
              }

            Rules:
              - "kind"="tv_episode" when the name encodes a specific episode of a show
                (any marker style: SxxEyy, 3x09, 1.09, episode words, absolute anime numbering).
              - "kind"="movie" when it is a single feature film; include the release year if present.
              - Strip release-group tags, codec tags (x264, HEVC), and resolution tags from "title".
              - Use sentence case for the title (e.g. "Better Call Saul", "The Matrix").
              - If unsure, use "unknown" and leave the optional fields null.
            """;

        try
        {
            var raw = await transport(
                settings.LlmProvider,
                "You are a media library classifier. Respond ONLY with the requested JSON object.",
                prompt,
                256,
                0.0,
                ct);

            var json = ExtractJsonObject(raw);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kindStr = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var title   = root.TryGetProperty("title",   out var t) ? t.GetString() : null;
            var year    = root.TryGetProperty("year",    out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : (int?)null;
            var season  = root.TryGetProperty("season",  out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : (int?)null;
            var episode = root.TryGetProperty("episode", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int?)null;

            if (string.IsNullOrWhiteSpace(title)) return null;
            return kindStr switch
            {
                "movie" => new LlmFileGuess { Kind = LlmFileKind.Movie, Title = title!.Trim(), Year = year },
                // An episode guess is only actionable when season AND episode are known.
                "tv_episode" when season.HasValue && episode.HasValue =>
                    new LlmFileGuess { Kind = LlmFileKind.TvEpisode, Title = title!.Trim(), Season = season, Episode = episode },
                _ => null,
            };
        }
        catch
        {
            // LLM failures are non-fatal — MediaButler just skips the file.
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
    internal static string? ExtractJsonObject(string raw)
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

public enum LlmFileKind { Movie, TvEpisode }

/// <summary>LLM best-guess for a single unmatched file name.</summary>
public sealed record LlmFileGuess
{
    public required LlmFileKind Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
}

public sealed record LlmGuess
{
    public required LlmKind Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
}
