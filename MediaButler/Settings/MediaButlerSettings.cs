namespace MediaButler.Settings;

/// <summary>
/// User-editable MediaButler configuration. Lives at
/// <c>%APPDATA%\MindAttic\MediaButler\settings.json</c> via
/// <see cref="MindAttic.Vault.Settings.JsonSettingsStore{T}"/>.
/// </summary>
public sealed class MediaButlerSettings
{
    /// <summary>Root folder MediaButler scans for messy torrent dumps.</summary>
    public string SourcePath { get; set; } = @"M:\Torrents";

    /// <summary>Where renamed TV shows are moved at the end of the pipeline.</summary>
    public string TvDestination { get; set; } = @"M:\TV";

    /// <summary>Where renamed movies are moved at the end of the pipeline.</summary>
    public string MoviesDestination { get; set; } = @"M:\Movies";

    /// <summary>Absolute path to the FileBot executable.</summary>
    public string FileBotPath { get; set; } = @"C:\Program Files\FileBot\filebot.exe";

    /// <summary>ISO 639-1 language code passed to <c>filebot -get-subtitles --lang</c>.</summary>
    public string SubtitleLanguage { get; set; } = "en";

    /// <summary>
    /// When true, the pipeline asks FileBot for subtitles. Requires OpenSubtitles
    /// credentials configured in FileBot (Preferences) or environment, otherwise
    /// the API returns 401 and MediaButler logs the failure and continues.
    /// </summary>
    public bool EnableSubtitles { get; set; } = false;

    /// <summary>Run <c>filebot -rename --db TheTVDB</c> on TV season folders.</summary>
    public bool RenameEpisodes { get; set; } = true;

    /// <summary>Run <c>filebot -rename --db TheMovieDB</c> on movie folders.</summary>
    public bool RenameMovies { get; set; } = true;

    /// <summary>Run <c>fn:artwork.tvdb</c> / <c>fn:artwork</c> for posters, banners, fanart.</summary>
    public bool FetchArtwork { get; set; } = true;

    /// <summary>
    /// When true, every stage logs what it <em>would</em> do but performs no
    /// renames, moves, deletes, or FileBot invocations that mutate disk state.
    /// FileBot itself is invoked with <c>--action TEST</c> so its planned actions
    /// still appear in stdout. Settable via Settings menu or <c>--dry-run</c> CLI flag.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// When true, folders the regex parser can't classify are sent to an LLM
    /// (via MindAttic.Legion) for a best-guess at show/movie/season. Off by
    /// default to avoid surprise API calls; turn on after confirming credentials
    /// exist in the shared MindAttic.Vault store (<c>%APPDATA%\MindAttic\LLM</c>).
    /// </summary>
    public bool EnableLlmFallback { get; set; } = false;

    /// <summary>Legion provider id used for fallback parsing (claude, openai, gemini, ...).</summary>
    public string LlmProvider { get; set; } = "claude";

    /// <summary>Top-level folder names under <see cref="SourcePath"/> MediaButler should skip.</summary>
    public string[] ExcludedFolders { get; set; } = ["temp", ".temp", "incomplete", "complete", "_unsorted"];

    /// <summary>
    /// Extensions MediaButler treats as video files (used for empty-folder
    /// detection and movie classification). Keep this list generous — anything
    /// missing here gets the host folder classified <see cref="Media.MediaKind.Empty"/>
    /// and the Rename stage will then DELETE it. The first batch is what FileBot
    /// itself recognises; the second batch (.iso .img .vob .ifo) catches DVD/BD
    /// rips that the user's real library exposed in testing.
    /// </summary>
    public string[] VideoExtensions { get; set; } =
        [".mkv", ".mp4", ".avi", ".m4v", ".wmv", ".mov", ".ts", ".m2ts", ".mpg", ".mpeg",
         ".webm", ".flv", ".divx", ".vob", ".mts", ".3gp", ".mxf", ".m2v", ".ogm", ".rmvb", ".rm", ".asf",
         ".iso", ".img", ".ifo"];

    /// <summary>
    /// Safety floor for the Rename stage's "delete empty folder" pass. A folder
    /// that contains zero recognised video files but holds more than this many
    /// bytes is refused for deletion and surfaced in the manual list instead —
    /// the user almost certainly has a video file with an extension missing
    /// from <see cref="VideoExtensions"/>. Set to 0 to disable the guard (not
    /// recommended).
    /// </summary>
    public long EmptyDeleteSafetyBytes { get; set; } = 1L * 1024 * 1024; // 1 MB

    /// <summary>Show-level artwork file names Plex looks for at the show root.</summary>
    public string[] ShowLevelArtFiles { get; set; } =
        ["poster.jpg", "banner.jpg", "fanart.jpg", "backdrop.jpg", "folder.jpg",
         "landscape.jpg", "clearart.png", "logo.png", "tvshow.nfo"];

    /// <summary>
    /// Movie titles that contain what looks like a year as part of the title
    /// itself (not the release year). Without this list, the parser would read
    /// <c>Blade Runner 2049</c> as title=<c>Blade Runner</c>, year=2049. When
    /// the normalized folder name matches one of these (case-insensitive,
    /// possibly followed by a parenthesised release year), the override wins.
    /// Add entries here for new releases that exhibit the same pattern.
    /// </summary>
    public string[] TitleYearOverrides { get; set; } =
        ["Blade Runner 2049", "Wonder Woman 1984", "1917",
         "2001 A Space Odyssey", "2012", "1984", "1922", "300"];
}
