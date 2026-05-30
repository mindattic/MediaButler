using MediaButler.Media;
using MediaButler.Settings;
using MediaButler.Ui;

namespace MediaButler.Pipeline;

/// <summary>
/// Final stage: relocate everything from <see cref="MediaButlerSettings.SourcePath"/>
/// into Plex-shaped destinations:
///
/// <list type="bullet">
///   <item>TV: <c>{Show} - Season XX</c> at source root → <c>{TvDestination}\{Show}\Season XX\episode.ext</c>.
///         Show-level artwork (poster.jpg / banner.jpg / fanart.jpg / tvshow.nfo) is hoisted from the
///         first processed season into <c>{TvDestination}\{Show}\</c> so Plex finds it at the show root,
///         and the duplicates inside other season folders are deleted.</item>
///   <item>Movies: <c>{Title} ({Year})</c> at source root → <c>{MoviesDestination}\{Title} ({Year})\</c>
///         (entire folder moves as-is — poster/backdrop already live with the movie file).</item>
/// </list>
///
/// <para>Cross-drive moves are detected by comparing path roots and fall back to recursive
/// copy + source delete. Same-drive moves use the cheap <see cref="Directory.Move"/> rename.</para>
///
/// <para>When <see cref="MediaButlerSettings.DryRun"/> is true, no moves, deletes, or
/// directory creations happen — every action is logged as <c>[dry]</c> with the target path.</para>
/// </summary>
public sealed class MoveStage
{
    private readonly MediaButlerSettings settings;
    private readonly HashSet<string> showArt;
    private readonly PipelineReport report;

    public MoveStage(MediaButlerSettings settings, PipelineReport report)
    {
        this.settings = settings;
        this.report   = report;
        showArt       = new HashSet<string>(settings.ShowLevelArtFiles, StringComparer.OrdinalIgnoreCase);
    }

    public void Run()
    {
        if (settings.DryRun)
            Status.Print("DRY RUN — no files will be moved.", Theme.Active);

        if (!settings.DryRun)
        {
            EnsureDir(settings.TvDestination);
            EnsureDir(settings.MoviesDestination);
        }

        // Surface any partial copies left by a prior crashed run so the user
        // sees them in the manual list before this run adds new mutations.
        ReportOrphanCopyMarkers(settings.TvDestination,     report);
        ReportOrphanCopyMarkers(settings.MoviesDestination, report);

        var items = new MediaScanner(settings).Scan().ToList();
        // Track show roots we've already populated so duplicate artwork from
        // subsequent seasons gets pruned instead of overwriting good art.
        var showRootsSeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Stable order — process all seasons of a show together so the first
        // season seeds show-level art and subsequent ones dedupe against it.
        var ordered = items
            .OrderBy(i => i.Kind == MediaKind.Movie ? 1 : 0)
            .ThenBy(i => i.ShowName ?? i.MovieTitle ?? i.OriginalName)
            .ThenBy(i => i.SeasonNumber ?? 0);

        foreach (var item in ordered)
        {
            try
            {
                switch (item.Kind)
                {
                    case MediaKind.TvSeason:
                        MoveTvSeason(item, showRootsSeeded);
                        break;
                    case MediaKind.Movie:
                        MoveMovie(item);
                        break;
                    case MediaKind.Extras:
                    case MediaKind.Unknown:
                        // Already flagged by RenameStage as needing manual review.
                        break;
                    default:
                        // Empty (already deleted), MultiSeasonParent (should be hoisted by now).
                        break;
                }
            }
            catch (Exception ex)
            {
                Status.Print($"  ! {item.OriginalName}: {ex.Message}", Theme.Err);
                report.RecordError(item.FullPath, ex.Message);
            }
        }
    }

    private void MoveTvSeason(MediaItem item, HashSet<string> showRootsSeeded)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            Status.Print($"  skip {item.OriginalName} (no show/season)", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "move skipped — show/season missing");
            return;
        }

        var showRoot   = Path.Combine(settings.TvDestination, SanitizeForFs(item.ShowName));
        var seasonRoot = Path.Combine(showRoot, $"Season {item.SeasonNumber:D2}");

        Status.Item($"{item.ShowName} S{item.SeasonNumber:D2}");

        if (Directory.Exists(seasonRoot) && Directory.EnumerateFileSystemEntries(seasonRoot).Any())
        {
            Status.Line("  [skip - target exists with content]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {seasonRoot} already has content");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {seasonRoot}]", Theme.Active);
            report.TvMoved++;
            return;
        }

        EnsureDir(showRoot);

        // First, hoist show-level art up to {ShowRoot}. We do this BEFORE moving
        // anything else so the art files don't ride along into the season folder.
        if (!showRootsSeeded.Contains(showRoot))
        {
            HoistShowLevelArt(item.FullPath, showRoot);
            showRootsSeeded.Add(showRoot);
        }
        else
        {
            // Show root already has its art — delete duplicates in this season folder.
            DeleteShowLevelArt(item.FullPath);
        }

        SafeMoveDirectory(item.FullPath, seasonRoot);
        Status.Line($"  -> {seasonRoot}", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "move", item.FullPath, seasonRoot, item.Kind);
        report.TvMoved++;
    }

    private void MoveMovie(MediaItem item)
    {
        // A blank title would build a junk destination ("(2019)" or even ""), so
        // skip and surface for manual review — matching RenameStage.RenameMovie
        // and RelocateStage.BuildMovieTarget rather than moving into a bad folder.
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            Status.Item(item.OriginalName);
            Status.Line("  [skip - no title]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "move skipped — movie title missing");
            return;
        }

        var folderName = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        var target     = Path.Combine(settings.MoviesDestination, SanitizeForFs(folderName));

        Status.Item(folderName);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            Status.Line("  [skip - target exists with content]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {target} already has content");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {target}]", Theme.Active);
            report.MoviesMoved++;
            return;
        }

        SafeMoveDirectory(item.FullPath, target);
        Status.Line($"  -> {target}", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "move", item.FullPath, target, item.Kind);
        report.MoviesMoved++;
    }

    /// <summary>
    /// Move show-level artwork files from the season folder to the show root.
    /// Plex expects poster.jpg / fanart.jpg / tvshow.nfo at the show root, not
    /// in each season subfolder. Reports any move/delete failure so the user
    /// knows when art didn't land where they expected.
    /// </summary>
    private void HoistShowLevelArt(string seasonFolder, string showRoot)
    {
        foreach (var file in Directory.EnumerateFiles(seasonFolder))
        {
            var name = Path.GetFileName(file);
            if (!showArt.Contains(name)) continue;
            var dest = Path.Combine(showRoot, name);
            if (File.Exists(dest))
            {
                try
                {
                    File.Delete(file);
                    AuditLog.Record(settings, settings.DryRun, "delete-art", file, null, MediaKind.TvSeason);
                }
                catch (Exception ex) { report.RecordError(file, "art dedupe delete failed: " + ex.Message); }
                continue;
            }
            try
            {
                File.Move(file, dest);
                AuditLog.Record(settings, settings.DryRun, "move-art", file, dest, MediaKind.TvSeason);
            }
            catch (Exception ex)
            {
                // Leave the file behind — SafeMoveDirectory will carry it over.
                // Surface the failure so the user knows art didn't reach the show root.
                report.RecordError(file, "art hoist failed: " + ex.Message);
            }
        }
    }

    /// <summary>Delete duplicate show-level artwork from a season folder when the show root already has it.</summary>
    private void DeleteShowLevelArt(string seasonFolder)
    {
        foreach (var file in Directory.EnumerateFiles(seasonFolder))
        {
            var name = Path.GetFileName(file);
            if (!showArt.Contains(name)) continue;
            try
            {
                File.Delete(file);
                AuditLog.Record(settings, settings.DryRun, "delete-art", file, null, MediaKind.TvSeason);
            }
            catch (Exception ex) { report.RecordError(file, "art dedupe delete failed: " + ex.Message); }
        }
    }

    /// <summary>The marker dropped at the destination root while a cross-volume copy is in progress.</summary>
    internal const string CopyingMarker = ".mediabutler-copying";

    /// <summary>
    /// Move <paramref name="source"/> to <paramref name="destination"/>. Uses
    /// <see cref="Directory.Move"/> when both ends share a volume root (cheap
    /// rename); falls back to recursive copy + delete across volumes.
    ///
    /// <para>Cross-volume path drops a <see cref="CopyingMarker"/> file at the
    /// destination root before copying and removes it on success. If the
    /// process dies mid-copy, the marker tells the next run "this folder is a
    /// partial copy — investigate" instead of letting the half-populated
    /// directory masquerade as a successful move.</para>
    /// </summary>
    internal static void SafeMoveDirectory(string source, string destination)
    {
        EnsureDir(Path.GetDirectoryName(destination)!);

        if (IsCrossVolume(source, destination))
        {
            Directory.CreateDirectory(destination);
            var marker = Path.Combine(destination, CopyingMarker);
            File.WriteAllText(marker, $"source={source}{Environment.NewLine}started={DateTime.UtcNow:o}{Environment.NewLine}");
            try
            {
                CopyDirectoryRecursive(source, destination);
            }
            catch
            {
                // Marker stays in place so the partial copy can be detected later.
                throw;
            }
            // Copy finished. Order matters: delete source FIRST, then the marker.
            // If source-delete fails (locked file, AV scan), the marker stays so
            // ReportOrphanCopyMarkers can still flag this destination as a
            // partial state — otherwise we'd have duplicate content with no
            // breadcrumb to find it.
            Directory.Delete(source, recursive: true);
            try { File.Delete(marker); } catch { /* tolerable; next scan can clean it up */ }
            return;
        }

        // Same-volume rename. Directory.Move throws "Cannot create a file when
        // that file already exists" if the target directory already exists —
        // even when empty. Callers only reach here after confirming the target
        // has no content, so remove an empty leftover shell first. The delete is
        // non-recursive on purpose: if it unexpectedly holds content the throw
        // surfaces as a recorded error instead of silently merging directories.
        if (Directory.Exists(destination)) Directory.Delete(destination);
        Directory.Move(source, destination);
    }

    /// <summary>
    /// Walk <paramref name="root"/> and report any orphan
    /// <see cref="CopyingMarker"/> files left by a prior crashed run. Records
    /// each into <paramref name="report"/>'s manual list so the user sees them
    /// in the final summary.
    /// </summary>
    public static void ReportOrphanCopyMarkers(string root, PipelineReport report)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        try
        {
            // Markers are only ever dropped at a move destination root:
            // {MoviesDest}\{Movie}\ (depth 1) or {TvDest}\{Show}\Season XX\
            // (depth 2). Cap recursion so this doesn't crawl an entire Plex
            // library — potentially tens of thousands of folders — every run.
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 2,
                IgnoreInaccessible = true,
            };
            foreach (var marker in Directory.EnumerateFiles(root, CopyingMarker, opts))
            {
                var dir = Path.GetDirectoryName(marker)!;
                report.RecordManual(dir, MediaKind.Unknown,
                    "partial cross-volume copy detected — a previous run crashed mid-move. Inspect and delete the marker once verified.");
            }
        }
        catch { /* enumeration failures are non-fatal */ }
    }

    /// <summary>
    /// True when the two paths resolve to different volume roots. Junctions
    /// and directory symlinks are followed to their final target before
    /// comparing — without this, <c>D:\junction-to-M\Movies</c> and
    /// <c>M:\Movies</c> would be treated as cross-volume and trigger a slow
    /// copy+delete instead of the cheap <see cref="Directory.Move"/>.
    /// </summary>
    internal static bool IsCrossVolume(string a, string b)
    {
        var rootA = Path.GetPathRoot(ResolveFinalPath(a));
        var rootB = Path.GetPathRoot(ResolveFinalPath(b));
        return !string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve a path through any junctions or directory symlinks to its final
    /// on-disk target. Falls back to <see cref="Path.GetFullPath(string)"/> if
    /// the path doesn't exist or the resolve API isn't supported on this OS
    /// (e.g. legacy Windows). Walks up the tree if the leaf doesn't exist yet
    /// — useful for destinations the move is about to create.
    /// </summary>
    private static string ResolveFinalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var probe = full;
        while (!string.IsNullOrEmpty(probe))
        {
            try
            {
                if (Directory.Exists(probe))
                {
                    var info = new DirectoryInfo(probe);
                    var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                    var resolvedRoot = resolved?.FullName ?? probe;
                    // Re-attach any path tail we walked past so the root computation
                    // still works for non-existent leaves under a resolved parent.
                    var tail = full[probe.Length..];
                    return resolvedRoot + tail;
                }
            }
            catch { /* fall through to parent walk-up */ }
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }
        return full;
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destination, name), overwrite: false);
        }
        foreach (var sub in Directory.EnumerateDirectories(source))
        {
            // Never follow junctions / directory symlinks: they can point
            // outside the media tree (copying an unrelated subtree) or form a
            // cycle. A genuine season/movie folder is never a reparse point.
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
            var name = Path.GetFileName(sub);
            CopyDirectoryRecursive(sub, Path.Combine(destination, name));
        }
    }

    /// <summary>
    /// Remove characters that are illegal in Windows file/folder names. Show
    /// titles like "Star Wars: A New Hope" would otherwise fail Path.Combine.
    /// </summary>
    internal static string SanitizeForFs(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            clean.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);
        return System.Text.RegularExpressions.Regex.Replace(clean.ToString(), @"\s+", " ").Trim();
    }

    private static void EnsureDir(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
    }
}
