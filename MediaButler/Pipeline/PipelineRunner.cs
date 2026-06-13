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
    public int RunFull(MediaButlerSettings s) => RunAcrossSources(s, src => RunStage(src, report =>
    {
        new RenameStage(src, report).Run();
        var fb = FileBotClient.TryCreate(src);
        if (fb is null)
            Status.Print("FileBot not found at " + src.FileBotPath + " — skipping FileBot stages.", Theme.Err);
        else
            new FileBotStage(src, fb, report).Run();
        new MoveStage(src, report).Run();
    }));

    public int RunRename(MediaButlerSettings s) =>
        RunAcrossSources(s, src => RunStage(src, report => new RenameStage(src, report).Run()));

    public int RunFileBotTv(MediaButlerSettings s) =>
        RunAcrossSources(s, src => RunWithFileBot(src, (fb, report) => new FileBotStage(src, fb, report).RunTv()));

    public int RunFileBotMovies(MediaButlerSettings s) =>
        RunAcrossSources(s, src => RunWithFileBot(src, (fb, report) => new FileBotStage(src, fb, report).RunMovies()));

    public int RunFileBotSubtitles(MediaButlerSettings s)
    {
        if (!s.EnableSubtitles)
        {
            Status.Print("Subtitles are disabled in Settings. Enable EnableSubtitles to use this command.", Theme.Dim);
            return 0;
        }
        return RunAcrossSources(s, src => RunWithFileBot(src, (fb, report) => new FileBotStage(src, fb, report).RunSubtitles()));
    }

    public int RunMove(MediaButlerSettings s) =>
        RunAcrossSources(s, src => RunStage(src, report => new MoveStage(src, report).Run()));

    /// <summary>
    /// Every inbox the pipeline should process: the primary
    /// <see cref="MediaButlerSettings.SourcePath"/> plus any
    /// <see cref="MediaButlerSettings.ExtraSources"/>, deduplicated, in order.
    /// </summary>
    internal static IReadOnlyList<string> EffectiveSources(MediaButlerSettings s)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return;
            var trimmed = src.Trim();
            if (seen.Add(Path.TrimEndingDirectorySeparator(trimmed))) list.Add(trimmed);
        }

        foreach (var src in new[] { s.SourcePath }.Concat(s.ExtraSources)) Add(src);

        // --recursive: container subfolders (the ExcludedFolders set — temp,
        // incomplete, ...) become inboxes of their own, recursively. The scan
        // of a parent source skips them as children, so each is processed
        // exactly once, as a root.
        if (s.Recursive)
        {
            var containers = new HashSet<string>(s.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < list.Count; i++)
            {
                if (!Directory.Exists(list[i])) continue;
                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(list[i]).ToList(); }
                catch { continue; }
                foreach (var sub in subs)
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.')) continue;
                    if (containers.Contains(name)) Add(sub);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Run a single-source stage body once per effective source. Exit codes
    /// combine by severity: any 1 (errors) wins, then 2 (needs manual), then 0.
    /// The settings instance is cloned per source so the body can't leak a
    /// mutated SourcePath into the caller's view.
    /// </summary>
    private static int RunAcrossSources(MediaButlerSettings s, Func<MediaButlerSettings, int> body)
    {
        var sources = EffectiveSources(s);
        var worst = ExitOk;
        foreach (var src in sources)
        {
            if (sources.Count > 1)
            {
                AnsiConsole.WriteLine();
                Status.Print($"==== Source: {src} ====", Theme.Header);
            }
            var perSource = Clone(s);
            perSource.SourcePath = src;
            perSource.ExtraSources = Array.Empty<string>();
            worst = CombineExit(worst, body(perSource));
        }
        return worst;
    }

    internal static int CombineExit(int a, int b) =>
        a == ExitErrors || b == ExitErrors ? ExitErrors : Math.Max(a, b);

    private static MediaButlerSettings Clone(MediaButlerSettings s) => new()
    {
        SourcePath            = s.SourcePath,
        ExtraSources          = s.ExtraSources,
        TvDestination         = s.TvDestination,
        MoviesDestination     = s.MoviesDestination,
        MusicDestination      = s.MusicDestination,
        Recursive             = false, // sources are already expanded by the caller
        AudioExtensions       = s.AudioExtensions,
        FileBotPath           = s.FileBotPath,
        SubtitleLanguage      = s.SubtitleLanguage,
        EnableSubtitles       = s.EnableSubtitles,
        RenameEpisodes        = s.RenameEpisodes,
        RenameMovies          = s.RenameMovies,
        FetchArtwork          = s.FetchArtwork,
        DryRun                = s.DryRun,
        EnableLlmFallback     = s.EnableLlmFallback,
        LlmProvider           = s.LlmProvider,
        ExcludedFolders       = s.ExcludedFolders,
        VideoExtensions       = s.VideoExtensions,
        EmptyDeleteSafetyBytes = s.EmptyDeleteSafetyBytes,
        SampleMaxBytes        = s.SampleMaxBytes,
        SubtitleExtensions    = s.SubtitleExtensions,
        VariationCatalogPath  = s.VariationCatalogPath,
        ShowLevelArtFiles     = s.ShowLevelArtFiles,
        TitleYearOverrides    = s.TitleYearOverrides,
    };

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
        return RunStage(s, report => new RelocateStage(s, report).Run(), guardPaths: false);
    }

    public int RunScan(MediaButlerSettings s) => RunAcrossSources(s, src =>
    {
        if (!Directory.Exists(src.SourcePath))
        {
            Status.Print("Source path not found: " + src.SourcePath, Theme.Err);
            return 1;
        }
        var items = new MediaScanner(src).Scan().ToList();
        foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
        {
            Status.Print($"{grp.Key} ({grp.Count()}):", Theme.Header);
            foreach (var it in grp.OrderBy(i => i.OriginalName))
            {
                var detail = it.Kind switch
                {
                    MediaKind.Movie             => $" -> {NameParser.FormatMovieFolder(it.MovieTitle ?? "?", it.MovieYear)}",
                    MediaKind.TvSeason          => $" -> {NameParser.FormatSeasonFolder(it.ShowName ?? "?", it.SeasonNumber ?? 0)}",
                    MediaKind.TvEpisode         => $" -> {NameParser.FormatSeasonFolder(it.ShowName ?? "?", it.SeasonNumber ?? 0)} (episode {it.EpisodeNumber})",
                    MediaKind.MoviePack         => $" ({it.PackMovies.Count} movie(s) to split)",
                    MediaKind.MultiSeasonParent => $" ({it.Seasons.Count} season folder(s), {it.LooseEpisodes.Count} loose episode(s), show='{it.ShowName ?? "?"}')",
                    _                           => "",
                };
                Status.Print($"  {it.OriginalName}{detail}", Theme.Dim);
            }
        }
        Status.Summary($"Total: {items.Count} root item(s).", Theme.Normal);
        return 0;
    });

    public int ShowStatus(MediaButlerSettings s)
    {
        Status.Print("Settings file: " + settings.FilePath, Theme.Normal);
        Status.Print("Mode         : " + (s.DryRun ? "DRY RUN" : "LIVE"), s.DryRun ? Theme.Active : Theme.Ok);
        var sourceOk = Directory.Exists(s.SourcePath);
        Status.Print("Source       : " + s.SourcePath + " " + (sourceOk ? "[ok]" : "[MISSING]"), sourceOk ? Theme.Ok : Theme.Err);
        foreach (var extra in s.ExtraSources)
        {
            var ok = Directory.Exists(extra);
            Status.Print("Extra source : " + extra + " " + (ok ? "[ok]" : "[MISSING]"), ok ? Theme.Ok : Theme.Err);
        }
        Status.Print("Variations   : " + VariationCatalog.ResolvePath(s), Theme.Normal);
        Status.Print("TV dest      : " + s.TvDestination, Theme.Normal);
        Status.Print("Movies dest  : " + s.MoviesDestination, Theme.Normal);
        Status.Print("Music dest   : " + (string.IsNullOrWhiteSpace(s.MusicDestination) ? "(disabled)" : s.MusicDestination), Theme.Normal);
        Status.Print("Recursive    : " + (s.Recursive ? "true (container subfolders become inboxes)" : "false"), Theme.Normal);
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

    /// <summary>
    /// Standard headless flow: validate paths (unless the caller opts out for
    /// the Relocate carve-out), run the supplied stage body against a fresh
    /// <see cref="PipelineReport"/>, print the consolidated report, and map
    /// "any errors" to exit code 1.
    /// </summary>
    /// <summary>
    /// Exit code conventions for pipeline runs:
    /// <list type="bullet">
    ///   <item><c>0</c>: clean — no errors, nothing in the manual-review list.</item>
    ///   <item><c>1</c>: errors occurred (exceptions, FileBot non-zero exits, IO failures).</item>
    ///   <item><c>2</c>: no errors but items need a human eye (Unknown folders,
    ///         target-exists skips, partial-copy markers). Cron jobs should
    ///         treat this as actionable, not silent success.</item>
    /// </list>
    /// </summary>
    public const int ExitOk = 0;
    public const int ExitErrors = 1;
    public const int ExitNeedsManual = 2;

    private int RunStage(MediaButlerSettings s, Action<PipelineReport> body, bool guardPaths = true)
    {
        if (guardPaths && !PathGuard.ValidatePaths(s)) return ExitErrors;
        var report = new PipelineReport();
        var auditFailuresBefore = AuditLog.FailureCount;
        body(report);
        PrintReport(s, report, AuditLog.FailureCount - auditFailuresBefore);
        if (report.Errors.Count > 0) return ExitErrors;
        if (report.NeedsManual.Count > 0) return ExitNeedsManual;
        return ExitOk;
    }

    /// <summary>
    /// Variant of <see cref="RunStage"/> that resolves a <see cref="FileBotClient"/>
    /// up front and short-circuits with a clear error message if FileBot isn't
    /// installed — used by the three FileBot-only subcommands.
    /// </summary>
    private int RunWithFileBot(MediaButlerSettings s, Action<FileBotClient, PipelineReport> body)
    {
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { Status.Print("FileBot not found.", Theme.Err); return 1; }
        return RunStage(s, report => body(fb, report));
    }

    public void PrintReport(MediaButlerSettings s, PipelineReport r) => PrintReport(s, r, auditFailures: 0);

    public void PrintReport(MediaButlerSettings s, PipelineReport r, int auditFailures)
    {
        AnsiConsole.WriteLine();
        Status.Summary("---- Pipeline summary ----", Theme.Header);
        Status.Summary($"Mode             : {(s.DryRun ? "DRY RUN" : "LIVE")}", s.DryRun ? Theme.Active : Theme.Ok);
        Status.Summary($"Renamed locally  : {r.Renamed}", Theme.Normal);
        Status.Summary($"Hoisted seasons  : {r.Hoisted}", Theme.Normal);
        Status.Summary($"Episodes filed   : {r.Consolidated}", Theme.Normal);
        Status.Summary($"Pack movies split: {r.PackSplit}", Theme.Normal);
        Status.Summary($"Merged files     : {r.MergedFiles}", Theme.Normal);
        Status.Summary($"Empty deleted    : {r.EmptyDeleted}", Theme.Normal);
        Status.Summary($"FileBot TV ok    : {r.FileBotTvOk}", Theme.Normal);
        Status.Summary($"FileBot Movies ok: {r.FileBotMoviesOk}", Theme.Normal);
        Status.Summary($"Artwork ok       : {r.ArtworkOk}", Theme.Normal);
        Status.Summary($"Subtitles ok     : {r.SubtitlesOk}", Theme.Normal);
        Status.Summary($"Moved to TV      : {r.TvMoved}", Theme.Ok);
        Status.Summary($"Moved to Movies  : {r.MoviesMoved}", Theme.Ok);
        Status.Summary($"Moved to Music   : {r.MusicMoved}", Theme.Ok);
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

        if (auditFailures > 0)
        {
            AnsiConsole.WriteLine();
            Status.Summary($"WARNING: {auditFailures} audit log write(s) failed during this run.", Theme.Err);
            if (!string.IsNullOrWhiteSpace(AuditLog.LastFailureMessage))
                Status.Summary("  Last failure: " + AuditLog.LastFailureMessage, Theme.Dim);
            Status.Summary("  The audit log at " + AuditLog.FilePath() + " is incomplete.", Theme.Dim);
        }
    }
}
