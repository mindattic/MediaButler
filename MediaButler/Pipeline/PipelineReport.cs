using MediaButler.Media;

namespace MediaButler.Pipeline;

/// <summary>
/// Running tally of what each stage did during a single pipeline invocation.
/// Stages append to the shared instance; the app prints a consolidated summary
/// at the end so the user knows exactly what changed, what FileBot rejected,
/// and what is still sitting at the source root waiting for manual cleanup.
/// </summary>
public sealed class PipelineReport
{
    public int Renamed { get; set; }
    public int Hoisted { get; set; }
    public int EmptyDeleted { get; set; }
    public int TvMoved { get; set; }
    public int MoviesMoved { get; set; }
    public int FileBotTvOk { get; set; }
    public int FileBotMoviesOk { get; set; }
    public int ArtworkOk { get; set; }
    public int SubtitlesOk { get; set; }

    /// <summary>Item paths that produced an exception during the run.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Items left at the source root after the pipeline finished — Unknown,
    /// Extras, or anything the parser/LLM couldn't classify. These need a
    /// human eye.
    /// </summary>
    public List<ManualItem> NeedsManual { get; } = new();

    public void RecordError(string itemPath, string message) =>
        Errors.Add($"{itemPath}: {message}");

    public void RecordManual(string itemPath, MediaKind kind, string reason) =>
        NeedsManual.Add(new ManualItem(itemPath, kind, reason));
}

/// <summary>A source-root entry the pipeline couldn't safely process.</summary>
public sealed record ManualItem(string Path, MediaKind Kind, string Reason);
