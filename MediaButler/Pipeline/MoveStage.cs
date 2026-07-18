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
        IEnumerable<MediaItem> ordered = items
            .OrderBy(i => i.Kind == MediaKind.Movie ? 1 : 0)
            .ThenBy(i => i.ShowName ?? i.MovieTitle ?? i.OriginalName)
            .ThenBy(i => i.SeasonNumber ?? 0);
        if (settings.Limit.HasValue) ordered = ordered.Take(settings.Limit.Value);

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
                    case MediaKind.Music:
                        MoveMusic(item);
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

        var sanitizedShow = SanitizeForFs(item.ShowName);

        // Use a year-tagged destination ONLY when the user has already set up disambiguation
        // for this show name — i.e., at least one "ShowName (YYYY)" folder exists in the TV
        // destination. This handles reboots (the user renames the existing bare folder to add
        // its year, which activates year-tagged routing for all future content with that name).
        string showRoot;
        if (item.TvYear.HasValue && IsShowDisambiguated(settings.TvDestination, sanitizedShow))
        {
            showRoot = Path.Combine(settings.TvDestination, $"{sanitizedShow} ({item.TvYear})");

            // Warn if the bare folder still exists alongside year-tagged content — it should
            // have been renamed when the user set up disambiguation.
            var bareShowRoot = Path.Combine(settings.TvDestination, sanitizedShow);
            if (Directory.Exists(bareShowRoot))
            {
                Status.Print($"  ! '{bareShowRoot}' exists — rename to '{item.ShowName} (YEAR)' to avoid mixing series with '{Path.GetFileName(showRoot)}'", Theme.Active);
                report.RecordManual(bareShowRoot, item.Kind,
                    $"bare show folder exists alongside year-tagged '{Path.GetFileName(showRoot)}' — rename to '{item.ShowName} (YEAR)'");
            }
        }
        else
        {
            showRoot = Path.Combine(settings.TvDestination, sanitizedShow);
        }

        var seasonRoot = Path.Combine(showRoot, $"Season {item.SeasonNumber:D2}");

        Status.Item($"{item.ShowName} S{item.SeasonNumber:D2}");

        if (Directory.Exists(seasonRoot) && Directory.EnumerateFileSystemEntries(seasonRoot).Any())
        {
            // The destination already has this season (an earlier inbox run or
            // a partial library) — merge file-by-file. Files whose name or
            // parsed episode already exists at the destination stay behind and
            // surface as a human decision; everything else moves.
            var res = SeasonMerger.MergeFiles(item.FullPath, seasonRoot, item.SeasonNumber.Value, settings, report);
            report.MergedFiles += res.Moved;
            Status.Line(settings.DryRun
                ? $"  [dry: merge {res.Moved} file(s) -> {seasonRoot}]"
                : $"  [merged {res.Moved} file(s) -> {seasonRoot}]",
                settings.DryRun ? Theme.Active : Theme.Ok);
            AuditLog.Record(settings, settings.DryRun, "merge-season", item.FullPath, seasonRoot, item.Kind);

            if (res.FullyMerged && !settings.DryRun)
            {
                if (SeasonMerger.TryDeleteShell(item.FullPath, settings, out var reason))
                    report.TvMoved++;
                else
                    report.RecordManual(item.FullPath, item.Kind, $"merged into {seasonRoot} but shell kept — {reason}");
            }
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

        // Loose movie FILES are wrapped into a folder by the Rename stage; a
        // bare `move` run that never renamed can still see one here. Folder
        // moves on a file path would throw, so surface it instead.
        if (item.IsFile)
        {
            Status.Item(item.OriginalName);
            Status.Line("  [skip - loose file; run rename first]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "loose movie file — run the rename stage to wrap it into a folder first");
            return;
        }

        var folderName = NameParser.FormatMovieFolder(item.MovieTitle, item.MovieYear);
        var target     = Path.Combine(settings.MoviesDestination, SanitizeForFs(folderName));

        Status.Item(folderName);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            ResolveDuplicateMovie(item, target);
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
    /// A movie's canonical destination already exists with content. Under
    /// <see cref="DuplicateMovieAction.KeepLargest"/> the copy with the larger
    /// primary video wins: incoming larger → the destination's video files are
    /// deleted and the incoming folder merges in (existing artwork is kept);
    /// incoming smaller or equal → the incoming folder is deleted. Both paths
    /// audit-log the loser. With no video on either side to compare — or under
    /// <see cref="DuplicateMovieAction.Flag"/> — nothing is touched and the
    /// item surfaces as needs-manual (classic MB-LAW-9).
    /// </summary>
    private void ResolveDuplicateMovie(MediaItem item, string target)
    {
        var incoming = PrimaryVideo(item.FullPath);
        var existing = PrimaryVideo(target);

        if (settings.DuplicateMovieAction == DuplicateMovieAction.Flag ||
            incoming is null || existing is null)
        {
            Status.Line("  [skip - target exists with content]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {target} already has content");
            return;
        }

        if (incoming.Length > existing.Length)
        {
            if (settings.DryRun)
            {
                Status.Line($"  [dry: duplicate — incoming {Gb(incoming.Length)} replaces {Gb(existing.Length)} at {target}]", Theme.Active);
                report.MoviesMoved++;
                return;
            }

            // Incoming wins: clear the destination's video files, merge the
            // incoming folder in. Non-video collisions (artwork the FileBot
            // pass already fetched at the destination) keep the existing copy.
            var videoExts = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(target).Where(f => videoExts.Contains(Path.GetExtension(f))).ToList())
                File.Delete(f);
            foreach (var f in Directory.EnumerateFiles(item.FullPath))
            {
                var dest = Path.Combine(target, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Move(f, dest);
            }
            if (!Directory.EnumerateFileSystemEntries(item.FullPath).Any())
                Directory.Delete(item.FullPath);
            else
                report.RecordManual(item.FullPath, item.Kind, $"replaced video at {target}; leftover non-video files kept for review");

            Status.Line($"  [duplicate — incoming {Gb(incoming.Length)} replaced {Gb(existing.Length)}] -> {target}", Theme.Ok);
            AuditLog.Record(settings, settings.DryRun, "duplicate-replace", item.FullPath, target, item.Kind);
            report.MoviesMoved++;
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: duplicate — existing {Gb(existing.Length)} kept; incoming {Gb(incoming.Length)} would be deleted]", Theme.Active);
            return;
        }

        Directory.Delete(item.FullPath, recursive: true);
        Status.Line($"  [duplicate — existing {Gb(existing.Length)} kept; incoming {Gb(incoming.Length)} deleted]", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "duplicate-discard", item.FullPath, target, item.Kind);
    }

    /// <summary>
    /// Largest non-sample video file directly inside <paramref name="folder"/>,
    /// or null when there is none. The largest file is "the movie"; smaller
    /// videos (samples, extras) don't decide a duplicate contest.
    /// </summary>
    private FileInfo? PrimaryVideo(string folder)
    {
        var videoExts = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => videoExts.Contains(Path.GetExtension(f)))
                .Where(f => !NameParser.IsSampleName(Path.GetFileName(f)))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static string Gb(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):F1} GB";

    /// <summary>
    /// Move a music folder AS-IS to <see cref="MediaButlerSettings.MusicDestination"/>.
    /// MediaButler never renames or restructures music content — tagging is a
    /// different tool's job. With no destination configured the folder is left
    /// in place and flagged.
    /// </summary>
    private void MoveMusic(MediaItem item)
    {
        Status.Item(item.OriginalName);

        if (string.IsNullOrWhiteSpace(settings.MusicDestination))
        {
            Status.Line("  [music - no MusicDestination configured; left in place]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "music — set MusicDestination (or --music-dest) to move it");
            return;
        }

        if (item.IsFile)
        {
            Status.Line("  [skip - loose music file; move the folder instead]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, "loose music file — move it into a folder first");
            return;
        }

        var target = Path.Combine(settings.MusicDestination, SanitizeForFs(item.OriginalName));
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            Status.Line("  [skip - target exists with content]", Theme.Dim);
            report.RecordManual(item.FullPath, item.Kind, $"target {target} already has content");
            return;
        }

        if (settings.DryRun)
        {
            Status.Line($"  [dry: -> {target}]", Theme.Active);
            report.MusicMoved++;
            return;
        }

        EnsureDir(settings.MusicDestination);
        SafeMoveDirectory(item.FullPath, target);
        Status.Line($"  -> {target}", Theme.Ok);
        AuditLog.Record(settings, settings.DryRun, "move", item.FullPath, target, item.Kind);
        report.MusicMoved++;
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

    /// <summary>
    /// True when the TV destination already contains at least one year-tagged folder
    /// for <paramref name="sanitizedShowName"/> (e.g. "Little House on the Prairie (1974)").
    /// This signals that the user has set up year-disambiguation for this show name
    /// (by renaming an existing bare folder to include its year), so future content
    /// with the same name should also land in year-tagged folders.
    /// </summary>
    private static bool IsShowDisambiguated(string tvDestination, string sanitizedShowName)
    {
        if (!Directory.Exists(tvDestination)) return false;
        try
        {
            return Directory
                .EnumerateDirectories(tvDestination, $"{sanitizedShowName} (*)", SearchOption.TopDirectoryOnly)
                .Any(d => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(d), @"^.+\s+\((19|20)\d{2}\)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        catch { return false; }
    }

    private static void EnsureDir(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
    }
}
