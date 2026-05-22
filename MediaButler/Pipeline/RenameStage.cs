using MediaButler.Media;
using MediaButler.Menu;
using MediaButler.Settings;

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
        ConsoleMenu.Status("Source: " + settings.SourcePath, ConsoleMenu.Normal);
        if (settings.DryRun)
            ConsoleMenu.Status("DRY RUN — no files will be renamed, moved, or deleted.", ConsoleMenu.Active);
        Console.WriteLine();

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
                ConsoleMenu.Status($"  ! {item.OriginalName}: {ex.Message}", ConsoleMenu.Err);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    private void ProcessItem(MediaItem item)
    {
        Console.Write("  " + item.OriginalName);

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
                ConsoleMenu.WriteColor("  [extras - left in place]", ConsoleMenu.Dim, newline: true);
                report.RecordManual(item.FullPath, item.Kind, "extras/specials folder — Plex prefers these inside the show root");
                break;

            default:
                ConsoleMenu.WriteColor("  [skip - unknown]", ConsoleMenu.Dim, newline: true);
                report.RecordManual(item.FullPath, item.Kind, "parser could not classify (try EnableLlmFallback)");
                break;
        }
    }

    private void RenameSeason(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            ConsoleMenu.WriteColor("  [skip - missing show/season]", ConsoleMenu.Dim, newline: true);
            report.RecordManual(item.FullPath, item.Kind, "parsed as TvSeason but show/season missing");
            return;
        }

        var newName = NameParser.FormatSeasonFolder(item.ShowName, item.SeasonNumber.Value);
        if (string.Equals(item.OriginalName, newName, StringComparison.Ordinal))
        {
            ConsoleMenu.WriteColor("  [ok]", ConsoleMenu.Dim, newline: true);
            return;
        }

        var target = Path.Combine(settings.SourcePath, newName);
        if (Directory.Exists(target))
        {
            ConsoleMenu.WriteColor($"  [skip - target exists: {newName}]", ConsoleMenu.Dim, newline: true);
            report.RecordManual(item.FullPath, item.Kind, $"target {newName} already exists");
            return;
        }

        if (settings.DryRun)
        {
            ConsoleMenu.WriteColor($"  [dry: -> {newName}]", ConsoleMenu.Active, newline: true);
        }
        else
        {
            Directory.Move(item.FullPath, target);
            ConsoleMenu.WriteColor($"  -> {newName}", ConsoleMenu.Ok, newline: true);
        }
        AuditLog.Record(settings, settings.DryRun, "rename", item.FullPath, target, item.Kind);
        report.Renamed++;
    }

    private void RenameMovie(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            ConsoleMenu.WriteColor("  [skip - no title]", ConsoleMenu.Dim, newline: true);
            report.RecordManual(item.FullPath, item.Kind, "parsed as Movie but title missing");
            return;
        }

        var newName = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        if (string.Equals(item.OriginalName, newName, StringComparison.Ordinal))
        {
            ConsoleMenu.WriteColor("  [movie - ok]", ConsoleMenu.Dim, newline: true);
            return;
        }

        var target = Path.Combine(settings.SourcePath, newName);
        if (Directory.Exists(target))
        {
            ConsoleMenu.WriteColor($"  [skip - target exists: {newName}]", ConsoleMenu.Dim, newline: true);
            report.RecordManual(item.FullPath, item.Kind, $"target {newName} already exists");
            return;
        }

        if (settings.DryRun)
        {
            ConsoleMenu.WriteColor($"  [dry: -> {newName} (movie)]", ConsoleMenu.Active, newline: true);
        }
        else
        {
            Directory.Move(item.FullPath, target);
            ConsoleMenu.WriteColor($"  -> {newName} (movie)", ConsoleMenu.Ok, newline: true);
        }
        AuditLog.Record(settings, settings.DryRun, "rename", item.FullPath, target, item.Kind);
        report.Renamed++;
    }

    private void HoistParent(MediaItem item)
    {
        var show = item.ShowName;
        if (string.IsNullOrWhiteSpace(show))
        {
            ConsoleMenu.WriteColor("  [skip - could not parse show name]", ConsoleMenu.Err, newline: true);
            report.RecordManual(item.FullPath, item.Kind, "multi-season parent: could not parse show name");
            return;
        }

        Console.WriteLine();

        var hoisted = new List<string>();
        foreach (var season in item.Seasons.OrderBy(s => s.SeasonNumber))
        {
            var newName = NameParser.FormatSeasonFolder(show!, season.SeasonNumber);
            var target  = Path.Combine(settings.SourcePath, newName);
            if (Directory.Exists(target))
            {
                ConsoleMenu.WriteColor($"    [skip - exists: {newName}]", ConsoleMenu.Dim, newline: true);
                report.RecordManual(season.FullPath, MediaKind.TvSeason, $"hoist target {newName} already exists");
                continue;
            }
            if (settings.DryRun)
            {
                ConsoleMenu.WriteColor($"    [dry: -> {newName}]", ConsoleMenu.Active, newline: true);
            }
            else
            {
                Directory.Move(season.FullPath, target);
                ConsoleMenu.WriteColor($"    -> {newName}", ConsoleMenu.Ok, newline: true);
            }
            AuditLog.Record(settings, settings.DryRun, "hoist", season.FullPath, target, MediaKind.TvSeason);
            hoisted.Add(target);
            report.Hoisted++;
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
                catch { /* best effort */ }
            }
        }

        // Delete the parent if no video files remain. We do NOT delete a parent
        // that still has hidden video content the scanner missed (e.g. Extras).
        if (!settings.DryRun && !HasAnyVideoLeft(item.FullPath))
        {
            try { Directory.Delete(item.FullPath, recursive: true); } catch { /* best effort */ }
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
            ConsoleMenu.WriteColor($"  [skip - could not measure: {ex.Message}]", ConsoleMenu.Err, newline: true);
            report.RecordError(item.FullPath, "measure failed: " + ex.Message);
            return;
        }

        if (size > settings.EmptyDeleteSafetyBytes)
        {
            var mb = size / (1024.0 * 1024.0);
            ConsoleMenu.WriteColor($"  [refuse - {mb:F1} MB of non-video content; extend VideoExtensions?]",
                ConsoleMenu.Active, newline: true);
            report.RecordManual(item.FullPath, item.Kind,
                $"marked Empty but holds {mb:F1} MB — likely an unrecognised video container");
            return;
        }

        if (!settings.DryRun) Directory.Delete(item.FullPath, recursive: true);
        ConsoleMenu.WriteColor(settings.DryRun ? "  [dry: would delete empty]" : "  [empty - deleted]",
            ConsoleMenu.Dim, newline: true);
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
