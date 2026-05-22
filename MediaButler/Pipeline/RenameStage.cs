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
///   <item><see cref="MediaKind.Unknown"/>: leave alone, log skip.</item>
/// </list>
/// </summary>
public sealed class RenameStage
{
    private readonly MediaButlerSettings settings;
    private readonly MediaScanner scanner;

    public RenameStage(MediaButlerSettings settings)
    {
        this.settings = settings;
        scanner = new MediaScanner(settings);
    }

    public void Run()
    {
        ConsoleMenu.Status("Source: " + settings.SourcePath, ConsoleMenu.Normal);
        Console.WriteLine();

        var counts = new Counts();

        // Snapshot first — we mutate the directory tree as we go.
        var items = scanner.Scan().ToList();
        foreach (var item in items)
        {
            try
            {
                ProcessItem(item, counts);
            }
            catch (Exception ex)
            {
                ConsoleMenu.Status($"  ! {item.OriginalName}: {ex.Message}", ConsoleMenu.Err);
                counts.Errors++;
            }
        }

        Console.WriteLine();
        ConsoleMenu.Status(
            $"Renamed: {counts.Renamed}  Hoisted: {counts.Hoisted}  " +
            $"Deleted empty: {counts.Empty}  Skipped: {counts.Skipped}  Errors: {counts.Errors}",
            ConsoleMenu.Ok);
    }

    private void ProcessItem(MediaItem item, Counts counts)
    {
        Console.Write("  " + item.OriginalName);

        switch (item.Kind)
        {
            case MediaKind.Empty:
                Directory.Delete(item.FullPath, recursive: true);
                ConsoleMenu.WriteColor("  [empty - deleted]", ConsoleMenu.Dim, newline: true);
                counts.Empty++;
                break;

            case MediaKind.MultiSeasonParent:
                HoistParent(item, counts);
                break;

            case MediaKind.TvSeason:
                RenameSeason(item, counts);
                break;

            case MediaKind.Movie:
                RenameMovie(item, counts);
                break;

            default:
                ConsoleMenu.WriteColor("  [skip - unknown]", ConsoleMenu.Dim, newline: true);
                counts.Skipped++;
                break;
        }
    }

    private void RenameSeason(MediaItem item, Counts counts)
    {
        if (string.IsNullOrWhiteSpace(item.ShowName) || item.SeasonNumber is null)
        {
            ConsoleMenu.WriteColor("  [skip - missing show/season]", ConsoleMenu.Dim, newline: true);
            counts.Skipped++;
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
            counts.Skipped++;
            return;
        }

        Directory.Move(item.FullPath, target);
        ConsoleMenu.WriteColor($"  -> {newName}", ConsoleMenu.Ok, newline: true);
        counts.Renamed++;
    }

    private void RenameMovie(MediaItem item, Counts counts)
    {
        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            ConsoleMenu.WriteColor("  [skip - no title]", ConsoleMenu.Dim, newline: true);
            counts.Skipped++;
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
            counts.Skipped++;
            return;
        }

        Directory.Move(item.FullPath, target);
        ConsoleMenu.WriteColor($"  -> {newName} (movie)", ConsoleMenu.Ok, newline: true);
        counts.Renamed++;
    }

    private void HoistParent(MediaItem item, Counts counts)
    {
        var show = item.ShowName;
        if (string.IsNullOrWhiteSpace(show))
        {
            ConsoleMenu.WriteColor("  [skip - could not parse show name]", ConsoleMenu.Err, newline: true);
            counts.Skipped++;
            return;
        }

        Console.WriteLine();

        var hoisted = new List<string>();
        foreach (var season in item.Seasons.OrderBy(s => s.SeasonNumber))
        {
            var newName = NameParser.FormatSeasonFolder(show!, season.SeasonNumber);
            var target = Path.Combine(settings.SourcePath, newName);
            if (Directory.Exists(target))
            {
                ConsoleMenu.WriteColor($"    [skip - exists: {newName}]", ConsoleMenu.Dim, newline: true);
                counts.Skipped++;
                continue;
            }
            Directory.Move(season.FullPath, target);
            hoisted.Add(target);
            ConsoleMenu.WriteColor($"    -> {newName}", ConsoleMenu.Ok, newline: true);
            counts.Hoisted++;
        }

        // Orphan show-level files at the parent (e.g. Bones_Large.jpg, Info.txt)
        // get tucked into the first new season folder so they aren't lost when
        // we delete the parent. Plex doesn't read them but they're cheap to keep.
        if (item.OrphanFilesAtParent.Count > 0 && hoisted.Count > 0)
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
        // that still has hidden video content the scanner missed.
        if (!HasAnyVideoLeft(item.FullPath))
        {
            try { Directory.Delete(item.FullPath, recursive: true); } catch { /* best effort */ }
        }
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

    private sealed class Counts
    {
        public int Renamed, Hoisted, Empty, Skipped, Errors;
    }
}
