namespace MediaButler.Media;

/// <summary>One top-level folder under SourcePath, after the scanner has classified it.</summary>
public sealed record MediaItem
{
    public required string FullPath { get; init; }
    public required string OriginalName { get; init; }
    public required MediaKind Kind { get; init; }

    // For Movie:
    public string? MovieTitle { get; init; }
    public int? MovieYear { get; init; }

    // For TvSeason, TvEpisode, and MultiSeasonParent (parent has Show + nested seasons):
    public string? ShowName { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public IReadOnlyList<SeasonChild> Seasons { get; init; } = Array.Empty<SeasonChild>();
    public IReadOnlyList<string> OrphanFilesAtParent { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when <see cref="FullPath"/> is a loose FILE at the source root
    /// (e.g. "Frankenstein 2025 ... .mkv") rather than a folder. The Rename
    /// stage wraps these into their canonical folder instead of renaming.
    /// </summary>
    public bool IsFile { get; init; }

    /// <summary>For MoviePack: each distinct movie file found inside the pack.</summary>
    public IReadOnlyList<MoviePackChild> PackMovies { get; init; } = Array.Empty<MoviePackChild>();

    /// <summary>
    /// For MultiSeasonParent: episode VIDEO FILES sitting flat at the parent
    /// (no Season subfolder) whose names carry a parseable season+episode —
    /// e.g. "S01 - E01 - Nice Face.mkv" inside a Complete Collection dump.
    /// The Rename stage files each into its "{Show} - Season XX" folder.
    /// </summary>
    public IReadOnlyList<LooseEpisode> LooseEpisodes { get; init; } = Array.Empty<LooseEpisode>();
}

/// <summary>A movie file discovered inside a multi-movie pack folder.</summary>
public sealed record MoviePackChild
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
}

/// <summary>A flat episode file inside a multi-season parent, keyed by parsed season.</summary>
public sealed record LooseEpisode
{
    public required string FilePath { get; init; }
    public required int SeasonNumber { get; init; }
    public required int EpisodeNumber { get; init; }
}

/// <summary>A nested season subfolder discovered inside a multi-season parent.</summary>
public sealed record SeasonChild
{
    public required string FullPath { get; init; }
    public required int SeasonNumber { get; init; }
}
