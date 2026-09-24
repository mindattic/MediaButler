namespace MediaButler.Tests.Fixtures;

/// <summary>
/// This project's canonical TV/Movie library layout. Every test fixture in
/// <see cref="PathologicalLibrary"/> declares an "expected" output that
/// conforms to these rules, and tests assert MediaButler produces (or stages
/// FileBot to produce) the same.
///
/// <para><b>TV Shows</b> — deliberately flat, matching the user's existing
/// library: a season folder lives directly under the TV library root, with
/// no per-show container folder (Plex's own docs describe a nested
/// <c>Show\Season NN</c> layout as preferred, but the user's library predates
/// that and flat also matches fine).</para>
/// <list type="bullet">
///   <item>Library root: <c>/TV Shows</c> (or per user setting).</item>
///   <item>Season folder: <c>Show Name - Season NN</c>, or
///         <c>Show Name (Year) - Season NN</c> once reboot disambiguation has
///         kicked in for a show name shared by two different series.
///         Zero-padded, two digits.</item>
///   <item>Specials: <c>Season 00</c>.</item>
///   <item>Episode file: <c>Show - SxxEyy - Episode Title.ext</c> or
///         <c>Show.SxxEyy.Episode.Title.ext</c>. FileBot's <c>{n} - {s00e00} - {t}</c>
///         format matches the first variant exactly.</item>
///   <item>Artwork (poster.jpg / banner.jpg / fanart.jpg / tvshow.nfo / ...)
///         lives inside the season folder itself, not hoisted to a show root.</item>
/// </list>
///
/// <para><b>Movies</b></para>
/// <list type="bullet">
///   <item>Library root: <c>/Movies</c>.</item>
///   <item>Movie folder: <c>Movie Title (Year)</c>. Year in parens is the
///         disambiguator and is strongly preferred.</item>
///   <item>Primary file: <c>Movie Title (Year).ext</c> inside the movie folder.</item>
///   <item>Movie-level art: <c>poster.jpg</c>, <c>fanart.jpg</c>,
///         <c>Movie Title (Year).nfo</c>.</item>
///   <item>Extras live in named subfolders under the movie folder:
///         <c>Behind The Scenes</c>, <c>Deleted Scenes</c>, <c>Featurettes</c>,
///         <c>Interviews</c>, <c>Scenes</c>, <c>Shorts</c>, <c>Trailers</c>, <c>Other</c>.</item>
/// </list>
///
/// <para><b>Things that BREAK Plex matching</b> — these are the test failure modes
/// the pathological fixture is designed to exercise:</para>
/// <list type="bullet">
///   <item>Multiple shows in one parent folder ("Bones Complete Series").</item>
///   <item>Release-group tags in the title (<c>YIFY</c>, <c>x265-PSA</c>).</item>
///   <item>Bare year prefix that looks like a TV identifier (<c>1917 (2019)</c>).</item>
///   <item>Year-in-title shadowing release year (<c>Blade Runner 2049</c>).</item>
///   <item>Extras/Specials subfolders being misread as Season 0 or movies.</item>
///   <item>ISO/IFO disc rips with no recognisable container extension.</item>
/// </list>
/// </summary>
public static class PlexStandard
{
    /// <summary>The flat season-folder name: "Show Name - Season NN", or
    /// "Show Name (Year) - Season NN" once reboot disambiguation applies.</summary>
    public static string SeasonFolder(string show, int season, int? showYear = null) =>
        showYear is null
            ? $"{show} - Season {season:D2}"
            : $"{show} ({showYear.Value}) - Season {season:D2}";

    /// <summary>Movie folder: "Heat (1995)". The year is required for canonical Plex layout.</summary>
    public static string MovieFolder(string title, int year) => $"{title} ({year})";

    /// <summary>Full path to a season folder, flat under the TV library root.</summary>
    public static string TvSeasonPath(string tvRoot, string show, int season, int? showYear = null) =>
        Path.Combine(tvRoot, SeasonFolder(show, season, showYear));

    /// <summary>Plex-canonical full path to a movie folder.</summary>
    public static string MoviePath(string moviesRoot, string title, int year) =>
        Path.Combine(moviesRoot, MovieFolder(title, year));

    /// <summary>Extras subfolder names Plex recognises under a movie folder.</summary>
    public static readonly string[] MovieExtrasFolders =
    {
        "Behind The Scenes", "Deleted Scenes", "Featurettes",
        "Interviews", "Scenes", "Shorts", "Trailers", "Other",
    };
}
