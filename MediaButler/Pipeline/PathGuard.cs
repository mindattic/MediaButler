using MediaButler.Settings;
using MediaButler.Ui;
// Spectre.Console exports a Status spinner type; disambiguate our pipeline logger.
using Status = MediaButler.Ui.Status;

namespace MediaButler.Pipeline;

/// <summary>
/// Source-vs-destination overlap guard. Pointing the source at <c>M:\TV</c>
/// would cause every show folder to be re-parsed as a multi-season parent and
/// hoisted into oblivion — this guard is the single most important safety net
/// in the pipeline. In live mode the overlap is a hard refusal; in dry-run we
/// warn but allow it (inspecting the destination is a legitimate use case).
/// </summary>
public static class PathGuard
{
    /// <summary>True when <paramref name="source"/> equals, contains, or is contained by <paramref name="other"/>.</summary>
    internal static bool PathOverlaps(string source, string other)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(other)) return false;
        string a, b;
        try
        {
            a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));
        }
        catch { return false; }

        var cmp = StringComparison.OrdinalIgnoreCase;
        if (string.Equals(a, b, cmp)) return true;
        var sep = Path.DirectorySeparatorChar.ToString();
        return a.StartsWith(b + sep, cmp) || b.StartsWith(a + sep, cmp);
    }

    /// <summary>
    /// Validate that the source exists AND does not overlap a destination.
    /// Prints any failure message to the console using <see cref="Status"/>
    /// and returns false. In dry-run the overlap downgrades to a warning so
    /// the user can still inspect classification.
    /// </summary>
    public static bool ValidatePaths(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            Status.Print("Source path not found: " + s.SourcePath, Theme.Err);
            Status.Print("Set it via the Settings menu.", Theme.Dim);
            return false;
        }

        var overlap = PathOverlaps(s.SourcePath, s.TvDestination)
                   || PathOverlaps(s.SourcePath, s.MoviesDestination);
        if (!overlap) return true;

        if (s.DryRun)
        {
            Status.Print("WARNING: Source path overlaps a destination. Dry-run only — no mutations will occur.", Theme.Active);
            Status.Print($"  Source : {s.SourcePath}",        Theme.Active);
            Status.Print($"  TV     : {s.TvDestination}",     Theme.Active);
            Status.Print($"  Movies : {s.MoviesDestination}", Theme.Active);
            return true;
        }

        Status.Print("REFUSING TO RUN: Source path overlaps a destination.", Theme.Err);
        Status.Print($"  Source : {s.SourcePath}",        Theme.Err);
        Status.Print($"  TV     : {s.TvDestination}",     Theme.Err);
        Status.Print($"  Movies : {s.MoviesDestination}", Theme.Err);
        Status.Print("Running here would reprocess already-organized folders and could destroy data.", Theme.Err);
        Status.Print("Pass --dry-run to inspect classification without risk.", Theme.Dim);
        return false;
    }
}
