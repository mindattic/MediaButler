using Tagsmith.Llm;
using Tagsmith.Settings;

namespace Tagsmith.Media;

/// <summary>
/// Walks the top level of <see cref="TagsmithSettings.SourcePath"/> and
/// classifies each folder into a <see cref="MediaItem"/>. The classifier is
/// deliberately ordered: empty first, then multi-season (structure trumps
/// name), then single season, then movie. Anything left is Unknown.
/// </summary>
public sealed class MediaScanner
{
    private readonly TagsmithSettings settings;
    private readonly HashSet<string> excluded;
    private readonly HashSet<string> videoExts;
    private readonly LegionFallbackParser? llmFallback;

    public MediaScanner(TagsmithSettings settings)
    {
        this.settings = settings;
        excluded   = new HashSet<string>(settings.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
        videoExts  = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        llmFallback = settings.EnableLlmFallback ? new LegionFallbackParser(settings) : null;
    }

    public IEnumerable<MediaItem> Scan()
    {
        if (!Directory.Exists(settings.SourcePath)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(settings.SourcePath))
        {
            var name = Path.GetFileName(dir);
            if (excluded.Contains(name)) continue;
            yield return Classify(dir);
        }
    }

    public MediaItem Classify(string fullPath)
    {
        var name = Path.GetFileName(fullPath);

        // Empty? (no video files anywhere underneath)
        if (!HasAnyVideo(fullPath))
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Empty };

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
            var movie = NameParser.ParseMovie(name);
            return new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.Movie,
                MovieTitle = movie.Title,
                MovieYear = movie.Year,
            };
        }

        // Regex couldn't classify. Try the LLM fallback as a last resort.
        if (llmFallback is not null)
        {
            var sampleFiles = Directory.EnumerateFiles(fullPath).Take(6).Select(Path.GetFileName).Where(s => s is not null).Cast<string>().ToList();
            var guess = llmFallback.ClassifyAsync(name, sampleFiles).GetAwaiter().GetResult();
            if (guess is not null)
            {
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
                    _ => new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Unknown },
                };
            }
        }

        return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Unknown };
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
                var subName = Path.GetFileName(sub.FullPath);
                // Try to recover show from a child like "Sherlock.Season.1.S01..."
                var parts = subName.Split('.', '_', ' ');
                var subShow = NameParser.ParseSingleSeason(subName)?.Show;
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
            if (NameParser.ParseNestedSeasonName(subName) is not null)
            {
                count++;
                if (count >= 2) return true;
            }
        }
        return false;
    }

    private bool HasAnyVideo(string fullPath)
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
