using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Settings;
using MediaButler.Ui;

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
    private bool dryRunBannerShown;

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
        RunTv();
        RunMovies();
        if (settings.EnableSubtitles) RunSubtitles();
    }

    public void RunTv()
    {
        AnnounceDryRunOnce();

        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.TvSeason)
            .OrderBy(i => i.ShowName)
            .ThenBy(i => i.SeasonNumber)
            .ToList();

        if (items.Count == 0)
        {
            Status.Print("No TV season folders to process.", Theme.Dim);
            return;
        }

        Status.Print($"Processing {items.Count} TV season folder(s)...", Theme.Normal);
        Status.NewLine();

        foreach (var item in items)
        {
            BeginItem(item.OriginalName);
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
                Status.NewLine();
            }
            catch (Exception ex)
            {
                EndItemWithError(item, ex);
            }
        }
    }

    public void RunMovies()
    {
        AnnounceDryRunOnce();

        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind == MediaKind.Movie)
            .OrderBy(i => i.MovieTitle)
            .ToList();

        if (items.Count == 0)
        {
            Status.Print("No movie folders to process.", Theme.Dim);
            return;
        }

        Status.Print($"Processing {items.Count} movie folder(s)...", Theme.Normal);
        Status.NewLine();

        foreach (var item in items)
        {
            BeginItem(item.OriginalName);
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
                Status.NewLine();
            }
            catch (Exception ex)
            {
                EndItemWithError(item, ex);
            }
        }
    }

    /// <summary>
    /// Write the per-item leading text ("  {item name}") without a newline so
    /// subsequent inline fragments stack on the same row. In Quiet mode the
    /// fragments are suppressed, so this header is suppressed too — otherwise
    /// each item would emit a stray blank line.
    /// </summary>
    /// <summary>
    /// Print the dry-run banner at most once per stage instance. A full pipeline
    /// run invokes RunTv then RunMovies on the same instance; without the guard
    /// the identical banner printed twice (or three times with subtitles).
    /// </summary>
    private void AnnounceDryRunOnce()
    {
        if (!settings.DryRun || dryRunBannerShown) return;
        dryRunBannerShown = true;
        Status.Print("DRY RUN — FileBot will run with --action TEST; artwork fetches skipped.", Theme.Active);
    }

    private static void BeginItem(string name)
    {
        if (Status.Verbosity == Verbosity.Quiet) return;
        Console.Write("  " + name);
    }

    /// <summary>
    /// Close out an in-progress item line on the error path. In non-quiet mode
    /// the leading <see cref="BeginItem"/> wrote "  name" without a newline —
    /// emit one now so the error line doesn't run into the name. Always log
    /// the error with the item name included, so Quiet-mode users (where
    /// <see cref="BeginItem"/> was a no-op) still get context.
    /// </summary>
    private void EndItemWithError(MediaItem item, Exception ex)
    {
        if (Status.Verbosity != Verbosity.Quiet) Console.WriteLine();
        Status.Line($"  ! {item.OriginalName}: {ex.Message}", Theme.Err);
        report.RecordError(item.FullPath, ex.Message);
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
            Status.Inline(dryRun ? $"  [dry {label} ok]" : $"  [{label} ok]", Theme.Ok);
            bumpOk(true);
            return;
        }
        var detail = result.LastInterestingLine();
        Status.Inline($"  [{label}: exit {result.ExitCode}]", Theme.Err);
        if (!string.IsNullOrWhiteSpace(detail))
            Status.Line($"    {Truncate(detail, 160)}", Theme.Dim);
        var message = $"FileBot {label} exit {result.ExitCode}" +
                      (string.IsNullOrWhiteSpace(detail) ? "" : $": {Truncate(detail, 240)}");
        report.RecordError(itemPath, message);
    }

    // Reserve one slot for the ellipsis so the returned string never exceeds
    // max characters (the old s[..max] + "…" produced max + 1).
    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public void RunSubtitles()
    {
        if (settings.DryRun)
        {
            Status.Print("DRY RUN — subtitle fetch skipped (would download SRTs).", Theme.Active);
            return;
        }

        var items = new MediaScanner(settings).Scan()
            .Where(i => i.Kind is MediaKind.TvSeason or MediaKind.Movie)
            .ToList();

        if (items.Count == 0)
        {
            Status.Print("Nothing to fetch subtitles for.", Theme.Dim);
            return;
        }

        var creds = credentials;
        if (creds.IsComplete)
            Status.Print($"Fetching {settings.SubtitleLanguage} subtitles for {items.Count} folder(s) as '{creds.User}'...", Theme.Normal);
        else
            Status.Print($"Fetching {settings.SubtitleLanguage} subtitles for {items.Count} folder(s) (no MindAttic Vault creds — relying on FileBot Preferences)...", Theme.Dim);
        Status.NewLine();

        var authWarned = false;
        foreach (var item in items)
        {
            BeginItem(item.OriginalName);
            try
            {
                var sub = fileBot.GetSubtitles(item.FullPath, settings.SubtitleLanguage, creds);
                if (sub.LooksLikeAuthFailure)
                {
                    if (Status.Verbosity != Verbosity.Quiet) Console.WriteLine();
                    Status.Line($"  ! {item.OriginalName}: OpenSubtitles auth failed (401)", Theme.Err);
                    // Subtitles are best-effort: a rejected login is something the
                    // user must act on (fix credentials), but it must not fail the
                    // whole organize pipeline. Surface it for manual review (exit 2)
                    // rather than as a hard error (exit 1) — matches the documented
                    // "warn instead of failing" contract on FileBotResult.
                    report.RecordManual(item.FullPath, item.Kind, "OpenSubtitles auth failed (401) — check credentials");
                    if (!authWarned)
                    {
                        Status.Print(
                            "OpenSubtitles rejected the login. Set 'MindAttic:Vault:Subtitles:OpenSubtitles:user' and ':password' via `dotnet user-secrets set` or environment variables.",
                            Theme.Err);
                        authWarned = true;
                    }
                    continue;
                }
                if (sub.Success)
                {
                    Status.Inline("  [ok]", Theme.Ok);
                    Status.NewLine();
                    report.SubtitlesOk++;
                }
                else
                {
                    if (Status.Verbosity != Verbosity.Quiet) Console.WriteLine();
                    Status.Line($"  ! {item.OriginalName}: FileBot subtitle exit {sub.ExitCode}", Theme.Err);
                    // A non-zero subtitle exit (often "no subtitles found") is not a
                    // pipeline failure — surface it for manual review, not exit 1.
                    report.RecordManual(item.FullPath, item.Kind, "FileBot subtitle fetch exit " + sub.ExitCode);
                }
            }
            catch (Exception ex)
            {
                // Match RunTv / RunMovies: one folder's failure (temp-file IO,
                // process launch, etc.) records an error and moves on instead of
                // aborting the whole subtitle batch.
                EndItemWithError(item, ex);
            }
        }
    }
}
