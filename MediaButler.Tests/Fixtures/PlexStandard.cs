namespace MediaButler.Tests.Fixtures;

/// <summary>
/// Canonical Plex Media Server library layout. This is the gold standard —
/// every test fixture in <see cref="PathologicalLibrary"/> declares an
/// "expected" output that conforms to these rules, and tests assert
/// MediaButler produces (or stages FileBot to produce) the same.
///
/// <para>Source: Plex support docs "Naming Series-Based / Movie Files",
/// "Local Media Assets - Movies / TV Shows", and the Plex Agent for TVDB/TMDB
/// matching rules. Captured here so tests don't drift if those URLs change.</para>
///
/// <para><b>TV Shows</b></para>
/// <list type="bullet">
///   <item>Library root: <c>/TV Shows</c> (or per user setting).</item>
///   <item>Show folder: <c>Show Name</c> or <c>Show Name (Year)</c>. The year
///         is recommended whenever two shows share a title.</item>
///   <item>Season folder: <c>Season 01</c> (zero-padded, two digits) is the
///         preferred Plex layout. <c>Season 1</c> also matches but loses
///         predictable sort order on file managers.</item>
///   <item>Specials: <c>Season 00</c>.</item>
///   <item>Episode file: <c>Show - SxxEyy - Episode Title.ext</c> or
///         <c>Show.SxxEyy.Episode.Title.ext</c>. FileBot's <c>{n} - {s00e00} - {t}</c>
///         format matches the first variant exactly.</item>
///   <item>Show-level art lives at the show folder root: <c>poster.jpg</c>,
///         <c>banner.jpg</c>, <c>fanart.jpg</c>, <c>tvshow.nfo</c>.</item>
///   <item>Season-level art also lives at the show folder root, prefixed:
///         <c>season01-poster.jpg</c>, <c>season01-fanart.jpg</c>.</item>
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
    /// <summary>The padded season-folder name Plex prefers: "Season 01".</summary>
    public static string SeasonFolder(int season) => $"Season {season:D2}";

    /// <summary>Show folder with optional year disambiguator: "Heat" or "Heat (2012)".</summary>
    public static string ShowFolder(string show, int? year = null) =>
        year is null ? show : $"{show} ({year.Value})";

    /// <summary>Movie folder: "Heat (1995)". The year is required for canonical Plex layout.</summary>
    public static string MovieFolder(string title, int year) => $"{title} ({year})";

    /// <summary>Plex-canonical full path to a season folder.</summary>
    public static string TvSeasonPath(string tvRoot, string show, int season, int? showYear = null) =>
        Path.Combine(tvRoot, ShowFolder(show, showYear), SeasonFolder(season));

    /// <summary>Plex-canonical full path to a movie folder.</summary>
    public static string MoviePath(string moviesRoot, string title, int year) =>
        Path.Combine(moviesRoot, MovieFolder(title, year));

    /// <summary>The file-level art names Plex looks for at the SHOW root (not inside seasons).</summary>
    public static readonly string[] ShowRootArtFiles =
    {
        "poster.jpg", "banner.jpg", "fanart.jpg", "tvshow.nfo",
        "backdrop.jpg", "folder.jpg", "landscape.jpg", "clearart.png", "logo.png",
    };

    /// <summary>Extras subfolder names Plex recognises under a movie folder.</summary>
    public static readonly string[] MovieExtrasFolders =
    {
        "Behind The Scenes", "Deleted Scenes", "Featurettes",
        "Interviews", "Scenes", "Shorts", "Trailers", "Other",
    };
}
