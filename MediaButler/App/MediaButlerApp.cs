using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Menu;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.App;

/// <summary>
/// Top-level MediaButler application: owns the settings service, builds the main
/// menu, and dispatches to each pipeline stage. Always re-reads settings
/// inside menu callbacks so the user can change Source/Destination/FileBot
/// path mid-session without restarting.
///
/// <para><b>CLI flags</b> recognised by <see cref="RunAsync"/>:</para>
/// <list type="bullet">
///   <item><c>--dry-run</c> / <c>-n</c>: force dry-run for this session, ignoring the persisted setting.</item>
///   <item><c>--run</c>: execute the full pipeline non-interactively and exit (script/cron friendly).
///         Combine with <c>--dry-run</c> to preview against a real library without opening the menu.</item>
/// </list>
/// </summary>
public sealed class MediaButlerApp
{
    private readonly SettingsService settings = new();
    private CliOptions cli = new();

    public Task<int> RunAsync(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected */ }

        cli = CliOptions.Parse(args);
        if (cli.ShowVersion)
        {
            Console.WriteLine(VersionString());
            return Task.FromResult(0);
        }
        if (cli.ShowHelp)
        {
            Console.WriteLine(CliOptions.HelpText);
            return Task.FromResult(0);
        }
        if (cli.UnknownArg is not null)
        {
            Console.Error.WriteLine($"Unknown argument: {cli.UnknownArg}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.HelpText);
            return Task.FromResult(1);
        }

        ConsoleMenu.Verbosity = cli.Verbosity;

        // Ensure settings.json exists so the user can see a concrete defaults
        // file the first time they open the Settings menu.
        settings.EnsureFolder();
        _ = LoadEffective();

        return Task.FromResult(cli.Command switch
        {
            CliCommand.RunFull          => RunFullPipelineHeadless(),
            CliCommand.Scan             => RunScan(),
            CliCommand.Rename           => RunRenameHeadless(),
            CliCommand.FileBotTv        => RunFileBotTvHeadless(),
            CliCommand.FileBotMovies    => RunFileBotMoviesHeadless(),
            CliCommand.FileBotSubtitles => RunFileBotSubtitlesHeadless(),
            CliCommand.Move             => RunMoveHeadless(),
            CliCommand.Relocate         => RunRelocateHeadless(),
            CliCommand.Status           => RunStatusHeadless(),
            _                           => RunMenu(),
        });
    }

    private int RunMenu()
    {
        ConsoleMenu.Show(Array.Empty<string>(), BuildMainMenu);
        return 0;
    }

    /// <summary>Assembly informational version, falling back to file version.</summary>
    private static string VersionString()
    {
        var asm = typeof(MediaButlerApp).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                      .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                      .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return $"MediaButler {info}";
        var ver = asm.GetName().Version?.ToString() ?? "0.0.0";
        return $"MediaButler {ver}";
    }

    private int RunFullPipelineHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("Run Full Pipeline (headless)");
        if (!ValidatePaths(s)) return 1;

        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null)
            ConsoleMenu.Status("FileBot not found at " + s.FileBotPath + " — skipping FileBot stages.", ConsoleMenu.Err);
        else
            new FileBotStage(s, fb, report).Run();
        new MoveStage(s, report).Run();

        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunScan()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("Scan (read-only)");
        if (!Directory.Exists(s.SourcePath))
        {
            ConsoleMenu.Status("Source path not found: " + s.SourcePath, ConsoleMenu.Err);
            return 1;
        }
        var items = new MediaScanner(s).Scan().ToList();
        foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
        {
            ConsoleMenu.Status($"{grp.Key} ({grp.Count()}):", ConsoleMenu.Header);
            foreach (var it in grp.OrderBy(i => i.OriginalName))
            {
                var detail = it.Kind switch
                {
                    MediaKind.Movie               => $" -> {NameParser.FormatMovieFolder(it.MovieTitle ?? "?", it.MovieYear)}",
                    MediaKind.TvSeason            => $" -> {NameParser.FormatSeasonFolder(it.ShowName ?? "?", it.SeasonNumber ?? 0)}",
                    MediaKind.MultiSeasonParent   => $" ({it.Seasons.Count} season(s), show='{it.ShowName ?? "?"}')",
                    _                             => "",
                };
                ConsoleMenu.Status($"  {it.OriginalName}{detail}", ConsoleMenu.Dim);
            }
        }
        ConsoleMenu.Summary($"Total: {items.Count} root folder(s).", ConsoleMenu.Normal);
        return 0;
    }

    private int RunRenameHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("Rename & Hoist (headless)");
        if (!ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunFileBotTvHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("FileBot TV (headless)");
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { ConsoleMenu.Status("FileBot not found.", ConsoleMenu.Err); return 1; }
        if (!ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunTv();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunFileBotMoviesHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("FileBot Movies (headless)");
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { ConsoleMenu.Status("FileBot not found.", ConsoleMenu.Err); return 1; }
        if (!ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunMovies();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunFileBotSubtitlesHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("FileBot Subtitles (headless)");
        if (!s.EnableSubtitles)
        {
            ConsoleMenu.Status("Subtitles are disabled in Settings. Enable EnableSubtitles to use this command.", ConsoleMenu.Dim);
            return 0;
        }
        var fb = FileBotClient.TryCreate(s);
        if (fb is null) { ConsoleMenu.Status("FileBot not found.", ConsoleMenu.Err); return 1; }
        if (!ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunSubtitles();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunMoveHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("Move to Plex (headless)");
        if (!ValidatePaths(s)) return 1;
        var report = new PipelineReport();
        new MoveStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunRelocateHeadless()
    {
        var s = LoadEffective();
        ConsoleMenu.WriteHeader("Relocate (headless)");
        if (!Directory.Exists(s.SourcePath))
        {
            ConsoleMenu.Status("Source path not found: " + s.SourcePath, ConsoleMenu.Err);
            return 1;
        }
        // Relocate is the one stage that deliberately runs against a destination
        // — pointing it at M:\Movies to evict a stray TvSeason is the whole
        // point. So the source-vs-dest overlap guard is not applied here.
        var report = new PipelineReport();
        new RelocateStage(s, report).Run();
        PrintReport(s, report);
        return report.Errors.Count == 0 ? 0 : 1;
    }

    private int RunStatusHeadless()
    {
        ShowStatus();
        return 0;
    }

    /// <summary>
    /// Load settings and overlay the parsed CLI options (dry-run, source, dest
    /// overrides). Stages should only see the effective settings — never the
    /// on-disk values directly — so CLI overrides take effect every place a
    /// stage looks up a path.
    /// </summary>
    private MediaButlerSettings LoadEffective()
    {
        var s = settings.Load();
        cli.ApplyTo(s);
        return s;
    }

    private IReadOnlyList<MenuItem> BuildMainMenu()
    {
        var s = LoadEffective();
        var fileBot = FileBotClient.TryLocate(s.FileBotPath);
        var fileBotDesc = fileBot is null
            ? "NOT FOUND — set FileBot Path in Settings"
            : "ready: " + fileBot;

        var modeLabel = s.DryRun
            ? (cli.DryRun ? "DRY RUN (CLI --dry-run)" : "DRY RUN (Settings toggle)")
            : "LIVE";

        return new List<MenuItem>
        {
            new()
            {
                Label = $"Run Full Pipeline  [{modeLabel}]",
                Description = "rename + hoist, FileBot rename + artwork, subtitles, move to Plex",
                OnSelect = RunFullPipeline,
            },
            new()
            {
                Label = "1. Rename & Hoist",
                Description = "clean folder names, hoist nested seasons, pad to Season 01",
                OnSelect = RunRenameAndHoist,
            },
            new()
            {
                Label = "2. FileBot: TV Episodes + Artwork",
                Description = "rename episodes via TheTVDB, fetch posters/banners/fanart",
                OnSelect = RunFileBotTv,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "3. FileBot: Movies + Artwork",
                Description = "rename movies via TheMovieDB, fetch poster + backdrop",
                OnSelect = RunFileBotMovies,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "4. FileBot: Subtitles",
                Description = "OpenSubtitles via User Secrets (or FileBot Preferences)",
                OnSelect = RunFileBotSubtitles,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "5. Move to Plex",
                Description = $"to {s.TvDestination} and {s.MoviesDestination}",
                OnSelect = RunMoveToPlex,
            },
            new()
            {
                Label = "Relocate misplaced items",
                Description = "scan a destination dir and evict items whose kind doesn't belong",
                OnSelect = RunRelocateInteractive,
            },
            new()
            {
                Label = "Settings",
                Description = "source, destinations, FileBot path, options, dry-run",
                OnSelect = () => { new SettingsEditor(settings).Show(); return true; },
            },
            new()
            {
                Label = "Status",
                Description = $"FileBot: {fileBotDesc}",
                OnSelect = () => { ShowStatus(); return true; },
            },
            new()
            {
                Label = "Exit",
                Description = "quit MediaButler",
                OnSelect = () => false,
            },
        };
    }

    private bool RunFullPipeline()
    {
        ConsoleMenu.WriteHeader("Run Full Pipeline");
        var s = LoadEffective();
        if (!ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }

        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null)
        {
            ConsoleMenu.Status("FileBot not found at " + s.FileBotPath + " — skipping FileBot stages.", ConsoleMenu.Err);
        }
        else
        {
            new FileBotStage(s, fb, report).Run();
        }
        new MoveStage(s, report).Run();

        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunRenameAndHoist()
    {
        ConsoleMenu.WriteHeader("Rename & Hoist");
        var s = LoadEffective();
        if (!ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }
        var report = new PipelineReport();
        new RenameStage(s, report).Run();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotTv()
    {
        ConsoleMenu.WriteHeader("FileBot: TV Episodes + Artwork");
        var s = LoadEffective();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunTv();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotMovies()
    {
        ConsoleMenu.WriteHeader("FileBot: Movies + Artwork");
        var s = LoadEffective();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunMovies();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotSubtitles()
    {
        ConsoleMenu.WriteHeader("FileBot: Subtitles");
        var s = LoadEffective();
        if (!s.EnableSubtitles)
        {
            ConsoleMenu.Status("Subtitles are disabled in Settings.", ConsoleMenu.Dim);
            ConsoleMenu.WaitForKey();
            return true;
        }
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }
        var report = new PipelineReport();
        new FileBotStage(s, fb, report).RunSubtitles();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunRelocateInteractive()
    {
        ConsoleMenu.WriteHeader("Relocate misplaced items");
        var s = LoadEffective();
        var prompt = ConsoleMenu.Prompt(
            "Folder to scan (e.g. M:\\Movies or M:\\TV)",
            currentValue: s.SourcePath);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ConsoleMenu.Status("Cancelled — no folder supplied.", ConsoleMenu.Dim);
            ConsoleMenu.WaitForKey();
            return true;
        }
        s.SourcePath = prompt;
        if (!Directory.Exists(s.SourcePath))
        {
            ConsoleMenu.Status("Folder not found: " + s.SourcePath, ConsoleMenu.Err);
            ConsoleMenu.WaitForKey();
            return true;
        }

        var report = new PipelineReport();
        new RelocateStage(s, report).Run();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunMoveToPlex()
    {
        ConsoleMenu.WriteHeader("Move to Plex");
        var s = LoadEffective();
        if (!ValidatePaths(s)) { ConsoleMenu.WaitForKey(); return true; }
        var report = new PipelineReport();
        new MoveStage(s, report).Run();
        PrintReport(s, report);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private void ShowStatus()
    {
        ConsoleMenu.WriteHeader("Status");
        var s = LoadEffective();
        ConsoleMenu.Status("Settings file: " + settings.FilePath, ConsoleMenu.Normal);
        ConsoleMenu.Status("Mode         : " + (s.DryRun ? "DRY RUN" : "LIVE"), s.DryRun ? ConsoleMenu.Active : ConsoleMenu.Ok);
        ConsoleMenu.Status("Source       : " + s.SourcePath + " " + (Directory.Exists(s.SourcePath) ? "[ok]" : "[MISSING]"), Directory.Exists(s.SourcePath) ? ConsoleMenu.Ok : ConsoleMenu.Err);
        ConsoleMenu.Status("TV dest      : " + s.TvDestination, ConsoleMenu.Normal);
        ConsoleMenu.Status("Movies dest  : " + s.MoviesDestination, ConsoleMenu.Normal);
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        ConsoleMenu.Status("FileBot      : " + (fb ?? "NOT FOUND"), fb is null ? ConsoleMenu.Err : ConsoleMenu.Ok);

        var creds = SubtitleCredentials.Load();
        ConsoleMenu.Status("OpenSubtitles: " + (creds.IsComplete ? $"configured as '{creds.User}'" : "no MindAttic Vault credentials"),
            creds.IsComplete ? ConsoleMenu.Ok : ConsoleMenu.Dim);

        if (Directory.Exists(s.SourcePath))
        {
            var scanner = new MediaScanner(s);
            var items = scanner.Scan().ToList();
            ConsoleMenu.Status($"Scanned {items.Count} root folder(s):", ConsoleMenu.Normal);
            foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
                ConsoleMenu.Status($"  {grp.Key,-20} {grp.Count()}", ConsoleMenu.Dim);
        }

        ConsoleMenu.WaitForKey();
    }

    /// <summary>
    /// Validate source exists AND is not the same as (or nested under) a destination.
    /// In live mode the overlap is a hard refusal — pointing the source at
    /// <c>M:\TV</c> would destroy an already-organized library because every show
    /// folder looks like a multi-season parent to hoist. In dry-run we just warn,
    /// because no mutations are possible and inspecting the destination is a
    /// legitimate use case ("what does the parser think of my Movies library?").
    /// </summary>
    private static bool ValidatePaths(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            ConsoleMenu.Status("Source path not found: " + s.SourcePath, ConsoleMenu.Err);
            ConsoleMenu.Status("Set it via the Settings menu.", ConsoleMenu.Dim);
            return false;
        }

        var overlap = PathOverlaps(s.SourcePath, s.TvDestination) || PathOverlaps(s.SourcePath, s.MoviesDestination);
        if (overlap)
        {
            if (s.DryRun)
            {
                ConsoleMenu.Status("WARNING: Source path overlaps a destination. Dry-run only — no mutations will occur.", ConsoleMenu.Active);
                ConsoleMenu.Status($"  Source : {s.SourcePath}", ConsoleMenu.Active);
                ConsoleMenu.Status($"  TV     : {s.TvDestination}", ConsoleMenu.Active);
                ConsoleMenu.Status($"  Movies : {s.MoviesDestination}", ConsoleMenu.Active);
                return true;
            }
            ConsoleMenu.Status("REFUSING TO RUN: Source path overlaps a destination.", ConsoleMenu.Err);
            ConsoleMenu.Status($"  Source : {s.SourcePath}", ConsoleMenu.Err);
            ConsoleMenu.Status($"  TV     : {s.TvDestination}", ConsoleMenu.Err);
            ConsoleMenu.Status($"  Movies : {s.MoviesDestination}", ConsoleMenu.Err);
            ConsoleMenu.Status("Running here would reprocess already-organized folders and could destroy data.", ConsoleMenu.Err);
            ConsoleMenu.Status("Pass --dry-run to inspect classification without risk.", ConsoleMenu.Dim);
            return false;
        }

        return true;
    }

    /// <summary>True when <paramref name="source"/> equals, contains, or is contained by <paramref name="other"/>.</summary>
    internal static bool PathOverlaps(string source, string other)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(other)) return false;
        string a, b;
        try
        {
            a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));
        }
        catch { return false; }

        var cmp = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(a, b, cmp)) return true;
        var sep = Path.DirectorySeparatorChar.ToString();
        return a.StartsWith(b + sep, cmp) || b.StartsWith(a + sep, cmp);
    }

    private static void PrintReport(MediaButlerSettings s, PipelineReport r)
    {
        Console.WriteLine();
        ConsoleMenu.Summary("---- Pipeline summary ----", ConsoleMenu.Header);
        ConsoleMenu.Summary($"Mode             : {(s.DryRun ? "DRY RUN" : "LIVE")}", s.DryRun ? ConsoleMenu.Active : ConsoleMenu.Ok);
        ConsoleMenu.Summary($"Renamed locally  : {r.Renamed}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"Hoisted seasons  : {r.Hoisted}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"Empty deleted    : {r.EmptyDeleted}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"FileBot TV ok    : {r.FileBotTvOk}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"FileBot Movies ok: {r.FileBotMoviesOk}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"Artwork ok       : {r.ArtworkOk}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"Subtitles ok     : {r.SubtitlesOk}", ConsoleMenu.Normal);
        ConsoleMenu.Summary($"Moved to TV      : {r.TvMoved}", ConsoleMenu.Ok);
        ConsoleMenu.Summary($"Moved to Movies  : {r.MoviesMoved}", ConsoleMenu.Ok);
        ConsoleMenu.Summary($"Errors           : {r.Errors.Count}", r.Errors.Count > 0 ? ConsoleMenu.Err : ConsoleMenu.Dim);
        ConsoleMenu.Summary($"Needs manual fix : {r.NeedsManual.Count}", r.NeedsManual.Count > 0 ? ConsoleMenu.Active : ConsoleMenu.Dim);

        if (r.Errors.Count > 0)
        {
            Console.WriteLine();
            ConsoleMenu.Summary("Errors:", ConsoleMenu.Err);
            foreach (var e in r.Errors.Take(20)) ConsoleMenu.Summary("  ! " + e, ConsoleMenu.Err);
            if (r.Errors.Count > 20) ConsoleMenu.Summary($"  ...and {r.Errors.Count - 20} more", ConsoleMenu.Dim);
        }

        if (r.NeedsManual.Count > 0)
        {
            Console.WriteLine();
            ConsoleMenu.Summary("Needs manual review:", ConsoleMenu.Active);
            foreach (var m in r.NeedsManual.Take(30))
                ConsoleMenu.Summary($"  - [{m.Kind}] {Path.GetFileName(m.Path)} — {m.Reason}", ConsoleMenu.Dim);
            if (r.NeedsManual.Count > 30) ConsoleMenu.Summary($"  ...and {r.NeedsManual.Count - 30} more", ConsoleMenu.Dim);
        }
    }
}
