using MediaButler.Llm;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Tests for <see cref="LegionFallbackParser"/> against the injectable
/// <see cref="LegionFallbackParser.Transport"/> seam — no live provider, no
/// real credentials, no network. Covers the MB-LAW-6 "never rename wrong"
/// contract: disabled/failed/ambiguous answers must come back null, never a
/// best-effort guess.
/// </summary>
[TestFixture]
public class LegionFallbackParserTests
{
    private static MediaButlerSettings Settings(bool enabled = true) =>
        new() { EnableLlmFallback = enabled };

    private static LegionFallbackParser.Transport Returning(string raw) =>
        (_, _, _, _, _, _) => Task.FromResult(raw);

    private static LegionFallbackParser.Transport Throwing() =>
        (_, _, _, _, _, _) => throw new InvalidOperationException("provider unreachable");

    // ── EnableLlmFallback=false short-circuit ───────────────────────────────

    [Test]
    public async Task ClassifyAsync_returns_null_when_fallback_disabled()
    {
        var parser = new LegionFallbackParser(Settings(enabled: false), Throwing());
        var result = await parser.ClassifyAsync("Some.Folder", new[] { "file.mkv" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_returns_null_when_fallback_disabled()
    {
        var parser = new LegionFallbackParser(Settings(enabled: false), Throwing());
        var result = await parser.ClassifyFileAsync("some.file.mkv");
        Assert.That(result, Is.Null);
    }

    // ── Provider exceptions swallowed to null ───────────────────────────────

    [Test]
    public async Task ClassifyAsync_swallows_provider_exception_to_null()
    {
        var parser = new LegionFallbackParser(Settings(), Throwing());
        var result = await parser.ClassifyAsync("Some.Folder", new[] { "file.mkv" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_swallows_provider_exception_to_null()
    {
        var parser = new LegionFallbackParser(Settings(), Throwing());
        var result = await parser.ClassifyFileAsync("some.file.mkv");
        Assert.That(result, Is.Null);
    }

    // ── ClassifyAsync kind mapping ───────────────────────────────────────────

    [Test]
    public async Task ClassifyAsync_maps_movie_kind()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "Heat", "year": 1995, "season": null }"""));
        var result = await parser.ClassifyAsync("Heat.1995", new[] { "heat.mkv" });
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Kind, Is.EqualTo(LlmKind.Movie));
            Assert.That(result.Title, Is.EqualTo("Heat"));
            Assert.That(result.Year, Is.EqualTo(1995));
        });
    }

    [Test]
    public async Task ClassifyAsync_maps_tv_season_kind()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "tv_season", "title": "Better Call Saul", "year": null, "season": 3 }"""));
        var result = await parser.ClassifyAsync("BCS.S03", new[] { "bcs.s03e01.mkv" });
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Kind, Is.EqualTo(LlmKind.TvSeason));
            Assert.That(result.Season, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ClassifyAsync_returns_null_for_unknown_kind()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "unknown", "title": null, "year": null, "season": null }"""));
        var result = await parser.ClassifyAsync("Mystery.Folder", new[] { "a.mkv" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyAsync_returns_null_when_title_missing()
    {
        // "kind"="movie" but no title at all — a titleless guess is not actionable.
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "year": 1995, "season": null }"""));
        var result = await parser.ClassifyAsync("Heat.1995", new[] { "heat.mkv" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyAsync_trims_title()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "  Heat  ", "year": 1995, "season": null }"""));
        var result = await parser.ClassifyAsync("Heat.1995", new[] { "heat.mkv" });
        Assert.That(result!.Title, Is.EqualTo("Heat"));
    }

    [Test]
    public async Task ClassifyAsync_ignores_non_numeric_year()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "Heat", "year": "nineteen ninety five", "season": null }"""));
        var result = await parser.ClassifyAsync("Heat.1995", new[] { "heat.mkv" });
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Year, Is.Null);
    }

    [Test]
    public async Task ClassifyAsync_ignores_non_numeric_season()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "tv_season", "title": "Show", "year": null, "season": "three" }"""));
        var result = await parser.ClassifyAsync("Show.S03", new[] { "s03e01.mkv" });
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Season, Is.Null);
    }

    // ── ClassifyFileAsync kind mapping ───────────────────────────────────────

    [Test]
    public async Task ClassifyFileAsync_maps_movie_kind()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "Heat", "year": 1995, "season": null, "episode": null }"""));
        var result = await parser.ClassifyFileAsync("heat.1995.mkv");
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Kind, Is.EqualTo(LlmFileKind.Movie));
            Assert.That(result.Title, Is.EqualTo("Heat"));
            Assert.That(result.Year, Is.EqualTo(1995));
        });
    }

    [Test]
    public async Task ClassifyFileAsync_maps_tv_episode_when_season_and_episode_present()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "tv_episode", "title": "Show", "year": null, "season": 3, "episode": 9 }"""));
        var result = await parser.ClassifyFileAsync("show.3x09.mkv");
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Kind, Is.EqualTo(LlmFileKind.TvEpisode));
            Assert.That(result.Season, Is.EqualTo(3));
            Assert.That(result.Episode, Is.EqualTo(9));
        });
    }

    [Test]
    public async Task ClassifyFileAsync_returns_null_tv_episode_missing_season()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "tv_episode", "title": "Show", "year": null, "season": null, "episode": 9 }"""));
        var result = await parser.ClassifyFileAsync("show.episode.9.mkv");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_returns_null_tv_episode_missing_episode()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "tv_episode", "title": "Show", "year": null, "season": 3, "episode": null }"""));
        var result = await parser.ClassifyFileAsync("show.season.3.mkv");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_returns_null_for_unknown_kind()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "unknown", "title": null, "year": null, "season": null, "episode": null }"""));
        var result = await parser.ClassifyFileAsync("mystery.mkv");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_returns_null_when_title_missing()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "year": 1995, "season": null, "episode": null }"""));
        var result = await parser.ClassifyFileAsync("heat.1995.mkv");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyFileAsync_trims_title()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "  Heat  ", "year": 1995, "season": null, "episode": null }"""));
        var result = await parser.ClassifyFileAsync("heat.1995.mkv");
        Assert.That(result!.Title, Is.EqualTo("Heat"));
    }

    // ── Unparseable / missing-object responses return null ──────────────────

    [Test]
    public async Task ClassifyAsync_returns_null_when_response_has_no_json_object()
    {
        var parser = new LegionFallbackParser(Settings(), Returning("I'm not sure, sorry."));
        var result = await parser.ClassifyAsync("Some.Folder", new[] { "a.mkv" });
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ClassifyAsync_returns_null_for_unterminated_object()
    {
        var parser = new LegionFallbackParser(Settings(),
            Returning("""{ "kind": "movie", "title": "Heat" """));
        var result = await parser.ClassifyAsync("Heat.1995", new[] { "heat.mkv" });
        Assert.That(result, Is.Null);
    }

    // ── ExtractJsonObject: balanced-brace extraction ─────────────────────────

    [Test]
    public void ExtractJsonObject_pulls_object_out_of_code_fence()
    {
        var raw = "```json\n{ \"kind\": \"movie\", \"title\": \"Heat\" }\n```";
        var json = LegionFallbackParser.ExtractJsonObject(raw);
        Assert.That(json, Is.EqualTo("{ \"kind\": \"movie\", \"title\": \"Heat\" }"));
    }

    [Test]
    public void ExtractJsonObject_pulls_object_out_of_surrounding_prose()
    {
        var raw = "Sure, here's the classification:\n{ \"kind\": \"movie\", \"title\": \"Heat\" }\nLet me know if you need more.";
        var json = LegionFallbackParser.ExtractJsonObject(raw);
        Assert.That(json, Is.EqualTo("{ \"kind\": \"movie\", \"title\": \"Heat\" }"));
    }

    [Test]
    public void ExtractJsonObject_skips_echoed_schema_and_takes_first_balanced_object()
    {
        // Some providers echo the schema object back before the real answer —
        // a naive first-brace-to-last-brace slice would merge both into
        // invalid JSON. Depth tracking must close out the FIRST object.
        var raw = """
            Schema:
              { "kind": "movie" | "tv_season" | "unknown", "title": "<title>", "year": null, "season": null }
            Answer:
              { "kind": "movie", "title": "Heat", "year": 1995, "season": null }
            """;
        var json = LegionFallbackParser.ExtractJsonObject(raw);
        Assert.That(json, Does.StartWith("{ \"kind\": \"movie\" | \"tv_season\""));
    }

    [Test]
    public void ExtractJsonObject_handles_braces_inside_string_values()
    {
        // A title like "Spinal Tap {Live}" must not throw off the depth count.
        var raw = """{ "kind": "movie", "title": "Spinal Tap {Live}", "year": 1984, "season": null }""";
        var json = LegionFallbackParser.ExtractJsonObject(raw);
        Assert.That(json, Is.EqualTo(raw));
    }

    [Test]
    public void ExtractJsonObject_handles_escaped_quotes_inside_string_values()
    {
        var raw = """{ "kind": "movie", "title": "The \"Best\" Movie", "year": 1999, "season": null }""";
        var json = LegionFallbackParser.ExtractJsonObject(raw);
        Assert.That(json, Is.EqualTo(raw));
    }

    [Test]
    public void ExtractJsonObject_returns_null_for_unterminated_object()
    {
        var json = LegionFallbackParser.ExtractJsonObject("""{ "kind": "movie", "title": "Heat" """);
        Assert.That(json, Is.Null);
    }

    [Test]
    public void ExtractJsonObject_returns_null_when_no_object_present()
    {
        var json = LegionFallbackParser.ExtractJsonObject("I'm not sure, sorry.");
        Assert.That(json, Is.Null);
    }

    [Test]
    public void ExtractJsonObject_returns_null_for_empty_input()
    {
        Assert.That(LegionFallbackParser.ExtractJsonObject(""), Is.Null);
        Assert.That(LegionFallbackParser.ExtractJsonObject("   "), Is.Null);
    }
}
