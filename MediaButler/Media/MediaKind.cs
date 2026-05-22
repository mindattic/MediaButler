namespace MediaButler.Media;

/// <summary>How a top-level folder under SourcePath is classified by the scanner.</summary>
public enum MediaKind
{
    /// <summary>Couldn't classify — MediaButler leaves it alone.</summary>
    Unknown,

    /// <summary>Movie folder (year + video file, no season markers).</summary>
    Movie,

    /// <summary>Single TV season folder (one show, one season).</summary>
    TvSeason,

    /// <summary>Parent folder containing multiple Season subfolders that must be hoisted.</summary>
    MultiSeasonParent,

    /// <summary>Has no video files at all — MediaButler deletes these.</summary>
    Empty,

    /// <summary>
    /// "Extras", "Specials", "Bonus", etc. — companion content to a show that
    /// must not be classified as a movie. MediaButler leaves it in place and
    /// flags it in the final report so the user can decide what to do.
    /// </summary>
    Extras,
}
