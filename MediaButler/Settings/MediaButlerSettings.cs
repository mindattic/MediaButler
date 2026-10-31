using MindAttic.Vault.Settings.Manifest;

namespace MediaButler.Settings;

/// <summary>
/// What happens when a movie folder — or, via <see cref="MediaButlerSettings.DuplicateEpisodeAction"/>,
/// a single TV episode — already exists at the destination with content.
/// Serialized as a string in settings.json (case-insensitive read).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DuplicateMovieAction
{
    /// <summary>
    /// The copy with the larger primary video file wins; the smaller copy is
    /// deleted (audit-logged). When either side has no video to compare, falls
    /// back to <see cref="Flag"/> — a wrong guess here destroys media.
    /// </summary>
    KeepLargest,

    /// <summary>
    /// Classic MB-LAW-9 behaviour: never touch either copy — the incoming
    /// folder/file stays in the source and surfaces as needs-manual (exit 2).
    /// </summary>
    Flag,
}

/// <summary>
/// MediaButler's app-level settings (LAMP, MB-T27). Lives at
/// <c>%APPDATA%\MindAttic\MediaButler\settings.json</c> via
/// <see cref="MindAttic.Vault.Settings.JsonSettingsStore{T}"/> and is edited only in
/// MindAttic.Vault.Desktop, which renders this class's manifest as the MediaButler tab.
/// Property names are the JSON names the file has always used, so an existing file loads
/// unchanged; initializers are the first-run defaults. No secrets: the OpenSubtitles login
/// lives in the Vault Subtitles store (<see cref="SubtitleCredentials"/>) and LLM keys in the
/// shared named key lists.
/// </summary>
[SettingsApp(AppId, "MediaButler", Icon = "film",
    Description = "Cleans torrent dumps into a Plex library: rename, FileBot, artwork, subtitles, move.")]
[UsesCredential("claude", Purpose = "LLM fallback for folder and file names the parser can't classify (off by default)")]
public sealed class MediaButlerSettings
{
    /// <summary>Settings app id, and the app's folder under <c>%APPDATA%\MindAttic</c>.</summary>
    public const string AppId = "MediaButler";

    /// <summary>Root folder MediaButler scans for messy torrent dumps.</summary>
    [Setting("Source folder", Group = "Folders", Order = 1, Help = "Root folder MediaButler scans for messy torrent dumps.")]
    [PathPicker(PathKind.Folder)]
    public string SourcePath { get; set; } = @"M:\Torrents";

    /// <summary>
    /// Additional inbox roots processed after <see cref="SourcePath"/> on every
    /// pipeline/scan run (e.g. <c>M:\Torrents\temp</c>, <c>D:\Downloads</c>).
    /// Each is validated and processed exactly like the primary source. CLI
    /// <c>--source</c> flags replace BOTH this list and <see cref="SourcePath"/>.
    /// </summary>
    [Setting("Extra source folders", Group = "Folders", Order = 2, Help = "More inbox folders processed after the source folder on every run. The CLI --source flag replaces both.")]
    public string[] ExtraSources { get; set; } = Array.Empty<string>();

    /// <summary>Where renamed TV shows are moved at the end of the pipeline.</summary>
    [Setting("TV destination", Group = "Folders", Order = 5, Help = "Where renamed TV seasons are moved at the end of the pipeline.")]
    [PathPicker(PathKind.Folder)]
    public string TvDestination { get; set; } = @"M:\TV";

    /// <summary>Where renamed movies are moved at the end of the pipeline.</summary>
    [Setting("Movies destination", Group = "Folders", Order = 6, Help = "Where renamed movies are moved at the end of the pipeline.")]
    [PathPicker(PathKind.Folder)]
    public string MoviesDestination { get; set; } = @"M:\Movies";

    /// <summary>
    /// Where music folders (recognised via the variation catalog's
    /// <c>music</c> section) are moved as-is. Empty (the default) disables
    /// music moves — music items are left in place and flagged instead.
    /// MediaButler never renames or restructures music content.
    /// </summary>
    [Setting("Music destination", Group = "Folders", Order = 7, Help = "Where music folders are moved as-is. Empty leaves music in place and flags it.")]
    [PathPicker(PathKind.Folder)]
    public string MusicDestination { get; set; } = "";

    /// <summary>
    /// When true, container subfolders of each source whose names appear in
    /// <see cref="ExcludedFolders"/> (e.g. <c>temp</c>, <c>incomplete</c>) are
    /// processed as their own inboxes too, recursively — so
    /// <c>--source M:\Torrents --recursive</c> also organizes
    /// <c>M:\Torrents\temp</c>.
    /// </summary>
    [Setting("Process container subfolders", Group = "Folders", Order = 3, Help = "Also treat subfolders named in Excluded folders (temp, incomplete, ...) as inboxes of their own.")]
    public bool Recursive { get; set; }

    /// <summary>Absolute path to the FileBot executable.</summary>
    [Setting("FileBot executable", Group = "FileBot", Order = 1, Help = "Full path to filebot.exe.")]
    [PathPicker(PathKind.File, Filter = "FileBot|filebot.exe|Executables|*.exe")]
    public string FileBotPath { get; set; } = @"C:\Program Files\FileBot\filebot.exe";

    /// <summary>
    /// When true, passes <c>-Dtrust.all.certs=true</c> to FileBot's JVM via
    /// <c>FILEBOT_OPTS</c>. Use when FileBot's bundled JRE can't build the
    /// certificate chain to TheMovieDB or TheTVDB — typically a
    /// <c>SunCertPathBuilderException</c> in FileBot's stderr. Not recommended
    /// on permanently open, fully-trusted networks.
    /// </summary>
    [Setting("Trust all certificates", Group = "FileBot", Order = 2, Help = "Pass -Dtrust.all.certs=true to FileBot's JVM. Use only when FileBot fails with SunCertPathBuilderException.")]
    public bool FileBotTrustAll { get; set; } = false;

    /// <summary>ISO 639-1 language code passed to <c>filebot -get-subtitles --lang</c>.</summary>
    [Setting("Subtitle language", Group = "Subtitles", Order = 2, Help = "ISO 639-1 code passed to FileBot's -get-subtitles --lang, e.g. en.")]
    public string SubtitleLanguage { get; set; } = "en";

    /// <summary>
    /// When true, the pipeline asks FileBot for subtitles. Requires OpenSubtitles
    /// credentials configured in FileBot (Preferences) or environment, otherwise
    /// the API returns 401 and MediaButler logs the failure and continues.
    /// </summary>
    [Setting("Fetch subtitles", Group = "Subtitles", Order = 1, Help = "Ask FileBot for OpenSubtitles subtitles during the pipeline. The OpenSubtitles login lives in the Vault Subtitles store, never here.")]
    public bool EnableSubtitles { get; set; } = false;

    /// <summary>Run <c>filebot -rename --db TheTVDB</c> on TV season folders.</summary>
    [Setting("Rename TV episodes", Group = "FileBot", Order = 3, Help = "Run filebot -rename --db TheTVDB on TV season folders.")]
    public bool RenameEpisodes { get; set; } = true;

    /// <summary>Run <c>filebot -rename --db TheMovieDB</c> on movie folders.</summary>
    [Setting("Rename movies", Group = "FileBot", Order = 4, Help = "Run filebot -rename --db TheMovieDB on movie folders.")]
    public bool RenameMovies { get; set; } = true;

    /// <summary>Run <c>fn:artwork.tvdb</c> / <c>fn:artwork</c> for posters, banners, fanart.</summary>
    [Setting("Fetch artwork", Group = "FileBot", Order = 5, Help = "Download posters, banners and fanart.")]
    public bool FetchArtwork { get; set; } = true;

    /// <summary>
    /// When true, every stage logs what it <em>would</em> do but performs no
    /// renames, moves, deletes, or FileBot invocations that mutate disk state.
    /// FileBot itself is invoked with <c>--action TEST</c> so its planned actions
    /// still appear in stdout. Set in MindAttic Vault, by the WPF Run page's switch, or per run with <c>--dry-run</c>.
    /// </summary>
    [Setting("Dry run", Group = "Safety", Order = 1, Help = "Log what every stage would do without renaming, moving or deleting anything. The CLI --dry-run flag forces it on for one run.")]
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// When set, each stage processes at most this many items before stopping.
    /// Useful for smoke-testing a new configuration without running the whole
    /// inbox. Null (the default) means unlimited.
    /// </summary>
    [Setting("Item limit per stage", Group = "Safety", Order = 6, Help = "Each stage processes at most this many items. Empty means unlimited. Usually set per run with --limit.")]
    [SettingRange(1, int.MaxValue)]
    public int? Limit { get; set; }

    /// <summary>
    /// When true, the source/destination overlap safety check is bypassed.
    /// Use when deliberately pointing a command at a destination folder to
    /// repair an existing library (e.g. <c>hoist --source "M:\Movies" --no-guard</c>
    /// to fix folder names, or <c>rename --source "M:\Movies" --no-guard</c> for a
    /// full pass). Not persisted — CLI/menu only.
    /// </summary>
    [Setting("Skip source/destination overlap check", Group = "Safety", Order = 7, Help = "Bypass the safety check that refuses a source inside a destination. Usually set per run with --no-guard.")]
    public bool NoGuard { get; set; }

    /// <summary>
    /// When true, folders the regex parser can't classify are sent to an LLM
    /// (via MindAttic.Legion) for a best-guess at show/movie/season. Off by
    /// default to avoid surprise API calls; turn on after confirming credentials
    /// exist in the shared MindAttic.Vault store (<c>%APPDATA%\MindAttic\LLM</c>).
    /// </summary>
    [Setting("LLM fallback for unparseable names", Group = "Name parsing", Order = 1, Help = "Send folder and file names the parser can't classify to an LLM for a best guess. Uses this app's key from the Keys section.")]
    public bool EnableLlmFallback { get; set; } = false;

    /// <summary>Legion provider id used for fallback parsing (claude, openai, gemini, ...).</summary>
    [Setting("LLM provider", Group = "Name parsing", Order = 2, Help = "Legion provider id for the fallback: claude, openai, gemini, ...")]
    public string LlmProvider { get; set; } = "claude";

    /// <summary>Top-level folder names under <see cref="SourcePath"/> MediaButler should skip.</summary>
    [Setting("Excluded folders", Group = "Folders", Order = 4, Help = "Top-level folder names under a source that are skipped (or processed as inboxes when container subfolders are on).")]
    public string[] ExcludedFolders { get; set; } = ["temp", ".temp", "incomplete", "complete", "_unsorted"];

    /// <summary>
    /// Extensions MediaButler treats as video files (used for empty-folder
    /// detection and movie classification). Keep this list generous — anything
    /// missing here gets the host folder classified <see cref="Media.MediaKind.Empty"/>
    /// and the Rename stage will then DELETE it. The first batch is what FileBot
    /// itself recognises; the second batch (.iso .img .vob .ifo) catches DVD/BD
    /// rips that the user's real library exposed in testing.
    /// </summary>
    [Setting("Video extensions", Group = "File types", Order = 1, Help = "Extensions treated as video. Keep this generous: a folder with no recognised video is classified Empty and deleted. An empty list falls back to the defaults.")]
    public string[] VideoExtensions
    {
        // Never let this resolve to an empty set: an empty list classifies
        // *every* folder as Empty, and the Rename stage then deletes them.
        // A cleared array (bad settings.json / manual edit) falls back to the
        // safe defaults instead.
        get => videoExtensions is { Length: > 0 } ? videoExtensions : DefaultVideoExtensions;
        set => videoExtensions = value is { Length: > 0 } ? value : DefaultVideoExtensions;
    }

    private string[] videoExtensions = DefaultVideoExtensions;

    private static readonly string[] DefaultVideoExtensions =
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
    [Setting("Empty-folder delete safety floor (bytes)", Group = "Safety", Order = 4, Help = "A folder with no recognised video but more than this many bytes is flagged instead of deleted. 0 disables the guard.")]
    [SettingRange(0, long.MaxValue)]
    public long EmptyDeleteSafetyBytes { get; set; } = 1L * 1024 * 1024; // 1 MB

    /// <summary>
    /// Extensions that mark a folder as MUSIC when it holds no recognised
    /// video: a folder of .mp3/.flac files classifies as
    /// <see cref="Media.MediaKind.Music"/> instead of Empty (which would have
    /// put it on the delete path). Music folders move as-is to
    /// <see cref="MusicDestination"/> when one is configured.
    /// </summary>
    [Setting("Audio extensions", Group = "File types", Order = 2, Help = "Extensions that mark a folder with no video as music.")]
    public string[] AudioExtensions { get; set; } =
        [".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".ape", ".alac", ".aiff", ".dsf"];

    /// <summary>
    /// Duplicate-movie policy: what to do when a movie's destination folder
    /// already exists with content. <see cref="DuplicateMovieAction.KeepLargest"/>
    /// (default) keeps whichever copy has the larger primary video and deletes
    /// the other; <see cref="DuplicateMovieAction.Flag"/> restores the classic
    /// leave-both-and-ask behaviour. CLI: <c>--duplicates keep-largest|flag</c>.
    /// </summary>
    [Setting("Duplicate movies", Group = "Safety", Order = 2, Help = "When a movie folder already exists at the destination: keep the copy with the larger video, or flag both for review. CLI: --duplicates.")]
    public DuplicateMovieAction DuplicateMovieAction { get; set; } = DuplicateMovieAction.KeepLargest;

    /// <summary>
    /// Duplicate-episode policy: what to do when a TV season merge finds an
    /// incoming episode whose name or parsed episode number already exists at
    /// the destination season folder. <see cref="DuplicateMovieAction.KeepLargest"/>
    /// (default) keeps whichever copy has the larger video file and deletes the
    /// other, mirroring <see cref="DuplicateMovieAction"/>; <see cref="DuplicateMovieAction.Flag"/>
    /// restores the classic leave-both-and-ask behaviour. CLI: <c>--tv-duplicates keep-largest|flag</c>.
    /// </summary>
    [Setting("Duplicate episodes", Group = "Safety", Order = 3, Help = "When a season merge finds an episode that already exists: keep the larger copy, or flag both. CLI: --tv-duplicates.")]
    public DuplicateMovieAction DuplicateEpisodeAction { get; set; } = DuplicateMovieAction.KeepLargest;

    /// <summary>
    /// Ceiling for treating a "sample"-named video as junk. Release groups
    /// bundle short promo clips ("...-sample.mkv", "Sample\..."); when a
    /// consolidated episode/movie shell is deleted, sample-named videos under
    /// this size don't block the delete. A sample-named video LARGER than this
    /// is treated as real media and keeps the folder alive for manual review.
    /// </summary>
    [Setting("Sample clip ceiling (bytes)", Group = "Safety", Order = 5, Help = "Sample-named videos under this size are junk and don't block deleting an emptied folder; larger ones are kept for review.")]
    [SettingRange(0, long.MaxValue)]
    public long SampleMaxBytes { get; set; } = 300L * 1024 * 1024; // 300 MB

    /// <summary>
    /// Subtitle sidecar extensions that travel with a video during episode
    /// consolidation and season merges.
    /// </summary>
    [Setting("Subtitle extensions", Group = "Subtitles", Order = 3, Help = "Subtitle sidecars that travel with a video during episode consolidation and season merges.")]
    public string[] SubtitleExtensions { get; set; } = [".srt", ".sub", ".idx", ".ass", ".ssa"];

    /// <summary>
    /// Override location of the naming-variation catalog. Empty (the default)
    /// resolves to <c>%APPDATA%\MindAttic\MediaButler\variations.json</c>.
    /// Tests point this at a temp file to stay hermetic.
    /// </summary>
    [Setting("Variation catalog file", Group = "Name parsing", Order = 4, Help = "Override for the naming-variation catalog. Empty uses variations.json in this app's folder.")]
    [PathPicker(PathKind.File, Filter = "JSON|*.json")]
    public string VariationCatalogPath { get; set; } = "";

    /// <summary>
    /// Movie titles that contain what looks like a year as part of the title
    /// itself (not the release year). Without this list, the parser would read
    /// <c>Blade Runner 2049</c> as title=<c>Blade Runner</c>, year=2049. When
    /// the normalized folder name matches one of these (case-insensitive,
    /// possibly followed by a parenthesised release year), the override wins.
    /// Add entries here for new releases that exhibit the same pattern.
    /// </summary>
    [Setting("Titles that contain a year", Group = "Name parsing", Order = 3, Help = "Movie titles whose year-like number is part of the title, e.g. Blade Runner 2049, so it isn't read as the release year.")]
    public string[] TitleYearOverrides { get; set; } =
        ["Blade Runner 2049", "Wonder Woman 1984", "1917",
         "2001 A Space Odyssey", "2012", "1984", "1922", "300"];

    /// <summary>
    /// Per-show TheTVDB series id overrides, keyed by the show name as it
    /// appears on disk (the folder's parsed <c>ShowName</c>, e.g. "Kians
    /// Bizarre B and B"). Some shows aren't findable by any title variant in
    /// TheTVDB's name-search index but resolve fine by numeric id — add an
    /// entry here once (found via <c>filebot -list --db TheTVDB --q "the id"</c>
    /// against a candidate id, or the show's TheTVDB URL) and every future
    /// rename for that show uses <c>--q {id}</c> instead of FileBot's
    /// filename-derived title search. Lookup is case-insensitive; see
    /// <see cref="GetTvdbIdOverride"/>.
    /// </summary>
    [Setting("TheTVDB id overrides", Group = "FileBot", Order = 6, Help = "Show name as it appears on disk = TheTVDB series id, for shows TheTVDB can't find by name.")]
    public Dictionary<string, string> TvdbIdOverrides { get; set; } = new();

    /// <summary>Case-insensitive lookup into <see cref="TvdbIdOverrides"/>.</summary>
    public string? GetTvdbIdOverride(string showName) =>
        TvdbIdOverrides.FirstOrDefault(kv => string.Equals(kv.Key, showName, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// Per-movie TheMovieDB id overrides, keyed by the movie title as it
    /// appears on disk (the folder's parsed <c>MovieTitle</c>, e.g. "Heat").
    /// Mirrors <see cref="TvdbIdOverrides"/> for the rare movie that isn't
    /// findable by any title variant in TheMovieDB's name-search index but
    /// resolves fine by numeric id. Lookup is case-insensitive; see
    /// <see cref="GetTmdbIdOverride"/>.
    /// </summary>
    [Setting("TheMovieDB id overrides", Group = "FileBot", Order = 7, Help = "Movie title as it appears on disk = TheMovieDB id, for movies TheMovieDB can't find by name.")]
    public Dictionary<string, string> TmdbIdOverrides { get; set; } = new();

    /// <summary>Case-insensitive lookup into <see cref="TmdbIdOverrides"/>.</summary>
    public string? GetTmdbIdOverride(string movieTitle) =>
        TmdbIdOverrides.FirstOrDefault(kv => string.Equals(kv.Key, movieTitle, StringComparison.OrdinalIgnoreCase)).Value;
}
