using System.Diagnostics;
using MediaButler.Settings;

namespace MediaButler.FileBot;

/// <summary>
/// Thin wrapper around the <c>filebot.exe</c> command line. Encodes the
/// quirks discovered during the manual pass:
///
/// <list type="bullet">
///   <item>The TV artwork script is <c>fn:artwork.tvdb</c>.</item>
///   <item>The movie artwork script is <c>fn:artwork.tmdb</c> but it crashes in
///         5.2.1 ("detectMovie" signature mismatch). Workaround: rename movies
///         first via <c>--db TheMovieDB --action MOVE</c> (which writes xattr),
///         then run the generic <c>fn:artwork</c> script.</item>
///   <item>The subtitle flag is <c>-get-subtitles</c>, NOT <c>-get-missing-subtitles</c>.</item>
///   <item><c>--action</c> values are MOVE / COPY / KEEPLINK / SYMLINK / HARDLINK /
///         CLONE / DUPLICATE / TEST — there is no <c>xattr</c> action.</item>
/// </list>
/// </summary>
public sealed class FileBotClient
{
    public string ExePath { get; }

    private FileBotClient(string exePath) => ExePath = exePath;

    /// <summary>Return a usable client or null if FileBot can't be located.</summary>
    public static FileBotClient? TryCreate(MediaButlerSettings settings)
    {
        var path = TryLocate(settings.FileBotPath);
        return path is null ? null : new FileBotClient(path);
    }

    /// <summary>
    /// Find filebot.exe in this order: configured path, %ProgramFiles%, %LOCALAPPDATA%
    /// (MSI per-user install), PATH lookup. Null if nothing exists.
    /// </summary>
    public static string? TryLocate(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var candidates = new[]
        {
            @"C:\Program Files\FileBot\filebot.exe",
            @"C:\Program Files (x86)\FileBot\filebot.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileBot", "filebot.exe"),
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;

        // Fall back to PATH lookup.
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                var p = Path.Combine(dir, "filebot.exe");
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    /// <summary>Fetch TV artwork for a season folder. Returns true on exit code 0.</summary>
    public FileBotResult FetchTvArtwork(string seasonFolder) =>
        Run(BuildFetchTvArtworkArgs(seasonFolder));

    /// <summary>
    /// Rename TV episodes inside a season folder using TheTVDB. Format produces
    /// <c>Show - S01E01 - Title.ext</c> which Plex parses cleanly.
    /// When <paramref name="dryRun"/> is true, runs with <c>--action TEST</c> so
    /// FileBot prints its plan but doesn't touch files.
    /// </summary>
    public FileBotResult RenameTvEpisodes(string seasonFolder, bool dryRun = false) =>
        Run(BuildRenameTvArgs(seasonFolder, dryRun));

    /// <summary>
    /// Rename a movie folder's contents to <c>Title (YYYY).ext</c>. Side effect:
    /// writes xattr metadata that <see cref="FetchMovieArtwork"/> relies on.
    /// When <paramref name="dryRun"/> is true, runs with <c>--action TEST</c> so
    /// FileBot prints its plan but doesn't touch files.
    /// </summary>
    public FileBotResult RenameMovie(string movieFolder, bool dryRun = false) =>
        Run(BuildRenameMovieArgs(movieFolder, dryRun));

    /// <summary>
    /// Fetch movie artwork via the generic <c>fn:artwork</c> script. Requires
    /// xattr metadata set by a prior <see cref="RenameMovie"/> call; without
    /// it, the script silently does nothing.
    /// </summary>
    public FileBotResult FetchMovieArtwork(string movieFolder) =>
        Run(BuildFetchMovieArtworkArgs(movieFolder));

    /// <summary>
    /// Try to download subtitles in <paramref name="languageCode"/>. When
    /// <paramref name="credentials"/> is supplied and complete, passes the
    /// OpenSubtitles login through FileBot's <c>--def osdb.user/osdb.pwd</c>
    /// definitions so FileBot Preferences need not be pre-configured. Callers
    /// should inspect <see cref="FileBotResult.LooksLikeAuthFailure"/> to detect
    /// the 401 case.
    /// </summary>
    public FileBotResult GetSubtitles(string folder, string languageCode, Settings.SubtitleCredentials? credentials = null) =>
        Run(BuildGetSubtitlesArgs(folder, languageCode, credentials));

    // ----- Pure argument builders (testable without spawning processes) -----

    internal static string[] BuildRenameTvArgs(string seasonFolder, bool dryRun) =>
        ["-rename", seasonFolder,
         "--db", "TheTVDB",
         "--format", "{n} - {s00e00} - {t}",
         "--action", dryRun ? "TEST" : "MOVE",
         "-non-strict"];

    internal static string[] BuildRenameMovieArgs(string movieFolder, bool dryRun) =>
        ["-rename", movieFolder,
         "--db", "TheMovieDB",
         "--format", "{n} ({y})",
         "--action", dryRun ? "TEST" : "MOVE",
         "-non-strict"];

    internal static string[] BuildFetchTvArtworkArgs(string seasonFolder) =>
        ["-script", "fn:artwork.tvdb", seasonFolder];

    internal static string[] BuildFetchMovieArtworkArgs(string movieFolder) =>
        ["-script", "fn:artwork", movieFolder];

    internal static string[] BuildGetSubtitlesArgs(string folder, string languageCode, Settings.SubtitleCredentials? credentials)
    {
        var args = new List<string> { "-get-subtitles", folder, "--lang", languageCode, "-non-strict" };
        if (credentials is { IsComplete: true })
        {
            args.Add("--def");
            args.Add("osdb.user=" + credentials.User);
            args.Add("--def");
            args.Add("osdb.pwd=" + credentials.Password);
        }
        return args.ToArray();
    }

    /// <summary>Run filebot with the given arguments and capture stdout/stderr.</summary>
    public FileBotResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start filebot: " + ExePath);

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return new FileBotResult
        {
            ExitCode = proc.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
    }
}

/// <summary>Captured outcome of a single FileBot invocation.</summary>
public sealed class FileBotResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public required string StdErr { get; init; }

    public bool Success => ExitCode == 0;

    /// <summary>Detect the OpenSubtitles 401 case so the caller can warn instead of failing the pipeline.</summary>
    public bool LooksLikeAuthFailure =>
        StdOut.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        StdErr.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        StdOut.Contains("invalid username/password", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Last meaningful line emitted by FileBot. Prefers stderr on failure
    /// (where FileBot writes its diagnostic) and falls back to stdout. Used
    /// to give the user the actual reason behind a non-zero exit instead of
    /// just the exit code.
    /// </summary>
    public string LastInterestingLine()
    {
        var fromErr = LastLine(StdErr);
        if (!string.IsNullOrWhiteSpace(fromErr)) return fromErr;
        return LastLine(StdOut);
    }

    private static string LastLine(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        return lines.Length == 0 ? "" : lines[^1];
    }
}
