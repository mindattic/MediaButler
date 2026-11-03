using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Ui;

namespace MediaButler.Wpf.UI.Services;

/// <summary>
/// Executes MediaButler pipeline actions from the WPF/Blazor shell. Mirrors the
/// orchestration logic in <c>MediaButler.Pipeline.PipelineRunner</c>'s
/// headless methods, but returns the populated <see cref="PipelineReport"/>
/// instead of an exit code and captures all <see cref="Console"/> output via
/// <see cref="ConsoleCaptureWriter"/> so the UI can display a live log.
///
/// <para>
/// Console redirection is process-wide, so the runner serializes invocations
/// with a static lock — the UI must already disable action buttons while a
/// run is in flight, but the lock is belt-and-suspenders for the case where
/// two windows or background timers ever race.
/// </para>
/// </summary>
public sealed class PipelineRunner
{
    private static readonly object consoleLock = new();
    private readonly SettingsService settings;

    public PipelineRunner(SettingsService settings) => this.settings = settings;

    public SettingsService SettingsService => settings;

    public enum PipelineAction
    {
        RunFull,
        Rename,
        FileBotTv,
        FileBotMovies,
        FileBotSubtitles,
        Move,
        Relocate,
        FixLibraryMovies,
        FixLibraryTv,
        Status,
        Scan,
    }

    /// <summary>
    /// Run the named action with output captured into <paramref name="onLine"/>.
    /// Always called from a background thread; the sink itself must marshal to
    /// the UI thread (use Blazor's <c>InvokeAsync</c>).
    /// </summary>
    public PipelineReport Run(PipelineAction action, Action<string> onLine, string? folderOverride = null)
    {
        var report = new PipelineReport();
        lock (consoleLock)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            var writer = new ConsoleCaptureWriter(onLine);
            try
            {
                Console.SetOut(writer);
                Console.SetError(writer);
                Status.Verbosity = Verbosity.Normal;
                Execute(action, report, folderOverride);
                writer.Flush();
            }
            finally
            {
                writer.Flush();
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
        return report;
    }

    private void Execute(PipelineAction action, PipelineReport report, string? folderOverride)
    {
        var s = settings.Load();

        switch (action)
        {
            case PipelineAction.RunFull:
                // Dry-run state is read from persisted settings (toggled from
                // the header checkbox) — no separate dry-run action needed.
                ForEachSource(s, src =>
                {
                    if (!ValidatePaths(src)) return;
                    new RenameStage(src, report).Run();
                    RunFileBotIfAvailable(src, report);
                    new MoveStage(src, report).Run();
                });
                break;

            case PipelineAction.Rename:
                ForEachSource(s, src =>
                {
                    if (!ValidatePaths(src)) return;
                    new RenameStage(src, report).Run();
                });
                break;

            case PipelineAction.FileBotTv:
                // FileBot renames in-place; path guard only guards source→dest moves (not applicable here).
                ForEachSource(s, src =>
                {
                    if (!Directory.Exists(src.SourcePath)) { Console.WriteLine("Source path not found: " + src.SourcePath); return; }
                    var fb = FileBotClient.TryCreate(src);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + src.FileBotPath); return; }
                    new FileBotStage(src, fb, report).RunTv();
                });
                break;

            case PipelineAction.FileBotMovies:
                ForEachSource(s, src =>
                {
                    if (!Directory.Exists(src.SourcePath)) { Console.WriteLine("Source path not found: " + src.SourcePath); return; }
                    var fb = FileBotClient.TryCreate(src);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + src.FileBotPath); return; }
                    new FileBotStage(src, fb, report).RunMovies();
                });
                break;

            case PipelineAction.FileBotSubtitles:
                if (!s.EnableSubtitles)
                {
                    Console.WriteLine("Subtitles are disabled in Settings. Enable EnableSubtitles to use this command.");
                    return;
                }
                ForEachSource(s, src =>
                {
                    if (!ValidatePaths(src)) return;
                    var fb = FileBotClient.TryCreate(src);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + src.FileBotPath); return; }
                    new FileBotStage(src, fb, report).RunSubtitles();
                });
                break;

            case PipelineAction.Move:
                ForEachSource(s, src =>
                {
                    if (!ValidatePaths(src)) return;
                    new MoveStage(src, report).Run();
                });
                break;

            case PipelineAction.FixLibraryMovies:
            case PipelineAction.FixLibraryTv:
                if (string.IsNullOrWhiteSpace(folderOverride))
                {
                    Console.WriteLine("No library folder supplied.");
                    return;
                }
                s.SourcePath   = folderOverride.Trim();
                s.NoGuard      = true;
                s.ExtraSources = Array.Empty<string>();
                s.Recursive    = false;
                if (!Directory.Exists(s.SourcePath)) { Console.WriteLine("Folder not found: " + s.SourcePath); return; }
                {
                    var fb = FileBotClient.TryCreate(s);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + s.FileBotPath); return; }
                    if (action == PipelineAction.FixLibraryMovies)
                        new FileBotStage(s, fb, report).RunMovies();
                    else
                        new FileBotStage(s, fb, report).RunTv();
                }
                break;

            case PipelineAction.Relocate:
                if (!string.IsNullOrWhiteSpace(folderOverride)) s.SourcePath = folderOverride.Trim();
                if (!Directory.Exists(s.SourcePath))
                {
                    Console.WriteLine("Source path not found: " + s.SourcePath);
                    return;
                }
                new RelocateStage(s, report).Run();
                break;

            case PipelineAction.Status:
                PrintStatus(s);
                break;

            case PipelineAction.Scan:
                ForEachSource(s, PrintScan);
                break;
        }
    }

    /// <summary>
    /// Run <paramref name="body"/> once per inbox, exactly like the CLI: the
    /// primary SourcePath, then ExtraSources, plus (with Recursive) container
    /// subfolders as inboxes of their own. Each gets a cloned settings
    /// instance scoped to that one source, so stages never see the others.
    /// </summary>
    private static void ForEachSource(MediaButlerSettings s, Action<MediaButlerSettings> body)
    {
        var sources = global::MediaButler.Pipeline.PipelineRunner.EffectiveSources(s);
        if (sources.Count == 0) { Console.WriteLine("No source path configured."); return; }
        foreach (var src in sources)
        {
            if (sources.Count > 1)
            {
                Console.WriteLine();
                Console.WriteLine($"==== Source: {src} ====");
            }
            var perSource = global::MediaButler.Pipeline.PipelineRunner.Clone(s);
            perSource.SourcePath   = src;
            perSource.ExtraSources = Array.Empty<string>();
            body(perSource);
        }
    }

    private static void RunFileBotIfAvailable(MediaButlerSettings s, PipelineReport report)
    {
        var fb = FileBotClient.TryCreate(s);
        if (fb is null)
            Console.WriteLine("FileBot not found at " + s.FileBotPath + " — skipping FileBot stages.");
        else
            new FileBotStage(s, fb, report).Run();
    }

    /// <summary>
    /// The CLI's own source/dest overlap guard (MB-LAW-3): warns in dry-run, blocks live. A private
    /// copy here had drifted — it skipped MusicDestination and missed drive-root destinations.
    /// </summary>
    private static bool ValidatePaths(MediaButlerSettings s) => PathGuard.ValidatePaths(s);

    private void PrintStatus(MediaButlerSettings s)
    {
        Console.WriteLine("Settings file: " + settings.FilePath);
        Console.WriteLine("Mode         : " + (s.DryRun ? "DRY RUN" : "LIVE"));
        Console.WriteLine("Source       : " + s.SourcePath + (Directory.Exists(s.SourcePath) ? " [ok]" : " [MISSING]"));
        foreach (var extra in s.ExtraSources)
            Console.WriteLine("Extra source : " + extra + (Directory.Exists(extra) ? " [ok]" : " [MISSING]"));
        Console.WriteLine("Recursive    : " + (s.Recursive ? "true (container subfolders become inboxes)" : "false"));
        Console.WriteLine("TV dest      : " + s.TvDestination);
        Console.WriteLine("Movies dest  : " + s.MoviesDestination);
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        Console.WriteLine("FileBot      : " + (fb ?? "NOT FOUND"));

        var creds = SubtitleCredentials.Load();
        Console.WriteLine("OpenSubtitles: " + (creds.IsComplete ? $"configured as '{creds.User}'" : "no MindAttic Vault credentials"));

        if (Directory.Exists(s.SourcePath))
        {
            var items = new MediaScanner(s).Scan().ToList();
            Console.WriteLine($"Scanned {items.Count} root folder(s):");
            foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
                Console.WriteLine($"  {grp.Key,-20} {grp.Count()}");
        }
    }

    private static void PrintScan(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            Console.WriteLine("Source path not found: " + s.SourcePath);
            return;
        }
        var items = new MediaScanner(s).Scan().ToList();
        foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
        {
            Console.WriteLine($"{grp.Key} ({grp.Count()}):");
            foreach (var it in grp.OrderBy(i => i.OriginalName))
            {
                var detail = it.Kind switch
                {
                    MediaKind.Movie               => $" -> {NameParser.FormatMovieFolder(it.MovieTitle ?? "?", it.MovieYear)}",
                    MediaKind.TvSeason            => $" -> {NameParser.FormatSeasonFolder(it.ShowName ?? "?", it.SeasonNumber ?? 0)}",
                    MediaKind.MultiSeasonParent   => $" ({it.Seasons.Count} season(s), show='{it.ShowName ?? "?"}')",
                    _                             => "",
                };
                Console.WriteLine($"  {it.OriginalName}{detail}");
            }
        }
        Console.WriteLine($"Total: {items.Count} root folder(s).");
    }

    public static string FormatReport(MediaButlerSettings s, PipelineReport r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---- Pipeline summary ----");
        sb.AppendLine($"Mode             : {(s.DryRun ? "DRY RUN" : "LIVE")}");
        sb.AppendLine($"Renamed locally  : {r.Renamed}");
        sb.AppendLine($"Hoisted seasons  : {r.Hoisted}");
        sb.AppendLine($"Empty deleted    : {r.EmptyDeleted}");
        sb.AppendLine($"FileBot TV ok    : {r.FileBotTvOk}");
        sb.AppendLine($"FileBot Movies ok: {r.FileBotMoviesOk}");
        sb.AppendLine($"Artwork ok       : {r.ArtworkOk}");
        sb.AppendLine($"Subtitles ok     : {r.SubtitlesOk}");
        sb.AppendLine($"Moved to TV      : {r.TvMoved}");
        sb.AppendLine($"Moved to Movies  : {r.MoviesMoved}");
        sb.AppendLine($"Errors           : {r.Errors.Count}");
        sb.AppendLine($"Needs manual fix : {r.NeedsManual.Count}");

        if (r.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Errors:");
            foreach (var e in r.Errors.Take(20)) sb.AppendLine("  ! " + e);
            if (r.Errors.Count > 20) sb.AppendLine($"  ...and {r.Errors.Count - 20} more");
        }

        if (r.NeedsManual.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Needs manual review:");
            foreach (var m in r.NeedsManual.Take(30))
                sb.AppendLine($"  - [{m.Kind}] {Path.GetFileName(m.Path)} — {m.Reason}");
            if (r.NeedsManual.Count > 30) sb.AppendLine($"  ...and {r.NeedsManual.Count - 30} more");
        }
        return sb.ToString();
    }
}
