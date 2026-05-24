namespace MediaButler.Tests.Fixtures;

/// <summary>
/// Materializes a temporary on-disk source library full of the messiest
/// folder names MediaButler has to deal with. The list is the canonical
/// pitfall set from <c>NameParser.cs</c>'s docstring plus a few additions
/// for nesting, art-only/orphan shells, and Extras subfolders.
///
/// <para>Each <see cref="FixtureCase"/> declares the input folder name AND
/// the Plex-canonical layout MediaButler is expected to produce (or stage
/// for FileBot). Tests assert that the actual post-pipeline state matches
/// the expected target path — divergence is either a bug to fix or a
/// documented design choice to surface.</para>
///
/// <para>The dummy <c>.mkv</c> files written into each folder are zero-byte —
/// the scanner only looks at extensions, not content, so we don't need real
/// video bytes to drive classification or move decisions.</para>
/// </summary>
public sealed class PathologicalLibrary : IDisposable
{
    public string Root { get; }
    public string TvDestination { get; }
    public string MoviesDestination { get; }

    private readonly string sourceDir;

    public PathologicalLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "mediabutler-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        sourceDir         = Path.Combine(Root, "source");
        TvDestination     = Path.Combine(Root, "tv");
        MoviesDestination = Path.Combine(Root, "movies");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(TvDestination);
        Directory.CreateDirectory(MoviesDestination);
    }

    /// <summary>The fully-built source root that MediaButler should scan.</summary>
    public string SourcePath => sourceDir;

    /// <summary>Materialize every case into the source root and return the case manifest.</summary>
    public IReadOnlyList<FixtureCase> Populate()
    {
        var cases = BuildCases();
        foreach (var c in cases) Materialize(c);
        return cases;
    }

    /// <summary>The exhaustive pathological case list. Edit here when a new pitfall is discovered.</summary>
    public static IReadOnlyList<FixtureCase> BuildCases() => new[]
    {
        // ---------- TV: single-season variants from NameParser docstring ----------
        new FixtureCase(
            Input: "Better Call Saul Season 1 Complete 1980 x 1080 x264 Phun Psyz",
            Kind:  ExpectedKind.TvSeason,
            Show:  "Better Call Saul",
            Season: 1,
            DummyFiles: new[] { "BCS.S01E01.mkv", "BCS.S01E02.mkv" }),

        new FixtureCase(
            Input: "Better.Call.Saul.S05.Complete.1080p.NF.WEBRip.DD+5.1.x264-AJP69",
            Kind:  ExpectedKind.TvSeason,
            Show:  "Better Call Saul",
            Season: 5,
            DummyFiles: new[] { "ep.S05E01.mkv" }),

        new FixtureCase(
            Input: "Blindspot.SEASON.01.S01.COMPLETE.720p.WEB-DL.2CH.x265.HEVC-PSA",
            Kind:  ExpectedKind.TvSeason,
            Show:  "Blindspot",
            Season: 1,
            DummyFiles: new[] { "Blindspot.S01E01.mkv" }),

        new FixtureCase(
            Input: "Law.And.Order.Organized.Crime.S01.COMPLETE.720p.AMZN.WEBRip.x264-GalaxyTV[TGx]",
            Kind:  ExpectedKind.TvSeason,
            Show:  "Law And Order Organized Crime",
            Season: 1,
            DummyFiles: new[] { "LOC.S01E01.mkv" }),

        // ---------- TV: multi-season parents (need to be hoisted into separate seasons) ----------
        new FixtureCase(
            Input: "Bones Complete Series S1-S12 x264 406p + English Subs (MP4)",
            Kind:  ExpectedKind.MultiSeasonParent,
            Show:  "Bones",
            NestedSeasonsToCreate: new[] { 1, 2, 3 }, // not all 12 — keep fixture small
            DummyFiles: new[] { "Bones_Large.jpg", "Info.txt" }), // orphan show-level files at parent

        new FixtureCase(
            Input: "Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 ...)",
            Kind:  ExpectedKind.MultiSeasonParent,
            Show:  "Breaking Bad",
            NestedSeasonsToCreate: new[] { 1, 2 }),

        new FixtureCase(
            Input: "Sherlock.Season.1-4.S01-S04.1080p.10bit.BluRay.5.1.x265.HEVC-MZABI",
            Kind:  ExpectedKind.MultiSeasonParent,
            Show:  "Sherlock",
            NestedSeasonsToCreate: new[] { 1, 4 }),

        new FixtureCase(
            Input: "The Following 2013 Seasons 1 to 3 Complete 720p WEB x264 [i_c]",
            Kind:  ExpectedKind.MultiSeasonParent,
            Show:  "The Following",
            NestedSeasonsToCreate: new[] { 1, 2, 3 }),

        // ---------- Movies: from NameParser docstring ----------
        new FixtureCase(
            Input: "Wake.Up.Dead.Man.A.Knives.Out.Mystery.2025.2160p.NF.WEB-DL",
            Kind:  ExpectedKind.Movie,
            MovieTitle: "Wake Up Dead Man A Knives Out Mystery",
            Year:  2025,
            DummyFiles: new[] { "movie.mkv" }),

        new FixtureCase(
            Input: "Heat (1995) [1080p]",
            Kind:  ExpectedKind.Movie,
            MovieTitle: "Heat",
            Year:  1995,
            DummyFiles: new[] { "Heat.1995.YIFY.mp4" }),

        new FixtureCase(
            Input: "Sicario (2015) [2160p] [4K] [BluRay] [5.1] [YTS.MX]",
            Kind:  ExpectedKind.Movie,
            MovieTitle: "Sicario",
            Year:  2015,
            DummyFiles: new[] { "Sicario.4K.mkv" }),

        // ---------- Year-in-title trap ----------
        new FixtureCase(
            Input: "Blade Runner 2049 (2017) [1080p]",
            Kind:  ExpectedKind.Movie,
            MovieTitle: "Blade Runner 2049",
            Year:  2017,
            DummyFiles: new[] { "BR2049.mkv" }),

        // ---------- Extras / Specials companion folder (NOT a movie) ----------
        new FixtureCase(
            Input: "The Venture Bros - Extras",
            Kind:  ExpectedKind.Extras,
            DummyFiles: new[] { "behind-the-scenes.mkv" }),

        // ---------- Empty shell (no video) — Rename stage should delete it ----------
        new FixtureCase(
            Input: "Some Empty Torrent Shell (1080p)",
            Kind:  ExpectedKind.Empty,
            DummyFiles: new[] { "readme.txt" }), // < safety threshold

        // ---------- Empty with too-much non-video content — should be REFUSED for deletion ----------
        new FixtureCase(
            Input: "Suspicious Big Non-Video Folder",
            Kind:  ExpectedKind.EmptyButLarge,
            DummyFiles: new[] { "huge-mystery-blob.bin" },
            BigFileBytes: 2L * 1024 * 1024), // 2 MB > 1 MB safety floor

        // NOTE: Producing an "Unknown" classification in practice requires a
        // folder with video AND a season marker that ParseSingleSeason fails on
        // — a vanishingly rare real-world shape. The scanner's design is to
        // classify any video-bearing folder as a movie by default. We do not
        // construct an Unknown fixture case because doing so requires a
        // pathological-pathological input that doesn't reflect real torrent
        // dumps.
    };

    private void Materialize(FixtureCase c)
    {
        var folder = Path.Combine(sourceDir, c.Input);
        Directory.CreateDirectory(folder);

        foreach (var fname in c.DummyFiles)
        {
            var path = Path.Combine(folder, fname);
            if (c.BigFileBytes.HasValue && fname == c.DummyFiles[^1])
            {
                // Write a sized file for the EmptyButLarge case.
                using var fs = File.Create(path);
                fs.SetLength(c.BigFileBytes.Value);
            }
            else
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
            }
        }

        // Nested season subfolders for multi-season parents — each holds a
        // dummy episode so HasAnyVideo returns true.
        foreach (var season in c.NestedSeasonsToCreate)
        {
            var sub = Path.Combine(folder, $"Season {season:D2}");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, $"ep.S{season:D2}E01.mkv"), Array.Empty<byte>());
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }
}

/// <summary>One pathological input + the classification/destination we expect MediaButler to produce.</summary>
public sealed record FixtureCase(
    string Input,
    ExpectedKind Kind,
    string? Show = null,
    int? Season = null,
    string? MovieTitle = null,
    int? Year = null,
    string[]? DummyFiles = null,
    int[]? NestedSeasonsToCreate = null,
    long? BigFileBytes = null)
{
    public string[] DummyFiles { get; } = DummyFiles ?? Array.Empty<string>();
    public int[] NestedSeasonsToCreate { get; } = NestedSeasonsToCreate ?? Array.Empty<int>();
}

/// <summary>
/// Classification categories the test fixture asserts. Maps to
/// <c>MediaButler.Media.MediaKind</c> but adds a synthetic
/// <c>EmptyButLarge</c> bucket for the "refused delete" safety case.
/// </summary>
public enum ExpectedKind
{
    TvSeason,
    MultiSeasonParent,
    Movie,
    Extras,
    Empty,
    EmptyButLarge,
    Unknown,
}
