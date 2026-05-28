using MediaButler.Llm;
using MediaButler.Settings;

namespace MediaButler.Media;

/// <summary>
/// Walks the top level of <see cref="MediaButlerSettings.SourcePath"/> and
/// classifies each folder into a <see cref="MediaItem"/>. The classifier is
/// deliberately ordered: empty first, then multi-season (structure trumps
/// name), then single season, then movie. Anything left is Unknown.
/// </summary>
public sealed class MediaScanner
{
    private readonly MediaButlerSettings settings;
    private readonly HashSet<string> excluded;
    private readonly HashSet<string> videoExts;
    private readonly LegionFallbackParser? llmFallback;
    // Cache HasAnyVideo results per scan. A multi-season parent classify
    // ends up asking for the same subtree twice (once for the Empty check,
    // again for HasMultipleSeasonSubfolders / BuildMultiSeasonParent),
    // and each walk is O(files-in-subtree).
    private readonly Dictionary<string, bool> hasVideoCache = new(StringComparer.OrdinalIgnoreCase);

    public MediaScanner(MediaButlerSettings settings)
    {
        this.settings = settings;
        excluded   = new HashSet<string>(settings.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
        videoExts  = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        llmFallback = settings.EnableLlmFallback ? new LegionFallbackParser(settings) : null;
    }

    /// <summary>
    /// Synchronous scan — the fast path used by every stage. When
    /// <see cref="MediaButlerSettings.EnableLlmFallback"/> is on, folders the
    /// regex parser can't classify are refined via the LLM (resolved
    /// synchronously); when it's off this is pure-filesystem and never touches
    /// the network. <see cref="ScanAsync"/> is the truly-async equivalent for
    /// callers that already run in an async context.
    /// </summary>
    public IEnumerable<MediaItem> Scan()
    {
        foreach (var dir in TopLevelDirs())
        {
            var item = ClassifyByRegex(dir);
            if (item.Kind == MediaKind.Unknown && llmFallback is not null)
            {
                // No SynchronizationContext on the console / Task.Run threads that
                // drive the pipeline, so blocking here can't deadlock.
                var refined = TryLlmClassifyAsync(dir, CancellationToken.None).GetAwaiter().GetResult();
                if (refined is not null) item = refined;
            }
            yield return item;
        }
    }

    /// <summary>
    /// Async scan. Same regex pipeline as <see cref="Scan"/> but with a real
    /// <c>await</c> on the LLM fallback so we don't deadlock on
    /// <c>.GetAwaiter().GetResult()</c> inside an iterator. Yields items in
    /// directory-enumeration order.
    /// </summary>
    public async IAsyncEnumerable<MediaItem> ScanAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var dir in TopLevelDirs())
        {
            ct.ThrowIfCancellationRequested();
            var item = ClassifyByRegex(dir);
            if (item.Kind == MediaKind.Unknown && llmFallback is not null)
            {
                var refined = await TryLlmClassifyAsync(dir, ct).ConfigureAwait(false);
                if (refined is not null) item = refined;
            }
            yield return item;
        }
    }

    private IEnumerable<string> TopLevelDirs()
    {
        if (!Directory.Exists(settings.SourcePath)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(settings.SourcePath))
        {
            var name = Path.GetFileName(dir);
            // Top-level dotfile directories (.claude, .tmp, .stversions, …) are
            // never user media — skip unconditionally so the empty-folder pass
            // can't delete them.
            if (name.StartsWith('.')) continue;
            if (excluded.Contains(name)) continue;
            yield return dir;
        }
    }

    /// <summary>
    /// Public single-folder classification. Sync — does not consult the LLM
    /// (use <see cref="ClassifyAsync"/> for the LLM-aware variant). Preserved
    /// for callers that want to classify one folder directly.
    /// </summary>
    public MediaItem Classify(string fullPath) => ClassifyByRegex(fullPath);

    /// <summary>Single-folder classification with LLM fallback for Unknown items.</summary>
    public async Task<MediaItem> ClassifyAsync(string fullPath, CancellationToken ct = default)
    {
        var item = ClassifyByRegex(fullPath);
        if (item.Kind != MediaKind.Unknown || llmFallback is null) return item;
        var refined = await TryLlmClassifyAsync(fullPath, ct).ConfigureAwait(false);
        return refined ?? item;
    }

    private MediaItem ClassifyByRegex(string fullPath)
    {
        var name = Path.GetFileName(fullPath);

        // Empty? (no video files anywhere underneath)
        if (!HasAnyVideo(fullPath))
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Empty };

        // Extras / Specials / Bonus folders — sit next to a show but aren't a season.
        // Check before the season-marker tests so "Show - Extras" isn't misread.
        if (NameParser.LooksLikeExtras(name))
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Extras };

        // Multi-season? Look at name first, then structure.
        if (NameParser.LooksLikeMultiSeason(name) || HasMultipleSeasonSubfolders(fullPath))
            return BuildMultiSeasonParent(fullPath, name);

        // Single season?
        var single = NameParser.ParseSingleSeason(name);
        if (single is not null)
        {
            return new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.TvSeason,
                ShowName = single.Value.Show,
                SeasonNumber = single.Value.Season,
            };
        }

        // Movie? (video file present + no season marker)
        if (!NameParser.HasAnySeasonMarker(name))
        {
            var movie = NameParser.ParseMovie(name, settings.TitleYearOverrides);
            return new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.Movie,
                MovieTitle = movie.Title,
                MovieYear = movie.Year,
            };
        }

        return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Unknown };
    }

    /// <summary>Best-effort LLM classification of a folder; returns null on any failure.</summary>
    private async Task<MediaItem?> TryLlmClassifyAsync(string fullPath, CancellationToken ct)
    {
        if (llmFallback is null) return null;
        var name = Path.GetFileName(fullPath);
        // Sample enumeration matches HasAnyVideo's guard set — a protected or
        // race-deleted directory must not unwind the whole scan just because
        // we wanted file names for the LLM prompt.
        List<string> sampleFiles;
        try
        {
            sampleFiles = Directory.EnumerateFiles(fullPath)
                .Take(6)
                .Select(Path.GetFileName)
                .Where(s => s is not null)
                .Cast<string>()
                .ToList();
        }
        catch (UnauthorizedAccessException) { sampleFiles = new List<string>(); }
        catch (DirectoryNotFoundException)  { sampleFiles = new List<string>(); }
        catch (IOException)                 { sampleFiles = new List<string>(); }
        var guess = await llmFallback.ClassifyAsync(name, sampleFiles, ct).ConfigureAwait(false);
        if (guess is null) return null;

        return guess.Kind switch
        {
            LlmKind.Movie => new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.Movie,
                MovieTitle = guess.Title,
                MovieYear = guess.Year,
            },
            LlmKind.TvSeason when guess.Season.HasValue => new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.TvSeason,
                ShowName = guess.Title,
                SeasonNumber = guess.Season,
            },
            _ => null,
        };
    }

    private MediaItem BuildMultiSeasonParent(string fullPath, string name)
    {
        var show = NameParser.ParseMultiSeasonParent(name);
        var seasons = new List<SeasonChild>();
        foreach (var sub in Directory.EnumerateDirectories(fullPath))
        {
            var subName = Path.GetFileName(sub);
            var sn = NameParser.ParseNestedSeasonName(subName);
            if (sn is not null && HasAnyVideo(sub))
                seasons.Add(new SeasonChild { FullPath = sub, SeasonNumber = sn.Value });
        }

        var orphanFiles = new List<string>();
        foreach (var f in Directory.EnumerateFiles(fullPath))
            orphanFiles.Add(f);

        // If the name didn't yield a show (e.g. structure-only detection), fall
        // back to the first nested season's show component.
        if (string.IsNullOrWhiteSpace(show))
        {
            foreach (var sub in seasons)
            {
                var subShow = NameParser.ParseSingleSeason(Path.GetFileName(sub.FullPath))?.Show;
                if (!string.IsNullOrWhiteSpace(subShow)) { show = subShow; break; }
            }
        }

        return new MediaItem
        {
            FullPath = fullPath,
            OriginalName = name,
            Kind = MediaKind.MultiSeasonParent,
            ShowName = show,
            Seasons = seasons,
            OrphanFilesAtParent = orphanFiles,
        };
    }

    private bool HasMultipleSeasonSubfolders(string fullPath)
    {
        var count = 0;
        foreach (var sub in Directory.EnumerateDirectories(fullPath))
        {
            var subName = Path.GetFileName(sub);
            // Require video too, so this agrees with BuildMultiSeasonParent —
            // otherwise empty "Season N" shells could classify a parent as
            // multi-season but yield zero hoistable seasons.
            if (NameParser.ParseNestedSeasonName(subName) is not null && HasAnyVideo(sub))
            {
                count++;
                if (count >= 2) return true;
            }
        }
        return false;
    }

    private bool HasAnyVideo(string fullPath)
    {
        if (hasVideoCache.TryGetValue(fullPath, out var cached)) return cached;
        var result = ComputeHasAnyVideo(fullPath);
        hasVideoCache[fullPath] = result;
        return result;
    }

    private bool ComputeHasAnyVideo(string fullPath)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(f);
                if (!string.IsNullOrEmpty(ext) && videoExts.Contains(ext)) return true;
            }
        }
        catch (UnauthorizedAccessException) { /* skip protected dirs */ }
        catch (DirectoryNotFoundException) { /* race vs. external mover */ }
        return false;
    }
}
