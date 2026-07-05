using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
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
    private readonly bool _trustAll;

    private FileBotClient(string exePath, bool trustAll) { ExePath = exePath; _trustAll = trustAll; }

    // Path where we copy + patch FileBot's bundled cacerts (user-writable, no admin required).
    private static readonly string UserCacertsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MindAttic", "MediaButler", "filebot-cacerts");

    private static readonly object CacertsLock = new();

    /// <summary>
    /// One-time setup: copies FileBot's bundled cacerts to a user-writable location,
    /// then imports every Windows trusted root CA on top via keytool. FileBot's JRE
    /// ships a stale cacerts that predates modern CAs (ISRG Root X1 for Let's Encrypt,
    /// Amazon Root CA 1, etc.), causing SSL failures against api.themoviedb.org and
    /// api.filebot.net. Layering in Windows roots ensures the JVM trusts exactly what
    /// Windows trusts. Subsequent calls return immediately once the file exists.
    /// Returns the patched path, or null if a prerequisite (FileBot JRE, keytool) is missing.
    /// </summary>
    private string? EnsureUserCacerts()
    {
        if (File.Exists(UserCacertsPath)) return UserCacertsPath;
        lock (CacertsLock)
        {
            if (File.Exists(UserCacertsPath)) return UserCacertsPath;
            try
            {
                var fbDir = Path.GetDirectoryName(ExePath)!;

                // FileBot's stripped JRE has no keytool — find one on the system.
                var keytool = FindSystemKeytool();
                if (keytool is null) return null;

                // Copy FileBot's bundled cacerts as the base (correct PKCS12 structure,
                // most common CAs already present, password "changeit").
                var srcCacerts = Path.Combine(fbDir, "jre", "lib", "security", "cacerts");
                if (!File.Exists(srcCacerts)) return null;

                Directory.CreateDirectory(Path.GetDirectoryName(UserCacertsPath)!);
                File.Copy(srcCacerts, UserCacertsPath, overwrite: true);

                // Import all Windows trusted root CAs on top. Using thumbprint as
                // alias guarantees uniqueness; non-zero keytool exits (duplicate alias)
                // are intentionally ignored.
                using var winStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                winStore.Open(OpenFlags.ReadOnly);

                var tempDir = Path.Combine(Path.GetTempPath(), $"mb-certs-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                try
                {
                    foreach (var cert in winStore.Certificates)
                    {
                        var derPath = Path.Combine(tempDir, cert.Thumbprint + ".der");
                        File.WriteAllBytes(derPath, cert.Export(X509ContentType.Cert));
                        RunKeytoolImport(keytool, UserCacertsPath, cert.Thumbprint.ToLowerInvariant(), derPath);
                    }
                }
                finally
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
                }

                return UserCacertsPath;
            }
            catch
            {
                try { File.Delete(UserCacertsPath); } catch { /* best-effort cleanup */ }
                return null;
            }
        }
    }

    /// <summary>
    /// Find keytool.exe in common system JDK locations, then PATH.
    /// FileBot's bundled JRE does not include keytool. Returns null if not found.
    /// </summary>
    private static string? FindSystemKeytool()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Microsoft\jdk-11.0.12.7-hotspot\bin\keytool.exe",
            @"C:\Program Files\Eclipse Adoptium\jdk-21.0.3.9-hotspot\bin\keytool.exe",
            @"C:\Program Files\Java\jdk-11\bin\keytool.exe",
            @"C:\Program Files\Java\jdk-17\bin\keytool.exe",
            @"C:\Program Files\Java\jdk-21\bin\keytool.exe",
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var p = Path.Combine(dir, "keytool.exe");
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    /// <summary>
    /// Import a DER certificate into a PKCS12 keystore via keytool.
    /// Silently ignores non-zero exit codes (duplicate alias, etc.).
    /// </summary>
    private static void RunKeytoolImport(string keytool, string keystorePath, string alias, string certFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = keytool,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-importcert");
        psi.ArgumentList.Add("-noprompt");
        psi.ArgumentList.Add("-trustcacerts");
        psi.ArgumentList.Add("-keystore"); psi.ArgumentList.Add(keystorePath);
        psi.ArgumentList.Add("-storetype"); psi.ArgumentList.Add("PKCS12");
        psi.ArgumentList.Add("-storepass"); psi.ArgumentList.Add("changeit");
        psi.ArgumentList.Add("-alias"); psi.ArgumentList.Add(alias);
        psi.ArgumentList.Add("-file"); psi.ArgumentList.Add(certFile);
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(30_000);
    }

    /// <summary>Return a usable client or null if FileBot can't be located.</summary>
    public static FileBotClient? TryCreate(MediaButlerSettings settings)
    {
        var path = TryLocate(settings.FileBotPath);
        return path is null ? null : new FileBotClient(path, settings.FileBotTrustAll);
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
                if (string.IsNullOrWhiteSpace(dir)) continue;
                // A single malformed PATH entry (stray quote, illegal char) must
                // not abort the whole search — skip it and keep looking.
                try
                {
                    var p = Path.Combine(dir, "filebot.exe");
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { /* illegal characters in this PATH entry */ }
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
    /// <paramref name="credentials"/> is supplied and complete, the OpenSubtitles
    /// login is staged to a per-user temp file and passed via FileBot's
    /// <c>--def osdb.user=@path</c> "value from file" syntax — the secret never
    /// appears in argv where other processes can read it via
    /// <c>Get-CimInstance Win32_Process</c>. The temp files are deleted on exit.
    /// Callers should inspect <see cref="FileBotResult.LooksLikeAuthFailure"/>
    /// to detect the 401 case.
    /// </summary>
    public FileBotResult GetSubtitles(string folder, string languageCode, Settings.SubtitleCredentials? credentials = null)
    {
        if (credentials is not { IsComplete: true })
            return Run(BuildGetSubtitlesArgs(folder, languageCode, userFile: null, pwdFile: null));

        var userFile = WriteSecretTempFile("osdb-user", credentials.User!);
        var pwdFile  = WriteSecretTempFile("osdb-pwd",  credentials.Password!);
        try
        {
            return Run(BuildGetSubtitlesArgs(folder, languageCode, userFile, pwdFile));
        }
        finally
        {
            TryDelete(userFile);
            TryDelete(pwdFile);
        }
    }

    /// <summary>
    /// Stage a secret value in a unique per-user temp file. On Windows the
    /// default temp directory already lives under the user's profile with
    /// user-only ACLs, so simple creation is sufficient — no need to layer
    /// explicit ACLs on top. Returns the absolute path.
    /// </summary>
    private static string WriteSecretTempFile(string label, string value)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mediabutler-{label}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, value);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }

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

    /// <summary>
    /// Build the args for a subtitle fetch. When <paramref name="userFile"/> /
    /// <paramref name="pwdFile"/> are non-null, emits FileBot's
    /// <c>--def name=@path</c> form so the secret values are read from those
    /// files at startup instead of appearing in argv.
    /// </summary>
    internal static string[] BuildGetSubtitlesArgs(string folder, string languageCode, string? userFile, string? pwdFile)
    {
        var args = new List<string> { "-get-subtitles", folder, "--lang", languageCode, "-non-strict" };
        if (!string.IsNullOrEmpty(userFile) && !string.IsNullOrEmpty(pwdFile))
        {
            args.Add("--def");
            args.Add("osdb.user=@" + userFile);
            args.Add("--def");
            args.Add("osdb.pwd=@" + pwdFile);
        }
        return args.ToArray();
    }

    /// <summary>
    /// Hard cap on any single FileBot invocation. Network-bound operations
    /// (OpenSubtitles fetch, TheTVDB lookup) occasionally hang; without a
    /// timeout a cron-scheduled run blocks forever. Ten minutes covers the
    /// slowest realistic operation (subtitle fetch over a flaky link) without
    /// turning into a noticeable wait when something is genuinely wedged.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Run filebot with the given arguments and capture stdout/stderr.</summary>
    public FileBotResult Run(params string[] args) => Run(DefaultTimeout, args);

    /// <summary>
    /// Run filebot with a hard timeout. If <paramref name="timeout"/> elapses
    /// before the process exits, the process tree is killed and the result
    /// carries <see cref="FileBotResult.TimedOut"/>=true with exit code -1.
    /// </summary>
    public FileBotResult Run(TimeSpan timeout, params string[] args)
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

        if (_trustAll)
        {
            // FileBot's Groovy HTTP client (artwork scripts) checks -Dtrust.all.certs=true.
            // FileBot's Java JSSE stack (rename --db TheMovieDB) reads the cacerts file
            // directly and ignores that property. FileBot's bundled JRE ships a stale
            // cacerts that predates Let's Encrypt's ISRG Root X1 root CA, so api.themoviedb.org
            // fails SSL. Fix: point the JVM at our user-space cacerts that has ISRG Root X1
            // added. EnsureUserCacerts() does the one-time copy+import on first call.
            var userCacerts = EnsureUserCacerts();
            var opts = userCacerts is not null
                ? $"-Djavax.net.ssl.trustStore={userCacerts} -Djavax.net.ssl.trustStorePassword=changeit -Djavax.net.ssl.trustStoreType=PKCS12 -Dtrust.all.certs=true"
                : "-Dtrust.all.certs=true";

            // Must use _JAVA_OPTIONS — FileBot's jpackage native-exe launcher on Windows
            // does NOT forward FILEBOT_OPTS to the embedded JVM; _JAVA_OPTIONS is read by
            // the JVM itself before main() and prints "Picked up _JAVA_OPTIONS: ..." to stderr
            // (harmless). FILEBOT_OPTS kept for Linux/macOS batch-script-based installs.
            var existingFb = psi.Environment.TryGetValue("FILEBOT_OPTS", out var fb) ? fb + " " : "";
            psi.Environment["FILEBOT_OPTS"] = existingFb + opts;
            var existingJo = psi.Environment.TryGetValue("_JAVA_OPTIONS", out var jo) ? jo + " " : "";
            psi.Environment["_JAVA_OPTIONS"] = existingJo + opts;
        }

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start filebot: " + ExePath);

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeout))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            // Drain any buffered output before reporting.
            try { proc.WaitForExit(TimeSpan.FromSeconds(5)); } catch { /* best-effort */ }
            stderr.AppendLine($"[mediabutler] killed filebot after {timeout.TotalMinutes:F1} min timeout.");
            return new FileBotResult
            {
                ExitCode = -1,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
                TimedOut = true,
            };
        }

        // The process exited within the timeout, but WaitForExit(TimeSpan) does
        // NOT guarantee the async output handlers have drained — only the
        // parameterless overload does. Without this the last stdout/stderr
        // lines (e.g. the OpenSubtitles "401 Unauthorized" marker) can be lost.
        try { proc.WaitForExit(); } catch { /* already fully exited */ }

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

    /// <summary>True when the call was killed for exceeding the timeout, not because filebot returned non-zero.</summary>
    public bool TimedOut { get; init; }

    public bool Success => ExitCode == 0;

    /// <summary>Detect the OpenSubtitles 401 case so the caller can warn instead of failing the pipeline.</summary>
    public bool LooksLikeAuthFailure =>
        StdOut.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        StdErr.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        StdOut.Contains("invalid username/password", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when FileBot ran cleanly but found nothing to rename — all files are
    /// already in the target format. FileBot exits 1 (not 0) in this case, so
    /// callers must check here before treating a non-zero exit as a real failure.
    /// </summary>
    public bool LooksLikeNoOp =>
        StdOut.Contains("Processed 0 files", StringComparison.OrdinalIgnoreCase) ||
        StdErr.Contains("Processed 0 files", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a <c>--action TEST</c> invocation matched files and printed its
    /// rename plan. FileBot (verified on 5.2.1) exits 1 for TEST even when every
    /// file matched — "[TEST] from [a] to [b]" per file plus "Processed 8 files" —
    /// because nothing was actually renamed on disk. Callers must treat this as
    /// success in dry-run only; a live MOVE never emits [TEST] lines.
    /// </summary>
    public bool LooksLikeTestPass =>
        !LooksLikeNoOp &&
        (StdOut.Contains("[TEST] from [", StringComparison.Ordinal) ||
         StdErr.Contains("[TEST] from [", StringComparison.Ordinal));

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
            // Skip JVM environment pickup banners ("Picked up _JAVA_OPTIONS: ...")
            // so they don't crowd out the actual FileBot error message.
            .Where(l => !l.StartsWith("Picked up ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length == 0 ? "" : lines[^1];
    }
}
