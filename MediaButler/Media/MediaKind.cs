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

    /// <summary>
    /// A single-episode dump: a per-episode torrent folder
    /// ("Ahsoka.S01E01...[TGx]") or a loose episode file at the source root.
    /// The Rename stage consolidates these into the canonical
    /// "{Show} - Season XX" folder.
    /// </summary>
    TvEpisode,

    /// <summary>
    /// One folder holding several distinct movies ("The Matrix 1-4 Pack ...").
    /// The Rename stage splits each video into its own "{Title} (YYYY)" folder.
    /// </summary>
    MoviePack,

    /// <summary>
    /// Music content, recognised via the user-curated variation catalog.
    /// MediaButler does not organize music yet — left in place and flagged.
    /// </summary>
    Music,

    /// <summary>
    /// A folder that is a collection husk containing several movie sub-folders
    /// (e.g. <c>Studio.Ghibli/</c> holding <c>Spirited.Away.2001/</c>,
    /// <c>Howl's.Moving.Castle.2004/</c>, etc.).
    /// The Rename stage and the FileBot pre-pass both hoist each sub-folder to the
    /// source root so FileBot can process each movie individually, then delete the husk.
    /// </summary>
    MovieCollection,
}
