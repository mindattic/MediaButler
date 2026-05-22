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

    // For TvSeason and MultiSeasonParent (parent has Show + nested seasons):
    public string? ShowName { get; init; }
    public int? SeasonNumber { get; init; }
    public IReadOnlyList<SeasonChild> Seasons { get; init; } = Array.Empty<SeasonChild>();
    public IReadOnlyList<string> OrphanFilesAtParent { get; init; } = Array.Empty<string>();
}

/// <summary>A nested season subfolder discovered inside a multi-season parent.</summary>
public sealed record SeasonChild
{
    public required string FullPath { get; init; }
    public required int SeasonNumber { get; init; }
}
