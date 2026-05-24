using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Ui;

namespace MediaButler.Maui.Services;

/// <summary>
/// Executes MediaButler pipeline actions from the MAUI shell. Mirrors the
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
        Status,
        Scan,
    }

    /// <summary>
    /// Run the named action with output captured into <paramref name="onLine"/>.
    /// Always called from a background thread; the sink itself must marshal to
    /// the UI thread (use <c>MainThread.BeginInvokeOnMainThread</c>).
    /// </summary>
    public PipelineReport Run(PipelineAction action, Action<string> onLine, string? relocateOverridePath = null)
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
                Execute(action, report, relocateOverridePath);
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

    private void Execute(PipelineAction action, PipelineReport report, string? relocateOverridePath)
    {
        var s = settings.Load();

        switch (action)
        {
            case PipelineAction.RunFull:
                // Dry-run state is read from persisted settings (toggled from
                // the header checkbox) — no separate dry-run action needed.
                if (!ValidatePaths(s)) return;
                new RenameStage(s, report).Run();
                RunFileBotIfAvailable(s, report);
                new MoveStage(s, report).Run();
                break;

            case PipelineAction.Rename:
                if (!ValidatePaths(s)) return;
                new RenameStage(s, report).Run();
                break;

            case PipelineAction.FileBotTv:
                if (!ValidatePaths(s)) return;
                {
                    var fb = FileBotClient.TryCreate(s);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + s.FileBotPath); return; }
                    new FileBotStage(s, fb, report).RunTv();
                }
                break;

            case PipelineAction.FileBotMovies:
                if (!ValidatePaths(s)) return;
                {
                    var fb = FileBotClient.TryCreate(s);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + s.FileBotPath); return; }
                    new FileBotStage(s, fb, report).RunMovies();
                }
                break;

            case PipelineAction.FileBotSubtitles:
                if (!s.EnableSubtitles)
                {
                    Console.WriteLine("Subtitles are disabled in Settings. Enable EnableSubtitles to use this command.");
                    return;
                }
                if (!ValidatePaths(s)) return;
                {
                    var fb = FileBotClient.TryCreate(s);
                    if (fb is null) { Console.WriteLine("FileBot not found at " + s.FileBotPath); return; }
                    new FileBotStage(s, fb, report).RunSubtitles();
                }
                break;

            case PipelineAction.Move:
                if (!ValidatePaths(s)) return;
                new MoveStage(s, report).Run();
                break;

            case PipelineAction.Relocate:
                if (!string.IsNullOrWhiteSpace(relocateOverridePath)) s.SourcePath = relocateOverridePath;
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
                PrintScan(s);
                break;
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

    /// <summary>Same source/dest overlap guard the CLI menu uses; warns in dry-run, blocks live.</summary>
    private static bool ValidatePaths(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            Console.WriteLine("Source path not found: " + s.SourcePath);
            return false;
        }

        var overlap = PathOverlaps(s.SourcePath, s.TvDestination)
                   || PathOverlaps(s.SourcePath, s.MoviesDestination);
        if (!overlap) return true;

        if (s.DryRun)
        {
            Console.WriteLine("WARNING: Source overlaps a destination. Dry-run only — no mutations.");
            return true;
        }
        Console.WriteLine("REFUSING TO RUN: Source overlaps a destination. Enable DryRun to inspect.");
        return false;
    }

    private static bool PathOverlaps(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string p, q;
        try
        {
            p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
            q = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        }
        catch { return false; }
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(p, q, cmp)) return true;
        var sep = Path.DirectorySeparatorChar.ToString();
        return p.StartsWith(q + sep, cmp) || q.StartsWith(p + sep, cmp);
    }

    private void PrintStatus(MediaButlerSettings s)
    {
        Console.WriteLine("Settings file: " + settings.FilePath);
        Console.WriteLine("Mode         : " + (s.DryRun ? "DRY RUN" : "LIVE"));
        Console.WriteLine("Source       : " + s.SourcePath + (Directory.Exists(s.SourcePath) ? " [ok]" : " [MISSING]"));
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
