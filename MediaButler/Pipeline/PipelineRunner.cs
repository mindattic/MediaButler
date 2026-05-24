using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Settings;
using MediaButler.Ui;
using Spectre.Console;
// Spectre.Console exports a Status spinner type; disambiguate our pipeline logger.
using Status = MediaButler.Ui.Status;

namespace MediaButler.Pipeline;

/// <summary>
/// Shared pipeline orchestration consumed by both the headless subcommands
/// and the interactive menu. Owns loading effective settings (persisted +
/// CLI overlay), wiring stages, and printing the consolidated report. Each
/// public method returns a 0/1 exit code so commands can return it directly.
/// </summary>
public sealed class PipelineRunner
{
    private readonly SettingsService settings;

    public PipelineRunner(SettingsService settings) => this.settings = settings;

    public SettingsService Settings => settings;

    /// <summary>Load persisted settings then overlay any CLI/menu overrides.</summary>
    public MediaButlerSettings LoadEffective(Action<MediaButlerSettings>? overlay = null)
    {
        var s = settings.Load();
        overlay?.Invoke(s);
        return s;
    }

    /// <summary>Run every stage in order: rename → FileBot (TV + movies + subs + artwork) → move.</summary>
    public int RunFull(MediaButlerSettings s)
    {
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null)
            Status.Print("FileBot not found at " + s.FileBotPath + " — skipping FileBot stages.", Theme.Err);
        else
            new FileBotStage(s, fb, report).Run();
        new MoveStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunScan(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            Status.Print("Source path not found: " + s.SourcePath, Theme.Err);
            return 1;
        }
        var items = new MediaScanner(s).Scan().ToList();
        foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
        {
            Status.Print($"{grp.Key} ({grp.Count()}):", Theme.Header);
            foreach (var it in grp.OrderBy(i => i.OriginalName))
            {
                var detail = it.Kind switch
                {
                    MediaKind.Movie             => $" -> {NameParser.FormatMovieFolder(it.MovieTitle ?? "?", it.MovieYear)}",
                    MediaKind.TvSeason          => $" -> {NameParser.FormatSeasonFolder(it.ShowName ?? "?", it.SeasonNumber ?? 0)}",
                    MediaKind.MultiSeasonParent => $" ({it.Seasons.Count} season(s), show='{it.ShowName ?? "?"}')",
                    _                           => "",
                };
                Status.Print($"  {it.OriginalName}{detail}", Theme.Dim);
            }
        }
        Status.Summary($"Total: {items.Count} root folder(s).", Theme.Normal);
        return 0;
    }

    public int RunRename(MediaButlerSettings s)
    {
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunFileBotTv(MediaButlerSettings s)
    {
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { Status.Print("FileBot not found.", Theme.Err); return 1; }
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunTv();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunFileBotMovies(MediaButlerSettings s)
    {
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { Status.Print("FileBot not found.", Theme.Err); return 1; }
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunMovies();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunFileBotSubtitles(MediaButlerSettings s)
    {
        if (!s.EnableSubtitles)
        {
            Status.Print("Subtitles are disabled in Settings. Enable EnableSubtitles to use this command.", Theme.Dim);
            return 0;
        }
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { Status.Print("FileBot not found.", Theme.Err); return 1; }
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunSubtitles();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunMove(MediaButlerSettings s)
    {
        if (!PathGuard.ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new MoveStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int RunRelocate(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            Status.Print("Source path not found: " + s.SourcePath, Theme.Err);
            return 1;
        }
        // Relocate deliberately runs against a destination — pointing it at
        // M:\Movies to evict a stray TvSeason is the whole point. So the
        // source-vs-dest overlap guard is not applied here.
        var report = new PipelineReport();
        new RelocateStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    public int ShowStatus(MediaButlerSettings s)
    {
        Status.Print("Settings file: " + settings.FilePath, Theme.Normal);
        Status.Print("Mode         : " + (s.DryRun ? "DRY RUN" : "LIVE"), s.DryRun ? Theme.Active : Theme.Ok);
        var sourceOk = Directory.Exists(s.SourcePath);
        Status.Print("Source       : " + s.SourcePath + " " + (sourceOk ? "[ok]" : "[MISSING]"), sourceOk ? Theme.Ok : Theme.Err);
        Status.Print("TV dest      : " + s.TvDestination, Theme.Normal);
        Status.Print("Movies dest  : " + s.MoviesDestination, Theme.Normal);
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        Status.Print("FileBot      : " + (fb ?? "NOT FOUND"), fb is null ? Theme.Err : Theme.Ok);

        var creds = SubtitleCredentials.Load();
        Status.Print("OpenSubtitles: " + (creds.IsComplete ? $"configured as '{creds.User}'" : "no MindAttic Vault credentials"),
            creds.IsComplete ? Theme.Ok : Theme.Dim);

        if (sourceOk)
        {
            var items = new MediaScanner(s).Scan().ToList();
            Status.Print($"Scanned {items.Count} root folder(s):", Theme.Normal);
            foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
                Status.Print($"  {grp.Key,-20} {grp.Count()}", Theme.Dim);
        }
        return 0;
    }

    public void PrintReport(MediaButlerSettings s, PipelineReport r)
    {
        AnsiConsole.WriteLine();
        Status.Summary("---- Pipeline summary ----", Theme.Header);
        Status.Summary($"Mode             : {(s.DryRun ? "DRY RUN" : "LIVE")}", s.DryRun ? Theme.Active : Theme.Ok);
        Status.Summary($"Renamed locally  : {r.Renamed}", Theme.Normal);
        Status.Summary($"Hoisted seasons  : {r.Hoisted}", Theme.Normal);
        Status.Summary($"Empty deleted    : {r.EmptyDeleted}", Theme.Normal);
        Status.Summary($"FileBot TV ok    : {r.FileBotTvOk}", Theme.Normal);
        Status.Summary($"FileBot Movies ok: {r.FileBotMoviesOk}", Theme.Normal);
        Status.Summary($"Artwork ok       : {r.ArtworkOk}", Theme.Normal);
        Status.Summary($"Subtitles ok     : {r.SubtitlesOk}", Theme.Normal);
        Status.Summary($"Moved to TV      : {r.TvMoved}", Theme.Ok);
        Status.Summary($"Moved to Movies  : {r.MoviesMoved}", Theme.Ok);
        Status.Summary($"Errors           : {r.Errors.Count}", r.Errors.Count > 0 ? Theme.Err : Theme.Dim);
        Status.Summary($"Needs manual fix : {r.NeedsManual.Count}", r.NeedsManual.Count > 0 ? Theme.Active : Theme.Dim);

        if (r.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            Status.Summary("Errors:", Theme.Err);
            foreach (var e in r.Errors.Take(20)) Status.Summary("  ! " + e, Theme.Err);
            if (r.Errors.Count > 20) Status.Summary($"  ...and {r.Errors.Count - 20} more", Theme.Dim);
        }

        if (r.NeedsManual.Count > 0)
        {
            AnsiConsole.WriteLine();
            Status.Summary("Needs manual review:", Theme.Active);
            foreach (var m in r.NeedsManual.Take(30))
                Status.Summary($"  - [{m.Kind}] {Path.GetFileName(m.Path)} — {m.Reason}", Theme.Dim);
            if (r.NeedsManual.Count > 30) Status.Summary($"  ...and {r.NeedsManual.Count - 30} more", Theme.Dim);
        }
    }
}
