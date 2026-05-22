namespace Tagsmith.Media;

/// <summary>How a top-level folder under SourcePath is classified by the scanner.</summary>
public enum MediaKind
{
    /// <summary>Couldn't classify — Tagsmith leaves it alone.</summary>
    Unknown,

    /// <summary>Movie folder (year + video file, no season markers).</summary>
    Movie,

    /// <summary>Single TV season folder (one show, one season).</summary>
    TvSeason,

    /// <summary>Parent folder containing multiple Season subfolders that must be hoisted.</summary>
    MultiSeasonParent,

    /// <summary>Has no video files at all — Tagsmith deletes these.</summary>
    Empty,
}
