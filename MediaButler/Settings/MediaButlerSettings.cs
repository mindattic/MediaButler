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

    /// <summary>Extensions MediaButler treats as video files (used for empty-folder detection and movie classification).</summary>
    public string[] VideoExtensions { get; set; } =
        [".mkv", ".mp4", ".avi", ".m4v", ".wmv", ".mov", ".ts", ".m2ts", ".mpg", ".mpeg"];

    /// <summary>Show-level artwork file names Plex looks for at the show root.</summary>
    public string[] ShowLevelArtFiles { get; set; } =
        ["poster.jpg", "banner.jpg", "fanart.jpg", "backdrop.jpg", "folder.jpg",
         "landscape.jpg", "clearart.png", "logo.png", "tvshow.nfo"];
}
