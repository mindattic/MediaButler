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

            case MediaKind.TvEpisode:
                ConsolidateEpisode(item);
                break;

            case MediaKind.MoviePack:
                SplitMoviePack(item);
                break;

            case MediaKind.Movie when item.IsFile:
                WrapLooseMovie(item);
                break;

            case MediaKind.Movie:
                RenameMovie(item);
                break;

            case MediaKind.Music:
                // Music is never renamed/restructured. With a MusicDestination
                // configured the Move stage relocates it as-is; without one it
                // needs a human, so flag it.
                if (string.IsNullOrWhiteSpace(settings.MusicDestination))
                {
                    Status.Line("  [music - left in place]", Theme.Dim);
                    report.RecordManual(item.FullPath, item.Kind, "music — set MusicDestination (or --music-dest) to move it");
                }
                else
                {
                    Status.Line("  [music - will move as-is]", Theme.Dim);
                }
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
            // The same season already exists in canonical form — usually a
            // second dump of the same show (different rip group, complementary
            // episode subsets). Merge file-by-file; duplicate episodes stay
            // behind and surface as a human decision.
            var res = SeasonMerger.MergeFiles(item.FullPath, target, item.SeasonNumber.Value, settings, report);
            report.MergedFiles += res.Moved;
            Status.Line(settings.DryRun
                ? $"  [dry: merge {res.Moved} file(s) -> {newName}]"
                : $"  [merged {res.Moved} file(s) -> {newName}]",
                settings.DryRun ? Theme.Active : Theme.Ok);
            AuditLog.Record(settings, settings.DryRun, "merge-season", item.FullPath, target, item.Kind);

            if (res.FullyMerged)
            {
                if (!SeasonMerger.TryDeleteShell(item.FullPath, settings, out var reason))
                    report.RecordManual(item.FullPath, item.Kind, $"merged into {newName} but shell kept — {reason}");
            }
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

    /// <summary>
    /// Consolidate a single-episode dump (per-episode torrent folder or loose
    /// episode file at the source root) into the canonical
    /// <c>{Show} - Season XX</c> folder, then delete the emptied shell.
    /// </summary>
    private void ConsolidateEpisode(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            Status.Line("  [skip - missing show/season]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "parsed as TvEpisode but show/season missing");
            return;
        }

        var seasonName = NameParser.FormatSeasonFolder(item.ShowName, item.SeasonNumber.Value);
        var target = Path.Combine(settings.SourcePath, seasonName);

        var mediaFiles = item.IsFile
            ? new List<string> { item.FullPath }
            : CollectEpisodeMedia(item.FullPath);

        if (mediaFiles.Count == 0)
        {
            Status.Line("  [skip - no media files found]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "episode dump holds no non-sample media");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: {mediaFiles.Count} file(s) -> {seasonName}]", Theme.Active);
            AuditLog.Record(settings, settings.DryRun, "consolidate", item.FullPath, target, item.Kind);
            report.Consolidated++;
            return;
        }

        Directory.CreateDirectory(target);
        var conflicts = 0;
        foreach (var file in mediaFiles)
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(dest)) { conflicts++; continue; }
            File.Move(file, dest);
        }
        AuditLog.Record(settings, settings.DryRun, "consolidate", item.FullPath, target, item.Kind);

        if (conflicts > 0)
        {
            Status.Line($"  [{conflicts} file(s) already at {seasonName} - left in place]", Theme.Active);
            report.RecordManual(item.FullPath, item.Kind,
                $"{conflicts} file(s) duplicate content already in {seasonName} — pick the copy to keep");
            return;
        }

        Status.Line($"  -> {seasonName}", Theme.Ok);
        report.Consolidated++;

        if (!item.IsFile && !SeasonMerger.TryDeleteShell(item.FullPath, settings, out var reason))
            report.RecordManual(item.FullPath, item.Kind, $"consolidated into {seasonName} but shell kept — {reason}");
    }

    /// <summary>
    /// Media worth carrying out of a per-episode torrent folder: every
    /// non-sample video anywhere underneath (the episode itself), plus
    /// top-level subtitle sidecars. Promo junk (nfo/txt/Sample) stays behind
    /// for the shell delete.
    /// </summary>
    private List<string> CollectEpisodeMedia(string folder)
    {
        var videoExts = new HashSet<string>(settings.VideoExtensions,    StringComparer.OrdinalIgnoreCase);
        var subExts   = new HashSet<string>(settings.SubtitleExtensions, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f);
            if (videoExts.Contains(ext))
            {
                if (!NameParser.IsSampleName(Path.GetFileName(f))) result.Add(f);
            }
            else if (subExts.Contains(ext) &&
                     string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(f);
            }
        }
        return result;
    }

    /// <summary>
    /// Split a multi-movie pack ("The Matrix 1-4 Pack ...") into one canonical
    /// <c>{Title} (YYYY)</c> folder per film, then delete the emptied shell.
    /// </summary>
    private void SplitMoviePack(MediaItem item)
    {
        Status.NewLine();
        var split = 0;
        foreach (var child in item.PackMovies)
        {
            var folderName = NameParser.FormatMovieFolder(child.Title, child.Year);
            var target = Path.Combine(settings.SourcePath, folderName);
            var dest = Path.Combine(target, Path.GetFileName(child.FilePath));

            if (settings.DryRun)
            {
                Status.Line($"    [dry: -> {folderName}]", Theme.Active);
                split++;
                continue;
            }

            Directory.CreateDirectory(target);
            if (File.Exists(dest))
            {
                Status.Line($"    [skip - exists: {folderName}]", Theme.Dim);
                report.RecordManual(child.FilePath, MediaKind.Movie, $"pack split target {folderName} already has this file");
                continue;
            }
            File.Move(child.FilePath, dest);
            Status.Line($"    -> {folderName}", Theme.Ok);
            AuditLog.Record(settings, settings.DryRun, "pack-split", child.FilePath, dest, MediaKind.Movie);
            split++;
        }
        report.PackSplit += split;

        if (!settings.DryRun && split == item.PackMovies.Count &&
            !SeasonMerger.TryDeleteShell(item.FullPath, settings, out var reason))
        {
            report.RecordManual(item.FullPath, item.Kind, $"pack split but shell kept — {reason}");
        }
    }

    /// <summary>
    /// Wrap a loose movie file at the source root ("Frankenstein 2025 ... .mkv")
    /// into its canonical <c>{Title} (YYYY)</c> folder so FileBot and the Move
    /// stage treat it like any other movie.
    /// </summary>
    private void WrapLooseMovie(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            Status.Line("  [skip - no title]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "loose movie file but title missing");
            return;
        }

        var folderName = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        var target = Path.Combine(settings.SourcePath, folderName);
        var dest = Path.Combine(target, item.OriginalName);

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {folderName}\\]", Theme.Active);
            AuditLog.Record(settings, settings.DryRun, "wrap", item.FullPath, dest, item.Kind);
            report.Renamed++;
            return;
        }

        Directory.CreateDirectory(target);
        if (File.Exists(dest))
        {
            Status.Line($"  [skip - target file exists in {folderName}]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {folderName} already holds {item.OriginalName}");
            return;
        }
        File.Move(item.FullPath, dest);
        Status.Line($"  -> {folderName}\\", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "wrap", item.FullPath, dest, item.Kind);
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

        // Flat episode files at the parent whose names carry season+episode
        // ("S01 - E01 - Nice Face.mkv" in a Complete Collection dump): file
        // each season's episodes into its own "{Show} - Season XX" folder.
        var filed = 0;
        foreach (var grp in item.LooseEpisodes.GroupBy(e => e.SeasonNumber).OrderBy(g => g.Key))
        {
            var newName = NameParser.FormatSeasonFolder(show!, grp.Key);
            var target  = Path.Combine(settings.SourcePath, newName);

            if (settings.DryRun)
            {
                Status.Line($"    [dry: {grp.Count()} episode(s) -> {newName}]", Theme.Active);
                filed += grp.Count();
                report.Consolidated += grp.Count();
                continue;
            }

            Directory.CreateDirectory(target);
            foreach (var ep in grp)
            {
                var dest = Path.Combine(target, Path.GetFileName(ep.FilePath));
                if (File.Exists(dest))
                {
                    report.RecordManual(ep.FilePath, MediaKind.TvSeason,
                        $"flat episode duplicates a file already in {newName} — pick the copy to keep");
                    continue;
                }
                File.Move(ep.FilePath, dest);
                AuditLog.Record(settings, settings.DryRun, "consolidate", ep.FilePath, dest, MediaKind.TvSeason);
                filed++;
                report.Consolidated++;
            }
            Status.Line($"    {grp.Count()} episode(s) -> {newName}", Theme.Ok);
            if (!hoisted.Contains(target)) hoisted.Add(target);
        }

        // No nested "Season N" subfolders to hoist and nothing filed — e.g. a
        // flat dump whose loose files carry no parseable episode markers.
        // Without this the parent is left untouched and unreported; surface it so
        // the user knows the multi-season name didn't yield an organizable layout.
        if (hoisted.Count == 0 && filed == 0)
        {
            Status.Line("  [skip - no season subfolders to hoist]", Theme.Active);
            report.RecordManual(item.FullPath, item.Kind,
                "multi-season name but no nested season subfolders found — episodes may be loose in the parent");
            return;
        }

        // Orphan show-level files at the parent (e.g. Bones_Large.jpg, Info.txt)
        // get tucked into the first new season folder so they aren't lost when
        // we delete the parent. Plex doesn't read them but they're cheap to keep.
        // Loose VIDEO files are deliberately excluded: tucking an episode into
        // "Season 01" would misfile it under the wrong season — leave it at the
        // parent so the not-empty guard below keeps the folder and flags it.
        var videoExts = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        if (!settings.DryRun && item.OrphanFilesAtParent.Count > 0 && hoisted.Count > 0)
        {
            var firstSeason = hoisted[0];
            foreach (var file in item.OrphanFilesAtParent)
            {
                if (videoExts.Contains(Path.GetExtension(file))) continue;
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

        // Loose episode videos sitting directly under the parent can't be safely
        // auto-filed into a season — surface them so the user sorts them by hand
        // rather than leaving the parent silently behind.
        if (!settings.DryRun && HasAnyVideoLeft(item.FullPath))
        {
            report.RecordManual(item.FullPath, item.Kind,
                "loose video files remain at the multi-season parent root — sort into the correct season manually");
            return;
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
