using System.Diagnostics;
using Tagsmith.Settings;

namespace Tagsmith.FileBot;

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
    public static FileBotClient? TryCreate(TagsmithSettings settings)
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
        Run("-script", "fn:artwork.tvdb", seasonFolder);

    /// <summary>
    /// Rename TV episodes inside a season folder using TheTVDB. Format produces
    /// <c>Show - S01E01 - Title.ext</c> which Plex parses cleanly.
    /// </summary>
    public FileBotResult RenameTvEpisodes(string seasonFolder) =>
        Run("-rename", seasonFolder,
            "--db", "TheTVDB",
            "--format", "{n} - {s00e00} - {t}",
            "--action", "MOVE",
            "-non-strict");

    /// <summary>
    /// Rename a movie folder's contents to <c>Title (YYYY).ext</c>. Side effect:
    /// writes xattr metadata that <see cref="FetchMovieArtwork"/> relies on.
    /// </summary>
    public FileBotResult RenameMovie(string movieFolder) =>
        Run("-rename", movieFolder,
            "--db", "TheMovieDB",
            "--format", "{n} ({y})",
            "--action", "MOVE",
            "-non-strict");

    /// <summary>
    /// Fetch movie artwork via the generic <c>fn:artwork</c> script. Requires
    /// xattr metadata set by a prior <see cref="RenameMovie"/> call; without
    /// it, the script silently does nothing.
    /// </summary>
    public FileBotResult FetchMovieArtwork(string movieFolder) =>
        Run("-script", "fn:artwork", movieFolder);

    /// <summary>
    /// Try to download subtitles in <paramref name="languageCode"/>. Returns the
    /// result; callers should inspect <see cref="FileBotResult.LooksLikeAuthFailure"/>
    /// to detect the OpenSubtitles 401 case.
    /// </summary>
    public FileBotResult GetSubtitles(string folder, string languageCode) =>
        Run("-get-subtitles", folder, "--lang", languageCode, "-non-strict");

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

    /// <summary>Last meaningful stdout line — used for compact status output.</summary>
    public string LastInterestingLine()
    {
        var lines = StdOut.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        return lines.Length == 0 ? "" : lines[^1];
    }
}
