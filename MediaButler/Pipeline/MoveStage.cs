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

        Console.Write($"  {item.ShowName} S{item.SeasonNumber:D2}");

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
        var folderName = NameParser.FormatMovieFolder(item.MovieTitle ?? item.OriginalName, item.MovieYear);
        var target     = Path.Combine(settings.MoviesDestination, SanitizeForFs(folderName));

        Console.Write($"  {folderName}");

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
                try { File.Delete(file); }
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
            try { File.Delete(file); }
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
            // Copy finished — commit by removing the marker, then delete source.
            try { File.Delete(marker); } catch { /* tolerable; next scan can clean it up */ }
            Directory.Delete(source, recursive: true);
            return;
        }

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
            foreach (var marker in Directory.EnumerateFiles(root, CopyingMarker, SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(marker)!;
                report.RecordManual(dir, MediaKind.Unknown,
                    "partial cross-volume copy detected — a previous run crashed mid-move. Inspect and delete the marker once verified.");
            }
        }
        catch { /* enumeration failures are non-fatal */ }
    }

    /// <summary>True when the two paths resolve to different volume roots.</summary>
    internal static bool IsCrossVolume(string a, string b)
    {
        var rootA = Path.GetPathRoot(Path.GetFullPath(a));
        var rootB = Path.GetPathRoot(Path.GetFullPath(b));
        return !string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
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
