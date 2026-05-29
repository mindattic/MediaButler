using MediaButler.Media;
using MediaButler.Settings;
using MediaButler.Ui;

namespace MediaButler.Pipeline;

/// <summary>
/// Stage 1. Pure local-rename pass — gives FileBot a clean stem to work with.
/// Walks the source root, applies the right transform per <see cref="MediaKind"/>:
///
/// <list type="bullet">
///   <item><see cref="MediaKind.Empty"/>: delete (was a Breaking Bad-style empty shell).</item>
///   <item><see cref="MediaKind.MultiSeasonParent"/>: hoist each Season subfolder to the source root as
///         <c>{Show} - Season XX</c>, move show-level orphans (jpg/txt) into the first season folder,
///         then delete the (now-empty) parent.</item>
///   <item><see cref="MediaKind.TvSeason"/>: rename in place to <c>{Show} - Season XX</c>.</item>
///   <item><see cref="MediaKind.Movie"/>: rename in place to <c>{Title} (YYYY)</c>.</item>
///   <item><see cref="MediaKind.Extras"/>: leave in place, surface in the manual report.</item>
///   <item><see cref="MediaKind.Unknown"/>: leave in place, surface in the manual report.</item>
/// </list>
///
/// <para>When <see cref="MediaButlerSettings.DryRun"/> is true, no filesystem
/// mutations occur — every action prints as <c>[dry]</c> with the target name
/// that <em>would</em> have been written.</para>
/// </summary>
public sealed class RenameStage
{
    private readonly MediaButlerSettings settings;
    private readonly MediaScanner scanner;
    private readonly PipelineReport report;

    public RenameStage(MediaButlerSettings settings, PipelineReport report)
    {
        this.settings = settings;
        this.report   = report;
        scanner       = new MediaScanner(settings);
    }

    public void Run()
    {
        Status.Print("Source: " + settings.SourcePath, Theme.Normal);
        if (settings.DryRun)
            Status.Print("DRY RUN — no files will be renamed, moved, or deleted.", Theme.Active);
        Status.NewLine();

        // Snapshot first — we mutate the directory tree as we go.
        var items = scanner.Scan().ToList();
        foreach (var item in items)
        {
            try
            {
                ProcessItem(item);
            }
            catch (Exception ex)
            {
                Status.Print($"  ! {item.OriginalName}: {ex.Message}", Theme.Err);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    private void ProcessItem(MediaItem item)
    {
        Status.Item(item.OriginalName);

        switch (item.Kind)
        {
            case MediaKind.Empty:
                DeleteEmptySafely(item);
                break;

            case MediaKind.MultiSeasonParent:
                HoistParent(item);
                break;

            case MediaKind.TvSeason:
                RenameSeason(item);
                break;

            case MediaKind.Movie:
                RenameMovie(item);
                break;

            case MediaKind.Extras:
                Status.Line("  [extras - left in place]", Theme.Dim);
                report.RecordManual(item.FullPath, item.Kind, "extras/specials folder — Plex prefers these inside the show root");
                break;

            default:
                Status.Line("  [skip - unknown]", Theme.Dim);
                report.RecordManual(item.FullPath, item.Kind, "parser could not classify (try EnableLlmFallback)");
                break;
        }
    }

    private void RenameSeason(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            Status.Line("  [skip - missing show/season]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "parsed as TvSeason but show/season missing");
            return;
        }

        var newName = NameParser.FormatSeasonFolder(item.ShowName, item.SeasonNumber.Value);
        if (string.Equals(item.OriginalName, newName, StringComparison.Ordinal))
        {
            Status.Line("  [ok]", Theme.Dim);
            return;
        }

        var target = Path.Combine(settings.SourcePath, newName);
        if (Directory.Exists(target))
        {
            Status.Line($"  [skip - target exists: {newName}]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {newName} already exists");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {newName}]", Theme.Active);
        }
        else
        {
            Directory.Move(item.FullPath, target);
            Status.Line($"  -> {newName}", Theme.Ok);
        }
        AuditLog.Record(settings, settings.DryRun, "rename", item.FullPath, target, item.Kind);
        report.Renamed++;
    }

    private void RenameMovie(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            Status.Line("  [skip - no title]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "parsed as Movie but title missing");
            return;
        }

        var newName = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        if (string.Equals(item.OriginalName, newName, StringComparison.Ordinal))
        {
            Status.Line("  [movie - ok]", Theme.Dim);
            return;
        }

        var target = Path.Combine(settings.SourcePath, newName);
        if (Directory.Exists(target))
        {
            Status.Line($"  [skip - target exists: {newName}]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {newName} already exists");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {newName} (movie)]", Theme.Active);
        }
        else
        {
            Directory.Move(item.FullPath, target);
            Status.Line($"  -> {newName} (movie)", Theme.Ok);
        }
        AuditLog.Record(settings, settings.DryRun, "rename", item.FullPath, target, item.Kind);
        report.Renamed++;
    }

    private void HoistParent(MediaItem item)
    {
        var show = item.ShowName;
        if (string.IsNullOrWhiteSpace(show))
        {
            Status.Line("  [skip - could not parse show name]", Theme.Err);
            report.RecordManual(item.FullPath, item.Kind, "multi-season parent: could not parse show name");
            return;
        }

        Status.NewLine();

        var hoisted = new List<string>();
        foreach (var season in item.Seasons.OrderBy(s => s.SeasonNumber))
        {
            var newName = NameParser.FormatSeasonFolder(show!, season.SeasonNumber);
            var target  = Path.Combine(settings.SourcePath, newName);
            if (Directory.Exists(target))
            {
                Status.Line($"    [skip - exists: {newName}]", Theme.Dim);
                report.RecordManual(season.FullPath, MediaKind.TvSeason, $"hoist target {newName} already exists");
                continue;
            }
            if (settings.DryRun)
            {
                Status.Line($"    [dry: -> {newName}]", Theme.Active);
            }
            else
            {
                Directory.Move(season.FullPath, target);
                Status.Line($"    -> {newName}", Theme.Ok);
            }
            AuditLog.Record(settings, settings.DryRun, "hoist", season.FullPath, target, MediaKind.TvSeason);
            hoisted.Add(target);
            report.Hoisted++;
        }

        // No nested "Season N" subfolders to hoist — e.g. a flat dump where every
        // episode sits directly under the parent ("Breaking Bad S01-S05 1080p").
        // Without this the parent is left untouched and unreported; surface it so
        // the user knows the multi-season name didn't yield an organizable layout.
        if (hoisted.Count == 0)
        {
            Status.Line("  [skip - no season subfolders to hoist]", Theme.Active);
            report.RecordManual(item.FullPath, item.Kind,
                "multi-season name but no nested season subfolders found — episodes may be loose in the parent");
            return;
        }

        // Orphan show-level files at the parent (e.g. Bones_Large.jpg, Info.txt)
        // get tucked into the first new season folder so they aren't lost when
        // we delete the parent. Plex doesn't read them but they're cheap to keep.
        if (!settings.DryRun && item.OrphanFilesAtParent.Count > 0 && hoisted.Count > 0)
        {
            var firstSeason = hoisted[0];
            foreach (var file in item.OrphanFilesAtParent)
            {
                try
                {
                    var dest = Path.Combine(firstSeason, Path.GetFileName(file));
                    if (!File.Exists(dest)) File.Move(file, dest);
                }
                catch (Exception ex)
                {
                    report.RecordError(file, "orphan file move failed: " + ex.Message);
                }
            }
        }

        // Delete the parent if no video files remain. We do NOT delete a parent
        // that still has hidden video content the scanner missed (e.g. Extras).
        if (!settings.DryRun && !HasAnyVideoLeft(item.FullPath))
        {
            try { Directory.Delete(item.FullPath, recursive: true); }
            catch (Exception ex) { report.RecordError(item.FullPath, "parent shell delete failed: " + ex.Message); }
        }
    }

    /// <summary>
    /// Delete an Empty-classified folder, but only after a size sanity check:
    /// if it holds more than <see cref="MediaButlerSettings.EmptyDeleteSafetyBytes"/>
    /// the folder is almost certainly real media in an unrecognised container
    /// — refuse to delete and surface to the manual list so the user can
    /// extend <see cref="MediaButlerSettings.VideoExtensions"/>.
    /// </summary>
    private void DeleteEmptySafely(MediaItem item)
    {
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { /* ignore unreadable file */ }
                if (size > settings.EmptyDeleteSafetyBytes) break; // short-circuit; we already know we're over
            }
        }
        catch (Exception ex)
        {
            Status.Line($"  [skip - could not measure: {ex.Message}]", Theme.Err);
            report.RecordError(item.FullPath, "measure failed: " + ex.Message);
            return;
        }

        if (size > settings.EmptyDeleteSafetyBytes)
        {
            var mb = size / (1024.0 * 1024.0);
            Status.Line($"  [refuse - {mb:F1} MB of non-video content; extend VideoExtensions?]",
                Theme.Active);
            report.RecordManual(item.FullPath, item.Kind,
                $"marked Empty but holds {mb:F1} MB — likely an unrecognised video container");
            return;
        }

        if (!settings.DryRun) Directory.Delete(item.FullPath, recursive: true);
        Status.Line(settings.DryRun ? "  [dry: would delete empty]" : "  [empty - deleted]",
            Theme.Dim);
        AuditLog.Record(settings, settings.DryRun, "delete-empty", item.FullPath, null, item.Kind);
        report.EmptyDeleted++;
    }

    private bool HasAnyVideoLeft(string path)
    {
        if (!Directory.Exists(path)) return false;
        var exts = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            if (exts.Contains(Path.GetExtension(f))) return true;
        }
        return false;
    }
}
