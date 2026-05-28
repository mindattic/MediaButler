using MediaButler.Media;
using MediaButler.Settings;
using MediaButler.Ui;

namespace MediaButler.Pipeline;

/// <summary>
/// Library-cleanup pass. Unlike <see cref="MoveStage"/> — which moves freshly-renamed
/// folders out of a staging directory into the Plex destinations — RelocateStage
/// scans an already-organized destination and moves out anything that doesn't
/// belong there.
///
/// <para>The expected kind for the scanned directory is inferred from the path:</para>
/// <list type="bullet">
///   <item>scanning <see cref="MediaButlerSettings.MoviesDestination"/> (or anything
///         under it) → expected kind is <see cref="MediaKind.Movie"/>; any
///         <see cref="MediaKind.TvSeason"/> folder gets relocated to
///         <see cref="MediaButlerSettings.TvDestination"/>.</item>
///   <item>scanning <see cref="MediaButlerSettings.TvDestination"/> → expected kind
///         is <see cref="MediaKind.TvSeason"/>; any <see cref="MediaKind.Movie"/>
///         folder gets relocated to <see cref="MediaButlerSettings.MoviesDestination"/>.</item>
///   <item>scanning anywhere else → no inferred expected kind; both Movie and
///         TvSeason items get moved to their respective destinations.</item>
/// </list>
///
/// <para>Items already at their canonical destination are left alone. Extras,
/// Unknown, Empty, and MultiSeasonParent are surfaced in the manual report
/// (Empty is intentionally <em>not</em> deleted by RelocateStage — that's
/// <see cref="RenameStage"/>'s job and the user might be running against a real
/// library where "Empty" actually means "video extension not in the list").</para>
/// </summary>
public sealed class RelocateStage
{
    private readonly MediaButlerSettings settings;
    private readonly PipelineReport report;

    public RelocateStage(MediaButlerSettings settings, PipelineReport report)
    {
        this.settings = settings;
        this.report   = report;
    }

    public void Run()
    {
        Status.Print("Relocate scan: " + settings.SourcePath, Theme.Normal);
        if (settings.DryRun)
            Status.Print("DRY RUN — no files will be moved.", Theme.Active);

        var expected = InferExpectedKind(settings);
        Status.Print(expected is null
            ? "Expected kind: any (will relocate both TvSeason and Movie items)."
            : $"Expected kind in this folder: {expected}. Other kinds will be relocated.",
            Theme.Dim);
        Status.NewLine();

        var items = new MediaScanner(settings).Scan().ToList();
        foreach (var item in items)
        {
            try
            {
                ProcessItem(item, expected);
            }
            catch (Exception ex)
            {
                Status.Print($"  ! {item.OriginalName}: {ex.Message}", Theme.Err);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    private void ProcessItem(MediaItem item, MediaKind? expected)
    {
        switch (item.Kind)
        {
            case MediaKind.Movie:
                if (expected == MediaKind.Movie) { LogInPlace(item); return; }
                if (MoveItem(item, BuildMovieTarget(item))) report.MoviesMoved++;
                break;

            case MediaKind.TvSeason:
                if (expected == MediaKind.TvSeason) { LogInPlace(item); return; }
                if (MoveItem(item, BuildTvTarget(item))) report.TvMoved++;
                break;

            case MediaKind.Extras:
                LogSkip(item, "extras/specials — left in place");
                report.RecordManual(item.FullPath, item.Kind, "extras/specials folder");
                break;
            case MediaKind.Unknown:
                LogSkip(item, "could not classify");
                report.RecordManual(item.FullPath, item.Kind, "parser could not classify");
                break;
            case MediaKind.Empty:
                LogSkip(item, "no video files detected — left in place (run `rename` to delete)");
                report.RecordManual(item.FullPath, item.Kind, "no recognized video files");
                break;
            case MediaKind.MultiSeasonParent:
                LogSkip(item, "multi-season parent — run `rename` to hoist first");
                report.RecordManual(item.FullPath, item.Kind, "multi-season parent — run rename to hoist");
                break;
        }
    }

    private string? BuildMovieTarget(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            LogSkip(item, "movie missing title");
            report.RecordManual(item.FullPath, item.Kind, "movie missing title");
            return null;
        }
        var folder = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        return Path.Combine(settings.MoviesDestination, MoveStage.SanitizeForFs(folder));
    }

    private string? BuildTvTarget(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            LogSkip(item, "TV item missing show/season");
            report.RecordManual(item.FullPath, item.Kind, "TV item missing show/season");
            return null;
        }
        var showRoot = Path.Combine(settings.TvDestination, MoveStage.SanitizeForFs(item.ShowName));
        return Path.Combine(showRoot, $"Season {item.SeasonNumber:D2}");
    }

    /// <summary>
    /// Move <paramref name="item"/> to <paramref name="target"/>. Returns true
    /// only when something was relocated (or, in dry-run, <em>would</em> be) so
    /// the caller's moved-counter reflects reality — a null target, an
    /// already-in-place folder, or a populated target must not inflate the tally.
    /// </summary>
    private bool MoveItem(MediaItem item, string? target)
    {
        if (target is null) return false;

        Status.Item(item.OriginalName);

        // Same-path no-op — happens when scanning the wrapper of the canonical
        // location ("Heat (1995)" inside M:\Movies is already at M:\Movies\Heat (1995)).
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.FullPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
                StringComparison.OrdinalIgnoreCase))
        {
            Status.Line("  [already in place]", Theme.Dim);
            return false;
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            Status.Line($"  [skip - target exists with content: {target}]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"relocate target {target} already has content");
            return false;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {target}]", Theme.Active);
            return true;
        }

        MoveStage.SafeMoveDirectory(item.FullPath, target);
        Status.Line($"  -> {target}", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "relocate", item.FullPath, target, item.Kind);
        return true;
    }

    private static void LogInPlace(MediaItem item)
    {
        Status.Item(item.OriginalName);
        Status.Line("  [in place]", Theme.Dim);
    }

    private static void LogSkip(MediaItem item, string reason)
    {
        Status.Item(item.OriginalName);
        Status.Line("  [skip - " + reason + "]", Theme.Dim);
    }

    /// <summary>
    /// Decide what kind of folder we're inside by comparing the scan source
    /// against the configured destinations. Returns null when neither destination
    /// matches — in that case both Movie and TvSeason items get relocated.
    /// </summary>
    internal static MediaKind? InferExpectedKind(MediaButlerSettings s)
    {
        if (PathContains(s.MoviesDestination, s.SourcePath)) return MediaKind.Movie;
        if (PathContains(s.TvDestination,     s.SourcePath)) return MediaKind.TvSeason;
        return null;
    }

    /// <summary>True when <paramref name="haystack"/> equals or is an ancestor of <paramref name="needle"/>.</summary>
    internal static bool PathContains(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        string h, n;
        try
        {
            h = Path.TrimEndingDirectorySeparator(Path.GetFullPath(haystack));
            n = Path.TrimEndingDirectorySeparator(Path.GetFullPath(needle));
        }
        catch { return false; }
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(h, n, cmp)) return true;
        var sep = Path.DirectorySeparatorChar.ToString();
        return n.StartsWith(h + sep, cmp);
    }
}
