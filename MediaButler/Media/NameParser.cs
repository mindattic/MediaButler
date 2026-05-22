using System.Text.RegularExpressions;

namespace MediaButler.Media;

/// <summary>
/// Pattern recognition for messy torrent folder names. Each helper has been
/// validated against the canonical pitfall set from the manual run:
///
/// <list type="bullet">
///   <item><c>Better Call Saul Season 1 Complete 1980 x 1080 x264 Phun Psyz</c></item>
///   <item><c>Better.Call.Saul.S05.Complete.1080p.NF.WEBRip.DD+5.1.x264-AJP69</c></item>
///   <item><c>Blindspot.SEASON.01.S01.COMPLETE.720p.WEB-DL.2CH.x265.HEVC-PSA</c></item>
///   <item><c>Bones Complete Series S1-S12 x264 406p + English Subs (MP4)</c></item>
///   <item><c>Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 ...)</c></item>
///   <item><c>Law.And.Order.Organized.Crime.S01.COMPLETE.720p.AMZN.WEBRip.x264-GalaxyTV[TGx]</c></item>
///   <item><c>Sherlock.Season.1-4.S01-S04.1080p.10bit.BluRay.5.1.x265.HEVC-MZABI</c></item>
///   <item><c>The Following 2013 Seasons 1 to 3 Complete 720p WEB x264 [i_c]</c></item>
///   <item><c>Wake.Up.Dead.Man.A.Knives.Out.Mystery.2025.2160p.NF.WEB-DL...</c></item>
///   <item><c>Heat (1995) [1080p]</c>, <c>Sicario (2015) [2160p] [4K] [BluRay] [5.1] [YTS.MX]</c></item>
/// </list>
/// </summary>
public static class NameParser
{
    // S01-S05, S1-S12 — used both for detecting multi-season parents and rejecting that match in single-season parse.
    private static readonly Regex MultiSeasonRange = new(
        @"\bS\d{1,2}\s*-\s*S\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Seasons 1-5", "Seasons 1 to 3", "Season 1-3"
    private static readonly Regex SeasonRange = new(
        @"\bSeasons?\s+\d{1,2}\s*(?:-|to)\s*\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Complete Series"
    private static readonly Regex CompleteSeries = new(
        @"\bComplete\s+Series\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Episode marker — used to reject "S01E01" patterns when looking for season-only names
    private static readonly Regex EpisodeMarker = new(
        @"\bS\d{1,2}E\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Captures a single Season N or S0N (NOT followed by E\d). Allows leading zero.
    // The non-S-anchor variants (Season N) are tried before bare SN so "Season 5" wins over "S5" inside "Season 5".
    private static readonly Regex SeasonNumber = new(
        @"\b(?:Season\s+|S(?!\d{1,2}E\d{1,2}))0*(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Season 1" / "Season 12" / "Season.1" — used when scanning nested subfolders ("Season 1", "Season 10").
    private static readonly Regex BareSeasonFolder = new(
        @"^(?:Season|S)\s*0*(\d{1,2})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Anywhere in name: parenthesised or bare 19xx/20xx
    private static readonly Regex YearAnywhere = new(
        @"(?:\(((?:19|20)\d{2})\)|\b((?:19|20)\d{2})\b)",
        RegexOptions.Compiled);

    // Tail tags FileBot/Plex shouldn't see in a clean title
    private static readonly Regex TrailingJunk = new(
        @"\s+(?:Complete|COMPLETE|1080p|720p|2160p|4K|BluRay|BRrip|BDRip|WEBRip|WEB-DL|HDTV|x264|x265|HEVC|H\.?264|H\.?265|AAC|AC3|DTS(?:-?HDMA)?|DD\+?5\.1|DDP5\.1|Atmos|MULTi|REMASTERED|PROPER|REPACK|NF|AMZN|HDR|DV|HMAX|MAX).*$",
        RegexOptions.Compiled);

    /// <summary>Replace dot/underscore separators with spaces and collapse whitespace.</summary>
    public static string Normalize(string name)
    {
        var n = name.Replace('.', ' ').Replace('_', ' ');
        n = Regex.Replace(n, @"\s+", " ");
        return n.Trim();
    }

    /// <summary>True if the folder name itself signals a multi-season dump (range markers, "Complete Series").</summary>
    public static bool LooksLikeMultiSeason(string folderName)
    {
        var n = Normalize(folderName);
        return MultiSeasonRange.IsMatch(n) || SeasonRange.IsMatch(n) || CompleteSeries.IsMatch(n);
    }

    /// <summary>True if a name contains any season marker (single or range).</summary>
    public static bool HasAnySeasonMarker(string folderName)
    {
        var n = Normalize(folderName);
        return SeasonNumber.IsMatch(n) || LooksLikeMultiSeason(n);
    }

    /// <summary>
    /// Parse a single-season folder name. Returns null if no season marker is found.
    /// Rejects multi-season range patterns first so "Seasons 1-5" doesn't return season 1.
    /// </summary>
    public static (string Show, int Season)? ParseSingleSeason(string folderName)
    {
        var n = Normalize(folderName);
        if (MultiSeasonRange.IsMatch(n) || SeasonRange.IsMatch(n)) return null;

        var m = SeasonNumber.Match(n);
        if (!m.Success) return null;

        var seasonNum = int.Parse(m.Groups[1].Value);
        var showPart = n[..m.Index].Trim();
        var show = CleanShowName(showPart);
        return string.IsNullOrWhiteSpace(show) ? null : (show, seasonNum);
    }

    /// <summary>
    /// Parse a multi-season parent name to recover the show. Strips the range
    /// marker ("S01-S05", "Seasons 1-5", "Complete Series") plus any trailing
    /// year, then cleans junk tags.
    /// </summary>
    public static string? ParseMultiSeasonParent(string folderName)
    {
        var n = Normalize(folderName);
        // Cut at the earliest range/series marker.
        var idx = -1;
        foreach (var r in new[] { MultiSeasonRange, SeasonRange, CompleteSeries })
        {
            var m = r.Match(n);
            if (m.Success && (idx == -1 || m.Index < idx)) idx = m.Index;
        }
        // Some parents only signal multi-season via subfolder structure, not name.
        // In that case keep the whole normalized string and let CleanShowName trim.
        var show = idx >= 0 ? n[..idx].Trim() : n;
        show = CleanShowName(show);
        return string.IsNullOrWhiteSpace(show) ? null : show;
    }

    /// <summary>
    /// Returns the season number if <paramref name="subfolderName"/> looks like a
    /// nested-season subfolder (e.g. "Season 1", "Season 10", "S01",
    /// "Sherlock.Season.1.S01.1080p..."). Otherwise null.
    /// </summary>
    public static int? ParseNestedSeasonName(string subfolderName)
    {
        // Bare "Season N" / "SN"
        var m = BareSeasonFolder.Match(subfolderName.Trim());
        if (m.Success) return int.Parse(m.Groups[1].Value);

        // "Sherlock.Season.1.S01.1080p..." — find first S0N or "Season N"
        var n = Normalize(subfolderName);
        var sm = SeasonNumber.Match(n);
        if (sm.Success) return int.Parse(sm.Groups[1].Value);
        return null;
    }

    /// <summary>
    /// Parse a movie folder name into Title + optional Year. Year is what
    /// disambiguates "Heat (1995)" from a TV show called "Heat".
    /// </summary>
    public static (string Title, int? Year) ParseMovie(string folderName)
    {
        var n = Normalize(folderName);
        // Strip bracket-tags first ("[YTS.MX]", "[i_c]") — they often follow the title.
        n = Regex.Replace(n, @"\[[^\]]*\]", " ").Trim();
        n = Regex.Replace(n, @"\s+", " ");

        var yearMatch = YearAnywhere.Match(n);
        int? year = null;
        var titleEnd = n.Length;
        if (yearMatch.Success)
        {
            var yv = yearMatch.Groups[1].Success ? yearMatch.Groups[1].Value : yearMatch.Groups[2].Value;
            year = int.Parse(yv);
            titleEnd = yearMatch.Index;
        }

        var title = n[..titleEnd];
        title = TrailingJunk.Replace(title, "");
        // Final cleanups: strip stray parens/brackets/dashes.
        title = Regex.Replace(title, @"[\(\)\[\]]+", " ");
        title = Regex.Replace(title, @"\s+", " ").Trim().TrimEnd('-').Trim();
        return (title, year);
    }

    /// <summary>Cleans up a show-name remnant after the season marker was sliced off.</summary>
    public static string CleanShowName(string raw)
    {
        var n = raw;
        // Strip trailing "(YYYY)" or " YYYY" — keeps "The Following" out of "The Following 2013".
        n = Regex.Replace(n, @"\s*\((19|20)\d{2}\)\s*$", "");
        n = Regex.Replace(n, @"\s+(19|20)\d{2}\s*$", "");
        // Strip dangling bracket tags
        n = Regex.Replace(n, @"\[[^\]]*\]", " ");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n;
    }

    /// <summary>
    /// MediaButler's canonical pre-FileBot TV folder name: <c>{Show} - Season 01</c>.
    /// Always two-digit padded.
    /// </summary>
    public static string FormatSeasonFolder(string show, int season) =>
        $"{show} - Season {season:D2}";

    /// <summary>MediaButler's canonical pre-FileBot movie folder name: <c>{Title} (YYYY)</c>.</summary>
    public static string FormatMovieFolder(string title, int? year) =>
        year.HasValue ? $"{title} ({year.Value})" : title;

    /// <summary>True if the file name contains an SxxEyy pattern.</summary>
    public static bool LooksLikeEpisodeFile(string fileName) => EpisodeMarker.IsMatch(fileName);
}
