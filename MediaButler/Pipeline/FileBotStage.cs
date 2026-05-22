using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Menu;
using MediaButler.Settings;

namespace MediaButler.Pipeline;

/// <summary>
/// Stage 2/3/4: Invoke FileBot for episode/movie rename, artwork, and (optionally)
/// subtitles. Re-scans the source root each entry so the TV vs Movie split is
/// computed from current on-disk state, not stale items.
///
/// <para>When <see cref="MediaButlerSettings.DryRun"/> is true, FileBot is still
/// invoked but with <c>--action TEST</c> so it prints its planned actions without
/// renaming anything. Artwork and subtitle fetches are skipped entirely in dry-run
/// because they always mutate the destination.</para>
/// </summary>
public sealed class FileBotStage
{
    private readonly MediaButlerSettings settings;
    private readonly FileBotClient fileBot;
    private readonly SubtitleCredentials credentials;
    private readonly PipelineReport report;

    public FileBotStage(MediaButlerSettings settings, FileBotClient fileBot, PipelineReport report)
        : this(settings, fileBot, report, SubtitleCredentials.Load()) { }

    public FileBotStage(MediaButlerSettings settings, FileBotClient fileBot, PipelineReport report, SubtitleCredentials credentials)
    {
        this.settings    = settings;
        this.fileBot     = fileBot;
        this.report      = report;
        this.credentials = credentials;
    }

    public void Run()
    {
        if (settings.DryRun)
            ConsoleMenu.Status("DRY RUN — FileBot will run with --action TEST; artwork/subtitle fetches skipped.", ConsoleMenu.Active);
        RunTv();
        RunMovies();
        if (settings.EnableSubtitles) RunSubtitles();
    }

    public void RunTv()
    {
        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.TvSeason)
            .OrderBy(i => i.ShowName)
            .ThenBy(i => i.SeasonNumber)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("No TV season folders to process.", ConsoleMenu.Dim);
            return;
        }

        ConsoleMenu.Status($"Processing {items.Count} TV season folder(s)...", ConsoleMenu.Normal);
        Console.WriteLine();

        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            try
            {
                if (settings.RenameEpisodes)
                {
                    var rn = fileBot.RenameTvEpisodes(item.FullPath, dryRun: settings.DryRun);
                    RecordFileBotOutcome("rename", item.FullPath, rn, settings.DryRun, ok => report.FileBotTvOk += ok ? 1 : 0);
                }
                if (settings.FetchArtwork && !settings.DryRun)
                {
                    var aw = fileBot.FetchTvArtwork(item.FullPath);
                    RecordFileBotOutcome("artwork", item.FullPath, aw, dryRun: false, ok => report.ArtworkOk += ok ? 1 : 0);
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                ConsoleMenu.WriteColor("  ! " + ex.Message, ConsoleMenu.Err, newline: true);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    public void RunMovies()
    {
        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.Movie)
            .OrderBy(i => i.MovieTitle)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("No movie folders to process.", ConsoleMenu.Dim);
            return;
        }

        ConsoleMenu.Status($"Processing {items.Count} movie folder(s)...", ConsoleMenu.Normal);
        Console.WriteLine();

        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            try
            {
                // Rename movie files first — this both cleans the filenames
                // (Heat.1995.YIFY.mp4 -> Heat (1995).mp4) and writes the xattr
                // metadata the artwork script needs.
                if (settings.RenameMovies)
                {
                    var rn = fileBot.RenameMovie(item.FullPath, dryRun: settings.DryRun);
                    RecordFileBotOutcome("rename", item.FullPath, rn, settings.DryRun, ok => report.FileBotMoviesOk += ok ? 1 : 0);
                }
                if (settings.FetchArtwork && !settings.DryRun)
                {
                    var aw = fileBot.FetchMovieArtwork(item.FullPath);
                    RecordFileBotOutcome("artwork", item.FullPath, aw, dryRun: false, ok => report.ArtworkOk += ok ? 1 : 0);
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                ConsoleMenu.WriteColor("  ! " + ex.Message, ConsoleMenu.Err, newline: true);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    /// <summary>
    /// Print a one-line success/failure for a FileBot invocation and record the
    /// outcome into <see cref="report"/>. On failure the last interesting stderr
    /// (or stdout) line is shown to the user and added to the report's error
    /// list so the final summary explains <em>why</em>, not just the exit code.
    /// </summary>
    private void RecordFileBotOutcome(string label, string itemPath, FileBot.FileBotResult result, bool dryRun, Action<bool> bumpOk)
    {
        if (result.Success)
        {
            ConsoleMenu.WriteColor(dryRun ? $"  [dry {label} ok]" : $"  [{label} ok]", ConsoleMenu.Ok);
            bumpOk(true);
            return;
        }
        var detail = result.LastInterestingLine();
        ConsoleMenu.WriteColor($"  [{label}: exit {result.ExitCode}]", ConsoleMenu.Err);
        if (!string.IsNullOrWhiteSpace(detail))
            ConsoleMenu.WriteColor($"    {Truncate(detail, 160)}", ConsoleMenu.Dim, newline: true);
        var message = $"FileBot {label} exit {result.ExitCode}" +
                      (string.IsNullOrWhiteSpace(detail) ? "" : $": {Truncate(detail, 240)}");
        report.RecordError(itemPath, message);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void RunSubtitles()
    {
        if (settings.DryRun)
        {
            ConsoleMenu.Status("DRY RUN — subtitle fetch skipped (would download SRTs).", ConsoleMenu.Active);
            return;
        }

        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind is MediaKind.TvSeason or MediaKind.Movie)
            .ToList();

        if (items.Count == 0)
        {
            ConsoleMenu.Status("Nothing to fetch subtitles for.", ConsoleMenu.Dim);
            return;
        }

        var creds = credentials;
        if (creds.IsComplete)
            ConsoleMenu.Status($"Fetching {settings.SubtitleLanguage} subtitles for {items.Count} folder(s) as '{creds.User}'...", ConsoleMenu.Normal);
        else
            ConsoleMenu.Status($"Fetching {settings.SubtitleLanguage} subtitles for {items.Count} folder(s) (no MindAttic Vault creds — relying on FileBot Preferences)...", ConsoleMenu.Dim);
        Console.WriteLine();

        var authWarned = false;
        foreach (var item in items)
        {
            Console.Write("  " + item.OriginalName);
            var sub = fileBot.GetSubtitles(item.FullPath, settings.SubtitleLanguage, creds);
            if (sub.LooksLikeAuthFailure)
            {
                ConsoleMenu.WriteColor("  [auth failed]", ConsoleMenu.Err, newline: true);
                report.RecordError(item.FullPath, "OpenSubtitles 401");
                if (!authWarned)
                {
                    ConsoleMenu.Status(
                        "OpenSubtitles rejected the login. Set 'MindAttic:Vault:Subtitles:OpenSubtitles:user' and ':password' via `dotnet user-secrets set` or environment variables.",
                        ConsoleMenu.Err);
                    authWarned = true;
                }
                continue;
            }
            if (sub.Success)
            {
                ConsoleMenu.WriteColor("  [ok]", ConsoleMenu.Ok, newline: true);
                report.SubtitlesOk++;
            }
            else
            {
                ConsoleMenu.WriteColor("  [exit " + sub.ExitCode + "]", ConsoleMenu.Err, newline: true);
                report.RecordError(item.FullPath, "FileBot subtitle exit " + sub.ExitCode);
            }
        }
    }
}
