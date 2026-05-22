using Tagsmith.Media;
using Tagsmith.Menu;
using Tagsmith.Settings;

namespace Tagsmith.Pipeline;

/// <summary>
/// Final stage: relocate everything from <see cref="TagsmithSettings.SourcePath"/>
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
/// Cross-drive scenarios use copy-then-delete via <see cref="Directory.Move"/>; if that fails because
/// the destination is on a different volume, Tagsmith falls back to recursive file copy + delete.
/// </summary>
public sealed class MoveStage
{
    private readonly TagsmithSettings settings;
    private readonly HashSet<string> showArt;

    public MoveStage(TagsmithSettings settings)
    {
        this.settings = settings;
        showArt = new HashSet<string>(settings.ShowLevelArtFiles, StringComparer.OrdinalIgnoreCase);
    }

    public void Run()
    {
        EnsureDir(settings.TvDestination);
        EnsureDir(settings.MoviesDestination);

        var items = new MediaScanner(settings).Scan().ToList();
        var counts = new Counts();
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
                        MoveTvSeason(item, showRootsSeeded, counts);
                        break;
                    case MediaKind.Movie:
                        MoveMovie(item, counts);
                        break;
                    default:
                        // Skip Unknown, Empty (already deleted), MultiSeasonParent
                        // (RenameStage should have hoisted these out before we got here).
                        break;
                }
            }
            catch (Exception ex)
            {
                ConsoleMenu.Status($"  ! {item.OriginalName}: {ex.Message}", ConsoleMenu.Err);
                counts.Errors++;
            }
        }

        Console.WriteLine();
        ConsoleMenu.Status(
            $"TV seasons: {counts.TvSeasons}  Movies: {counts.Movies}  " +
            $"Show roots seeded: {counts.ShowsSeeded}  Errors: {counts.Errors}",
            ConsoleMenu.Ok);
    }

    private void MoveTvSeason(MediaItem item, HashSet<string> showRootsSeeded, Counts counts)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            ConsoleMenu.Status($"  skip {item.OriginalName} (no show/season)", ConsoleMenu.Dim);
            return;
        }

        var showRoot = Path.Combine(settings.TvDestination, SanitizeForFs(item.ShowName));
        var seasonRoot = Path.Combine(showRoot, $"Season {item.SeasonNumber:D2}");
        EnsureDir(showRoot);

        Console.Write($"  {item.ShowName} S{item.SeasonNumber:D2}");

        if (Directory.Exists(seasonRoot) && Directory.EnumerateFileSystemEntries(seasonRoot).Any())
        {
            ConsoleMenu.WriteColor("  [skip - target exists with content]", ConsoleMenu.Dim, newline: true);
            return;
        }

        // First, hoist show-level art up to {ShowRoot}. We do this BEFORE moving
        // anything else so the art files don't ride along into the season folder.
        if (!showRootsSeeded.Contains(showRoot))
        {
            HoistShowLevelArt(item.FullPath, showRoot);
            showRootsSeeded.Add(showRoot);
            counts.ShowsSeeded++;
        }
        else
        {
            // Show root already has its art — delete duplicates in this season folder.
            DeleteShowLevelArt(item.FullPath);
        }

        SafeMoveDirectory(item.FullPath, seasonRoot);
        ConsoleMenu.WriteColor($"  -> {seasonRoot}", ConsoleMenu.Ok, newline: true);
        counts.TvSeasons++;
    }

    private void MoveMovie(MediaItem item, Counts counts)
    {
        var folderName = NameParser.FormatMovieFolder(item.MovieTitle ?? item.OriginalName, item.MovieYear);
        var target = Path.Combine(settings.MoviesDestination, SanitizeForFs(folderName));

        Console.Write($"  {folderName}");

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            ConsoleMenu.WriteColor("  [skip - target exists with content]", ConsoleMenu.Dim, newline: true);
            return;
        }

        SafeMoveDirectory(item.FullPath, target);
        ConsoleMenu.WriteColor($"  -> {target}", ConsoleMenu.Ok, newline: true);
        counts.Movies++;
    }

    /// <summary>
    /// Move show-level artwork files from the season folder to the show root.
    /// Plex expects poster.jpg / fanart.jpg / tvshow.nfo at the show root, not
    /// in each season subfolder.
    /// </summary>
    private void HoistShowLevelArt(string seasonFolder, string showRoot)
    {
        foreach (var file in Directory.EnumerateFiles(seasonFolder))
        {
            var name = Path.GetFileName(file);
            if (!showArt.Contains(name)) continue;
            var dest = Path.Combine(showRoot, name);
            if (File.Exists(dest)) { try { File.Delete(file); } catch { } continue; }
            try { File.Move(file, dest); } catch { /* leave it; SafeMoveDirectory will carry it over */ }
        }
    }

    /// <summary>Delete duplicate show-level artwork from a season folder when the show root already has it.</summary>
    private void DeleteShowLevelArt(string seasonFolder)
    {
        foreach (var file in Directory.EnumerateFiles(seasonFolder))
        {
            var name = Path.GetFileName(file);
            if (showArt.Contains(name))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// <see cref="Directory.Move"/> fails across volumes with IOException. Fall back to
    /// recursive copy + source delete in that case. Both legs preserve subfolder structure.
    /// </summary>
    private static void SafeMoveDirectory(string source, string destination)
    {
        EnsureDir(Path.GetDirectoryName(destination)!);
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException)
        {
            // Cross-volume — copy then delete.
        }

        CopyDirectoryRecursive(source, destination);
        Directory.Delete(source, recursive: true);
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
    private static string SanitizeForFs(string name)
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

    private sealed class Counts
    {
        public int TvSeasons, Movies, ShowsSeeded, Errors;
    }
}
