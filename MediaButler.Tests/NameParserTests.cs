using MediaButler.Media;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Regression tests for <see cref="NameParser"/>. Cases are paired one-to-one
/// with the canonical pitfall set in the README and in the parser's own XML doc
/// — when a new dirty-name pattern is added to the codebase, add a row here.
/// </summary>
[TestFixture]
public class NameParserTests
{
    // ---- Normalize ---------------------------------------------------------

    [TestCase("Better.Call.Saul.S05", "Better Call Saul S05")]
    [TestCase("foo_bar__baz",         "foo bar baz")]
    [TestCase("   spaced   out   ",   "spaced out")]
    [TestCase("[YTS.MX] - Heat (1995)", "Heat (1995)")]                // index/group prefix stripped
    [TestCase("www.UIndex.org - A Knight of the Seven Kingdoms S01E01", "A Knight of the Seven Kingdoms S01E01")]
    public void Normalize_collapses_separators_and_strips_index_prefix(string input, string expected) =>
        Assert.That(NameParser.Normalize(input), Is.EqualTo(expected));

    // ---- LooksLikeMultiSeason ---------------------------------------------

    [TestCase("Bones Complete Series S1-S12 x264 406p + English Subs (MP4)", true)]
    [TestCase("Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 ...)", true)]
    [TestCase("Sherlock.Season.1-4.S01-S04.1080p.10bit.BluRay.5.1.x265.HEVC-MZABI", true)]
    [TestCase("The Following 2013 Seasons 1 to 3 Complete 720p WEB x264 [i_c]", true)]
    [TestCase("Better.Call.Saul.S05.Complete.1080p.NF.WEBRip.DD+5.1.x264-AJP69", false)]
    [TestCase("Heat (1995) [1080p]", false)]
    public void LooksLikeMultiSeason_detects_range_or_complete_series(string input, bool expected) =>
        Assert.That(NameParser.LooksLikeMultiSeason(input), Is.EqualTo(expected));

    // ---- LooksLikeExtras ---------------------------------------------------

    [TestCase("Extras",                                  true)]
    [TestCase("The Venture Bros. - Extras",              true)]
    [TestCase("The Venture Bros. - Specials",            true)]
    [TestCase("Show Name - Bonus",                       true)]
    [TestCase("The Venture Bros. - Season 01",           false)]
    [TestCase("Heat (1995)",                             false)]
    public void LooksLikeExtras_detects_companion_folders(string input, bool expected) =>
        Assert.That(NameParser.LooksLikeExtras(input), Is.EqualTo(expected));

    // ---- ParseSingleSeason -------------------------------------------------

    [TestCase("Better Call Saul Season 1 Complete 1980 x 1080 x264 Phun Psyz", "Better Call Saul", 1)]
    [TestCase("Better.Call.Saul.S05.Complete.1080p.NF.WEBRip.DD+5.1.x264-AJP69", "Better Call Saul", 5)]
    [TestCase("Blindspot.SEASON.01.S01.COMPLETE.720p.WEB-DL.2CH.x265.HEVC-PSA", "Blindspot",        1)]
    [TestCase("Law.And.Order.Organized.Crime.S01.COMPLETE.720p.AMZN.WEBRip.x264-GalaxyTV[TGx]", "Law And Order Organized Crime", 1)]
    [TestCase("The Mentalist - Season 04",                                     "The Mentalist",    4)]
    [TestCase("The Penguin Season 01",                                         "The Penguin",      1)]
    [TestCase("True Detective Season 02",                                      "True Detective",   2)]
    [TestCase("Twin.Peaks.SEASON.01.S01.COMPLETE.1080p.10bit.BluRay.6CH.x265.HEVC-PSA", "Twin Peaks", 1)]
    public void ParseSingleSeason_extracts_show_and_season(string input, string expectedShow, int expectedSeason)
    {
        var parsed = NameParser.ParseSingleSeason(input);
        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Season, Is.EqualTo(expectedSeason));
            Assert.That(parsed.Value.Show,    Is.EqualTo(expectedShow));
        });
    }

    [TestCase("Bones Complete Series S1-S12 x264 406p + English Subs (MP4)")]
    [TestCase("Sherlock.Season.1-4.S01-S04.1080p.10bit.BluRay.5.1.x265.HEVC-MZABI")]
    [TestCase("The Following 2013 Seasons 1 to 3 Complete 720p WEB x264 [i_c]")]
    public void ParseSingleSeason_rejects_multi_season_dumps(string input) =>
        Assert.That(NameParser.ParseSingleSeason(input), Is.Null);

    // ---- ParseMultiSeasonParent --------------------------------------------

    [TestCase("Bones Complete Series S1-S12 x264 406p + English Subs (MP4)", "Bones")]
    [TestCase("Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 ...)", "Breaking Bad")]
    [TestCase("The Following 2013 Seasons 1 to 3 Complete 720p WEB x264 [i_c]", "The Following")]
    [TestCase("The Sopranos - The Complete Series (Season 1, 2, 3, 4, 5 & 6) + Extras", "The Sopranos - The")]
    public void ParseMultiSeasonParent_recovers_show_name(string input, string expectedPrefix)
    {
        var show = NameParser.ParseMultiSeasonParent(input);
        Assert.That(show, Is.Not.Null);
        Assert.That(show, Does.StartWith(expectedPrefix));
    }

    // ---- ParseNestedSeasonName ---------------------------------------------

    [TestCase("Season 1",  1)]
    [TestCase("Season 10", 10)]
    [TestCase("S01",       1)]
    [TestCase("Sherlock.Season.1.S01.1080p.10bit.BluRay.5.1.x265.HEVC-MZABI", 1)]
    public void ParseNestedSeasonName_handles_bare_and_nested_forms(string input, int expected) =>
        Assert.That(NameParser.ParseNestedSeasonName(input), Is.EqualTo(expected));

    [TestCase("Extras")]
    [TestCase("Bonus Disc")]
    [TestCase("Some Random Folder")]
    public void ParseNestedSeasonName_returns_null_for_non_season_folders(string input) =>
        Assert.That(NameParser.ParseNestedSeasonName(input), Is.Null);

    // ---- ParseMovie --------------------------------------------------------

    [TestCase("Heat (1995) [1080p]",                                                                "Heat",                        1995)]
    [TestCase("Sicario (2015) [2160p] [4K] [BluRay] [5.1] [YTS.MX]",                                "Sicario",                      2015)]
    [TestCase("Wake.Up.Dead.Man.A.Knives.Out.Mystery.2025.2160p.NF.WEB-DL.DDP5.1.Atmos.DV.HDR.x265.10bit-RAWR", "Wake Up Dead Man A Knives Out Mystery", 2025)]
    [TestCase("The.Roast.of.Tom.Brady.2024",                                                        "The Roast of Tom Brady",       2024)]
    [TestCase("Once 2007",                                                                          "Once",                          2007)]
    [TestCase("Prospect.2018.1080p.BluRay.x264-VETO[EtHD]",                                         "Prospect",                      2018)]
    public void ParseMovie_extracts_title_and_year(string input, string expectedTitle, int expectedYear)
    {
        var (title, year) = NameParser.ParseMovie(input);
        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo(expectedTitle));
            Assert.That(year,  Is.EqualTo(expectedYear));
        });
    }

    /// <summary>
    /// Regression: titles that start with a 4-digit year-shaped number
    /// (<c>1917 (2019)</c>, <c>2009 Lost Memories (2002)</c>) must not have the
    /// leading number eaten as the release year. Surfaced by a dry-run scan
    /// against the user's real Movies library.
    /// </summary>
    [TestCase("1917 (2019)",               "1917",               2019)]
    [TestCase("2009 Lost Memories (2002)", "2009 Lost Memories", 2002)]
    public void ParseMovie_prefers_parenthesised_year_over_bare_leading_year(string input, string expectedTitle, int expectedYear)
    {
        var (title, year) = NameParser.ParseMovie(input);
        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo(expectedTitle));
            Assert.That(year,  Is.EqualTo(expectedYear));
        });
    }

    /// <summary>
    /// Titles whose own text contains a year-shaped number that is NOT the
    /// release year. Without the override list the parser eats the number as
    /// the year. The override should preserve the title verbatim and pick up
    /// any parenthesised release year that follows.
    /// </summary>
    [TestCase("Blade.Runner.2049",         "Blade Runner 2049", null)]
    [TestCase("Blade Runner 2049 (2017)",  "Blade Runner 2049", 2017)]
    [TestCase("Wonder Woman 1984",         "Wonder Woman 1984", null)]
    [TestCase("Wonder Woman 1984 (2020)",  "Wonder Woman 1984", 2020)]
    [TestCase("2001 A Space Odyssey",      "2001 A Space Odyssey", null)]
    [TestCase("2001 A Space Odyssey (1968)", "2001 A Space Odyssey", 1968)]
    // Bare (non-parenthesised) release year after an override is still captured,
    // and quality tags (1080p/2160p) must not be mistaken for the year.
    [TestCase("Blade Runner 2049 2017",            "Blade Runner 2049", 2017)]
    [TestCase("Wonder Woman 1984 2020 1080p BluRay", "Wonder Woman 1984", 2020)]
    [TestCase("Blade Runner 2049 1080p BluRay",    "Blade Runner 2049", null)]
    public void ParseMovie_respects_title_year_overrides(string input, string expectedTitle, int? expectedYear)
    {
        var overrides = new MediaButler.Settings.MediaButlerSettings().TitleYearOverrides;
        var (title, year) = NameParser.ParseMovie(input, overrides);
        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo(expectedTitle));
            Assert.That(year,  Is.EqualTo(expectedYear));
        });
    }

    [Test]
    public void ParseMovie_override_does_not_match_substrings()
    {
        // "Blade Runner 2049" override must NOT match a folder that just happens
        // to contain those words as a prefix to a different title.
        var overrides = new[] { "Blade Runner 2049" };
        var (title, _) = NameParser.ParseMovie("Blade Runner 2049Theatrical (2017)", overrides);
        // Without a word boundary the override should NOT match — fall through
        // to the generic year parser, which uses the parenthesised 2017.
        Assert.That(title, Is.Not.EqualTo("Blade Runner 2049"));
    }

    [TestCase("Oppenheimer")]
    [TestCase("Pee-Wee's Big Adventure")]
    [TestCase("On the Beach at Night Alone")]
    public void ParseMovie_handles_year_less_titles(string input)
    {
        var (title, year) = NameParser.ParseMovie(input);
        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo(input));
            Assert.That(year,  Is.Null);
        });
    }

    // ---- Format / idempotency ---------------------------------------------

    [TestCase("Better Call Saul", 5, "Better Call Saul - Season 05")]
    [TestCase("Bones",            12, "Bones - Season 12")]
    public void FormatSeasonFolder_pads_to_two_digits(string show, int season, string expected) =>
        Assert.That(NameParser.FormatSeasonFolder(show, season), Is.EqualTo(expected));

    [TestCase("Okja", 2017, "Okja (2017)")]
    [TestCase("Oppenheimer", null, "Oppenheimer")]
    public void FormatMovieFolder_omits_year_when_null(string title, int? year, string expected) =>
        Assert.That(NameParser.FormatMovieFolder(title, year), Is.EqualTo(expected));

    /// <summary>
    /// Critical safety property: running the rename pass twice must be a no-op.
    /// If FormatSeasonFolder ≠ ParseSingleSeason(FormatSeasonFolder(...)),
    /// repeated runs would keep renaming and eventually destroy data.
    /// </summary>
    [TestCase("Better Call Saul", 5)]
    [TestCase("The X-Files",      9)]
    [TestCase("Twin Peaks",       1)]
    [TestCase("The Mentalist",    4)]
    public void FormatSeasonFolder_round_trips_through_ParseSingleSeason(string show, int season)
    {
        var formatted = NameParser.FormatSeasonFolder(show, season);
        var parsed    = NameParser.ParseSingleSeason(formatted);
        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Show,   Is.EqualTo(show));
            Assert.That(parsed.Value.Season,  Is.EqualTo(season));
            Assert.That(NameParser.FormatSeasonFolder(parsed.Value.Show, parsed.Value.Season),
                Is.EqualTo(formatted));
        });
    }

    [TestCase("Heat (1995)")]
    [TestCase("Okja (2017)")]
    [TestCase("The Roast of Tom Brady (2024)")]
    public void FormatMovieFolder_round_trips_through_ParseMovie(string canonical)
    {
        var (title, year) = NameParser.ParseMovie(canonical);
        Assert.That(NameParser.FormatMovieFolder(title, year), Is.EqualTo(canonical));
    }
}
