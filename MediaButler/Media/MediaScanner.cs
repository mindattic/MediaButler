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
    private readonly VariationCatalog catalog;
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
        catalog    = VariationCatalog.Load(settings);
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
        try
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
                catalog.Record(item.OriginalName, item.Kind);
                yield return item;
            }

            foreach (var item in LooseRootFiles())
            {
                var resolved = item;
                if (item.Kind == MediaKind.Unknown && llmFallback is not null)
                {
                    var refined = TryLlmClassifyFileAsync(item, CancellationToken.None).GetAwaiter().GetResult();
                    if (refined is not null) resolved = refined;
                }
                catalog.Record(resolved.OriginalName, resolved.Kind);
                yield return resolved;
            }
        }
        finally
        {
            // Every run grows the variation corpus, even when the caller stops
            // enumerating early. Catalog writes are best-effort telemetry and
            // deliberately also happen in dry-run (like the audit log).
            catalog.Save();
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
        try
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
                catalog.Record(item.OriginalName, item.Kind);
                yield return item;
            }

            foreach (var item in LooseRootFiles())
            {
                ct.ThrowIfCancellationRequested();
                var resolved = item;
                if (item.Kind == MediaKind.Unknown && llmFallback is not null)
                {
                    var refined = await TryLlmClassifyFileAsync(item, ct).ConfigureAwait(false);
                    if (refined is not null) resolved = refined;
                }
                catalog.Record(resolved.OriginalName, resolved.Kind);
                yield return resolved;
            }
        }
        finally
        {
            catalog.Save();
        }
    }

    /// <summary>
    /// Legion fallback for a loose FILE the regex parsers couldn't place —
    /// "file renames that don't match any known pattern". Maps the LLM's
    /// best-guess into the same shapes the regex path produces (a wrappable
    /// Movie or a consolidatable TvEpisode); null on any failure so the file
    /// is left alone (MB-LAW-6).
    /// </summary>
    private async Task<MediaItem?> TryLlmClassifyFileAsync(MediaItem item, CancellationToken ct)
    {
        if (llmFallback is null) return null;
        var guess = await llmFallback.ClassifyFileAsync(item.OriginalName, ct).ConfigureAwait(false);
        if (guess is null) return null;

        return guess.Kind switch
        {
            LlmFileKind.Movie => item with
            {
                Kind = MediaKind.Movie,
                MovieTitle = guess.Title,
                MovieYear = guess.Year,
            },
            LlmFileKind.TvEpisode when guess is { Season: not null, Episode: not null } => item with
            {
                Kind = MediaKind.TvEpisode,
                ShowName = guess.Title,
                SeasonNumber = guess.Season,
                EpisodeNumber = guess.Episode,
            },
            _ => null,
        };
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

        // Extras / Specials / Bonus folders — sit next to a show but aren't a season.
        // Checked BEFORE the Empty test: an extras folder that holds only subtitles,
        // nfo, or artwork (no recognized video) would otherwise classify as Empty
        // and the Rename stage would DELETE it. Classify as Extras so it is left in
        // place and surfaced for manual review instead of destroyed.
        if (NameParser.LooksLikeExtras(name))
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Extras };

        // User-curated catalog pin. Checked BEFORE the Empty test: a music
        // folder holds no recognised VIDEO files and would otherwise classify
        // Empty — and the Rename stage would try to delete it.
        var hint = catalog.LookupHint(name);
        if (hint == VariationCatalog.Hint.Music)
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Music };

        // Empty? (no video files anywhere underneath) — but a folder full of
        // AUDIO is music, not an empty shell on the delete path.
        if (!HasAnyVideo(fullPath))
        {
            if (HasAnyAudio(fullPath))
                return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Music };
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Empty };
        }

        // A movie pin skips every TV-shaped check; a TV pin skips the movie path.
        if (hint == VariationCatalog.Hint.Movie)
            return ClassifyMoviePath(fullPath, name);

        // Multi-season? Look at name first, then structure.
        if (NameParser.LooksLikeMultiSeason(name) || HasSeasonSubfolder(fullPath))
            return BuildMultiSeasonParent(fullPath, name);

        // Per-episode torrent folder ("Ahsoka.S01E01...[TGx]")?
        if (NameParser.LooksLikeEpisodeFile(name) &&
            NameParser.ParseEpisode(name) is { } folderEp &&
            !string.IsNullOrWhiteSpace(folderEp.Show))
        {
            return new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.TvEpisode,
                ShowName = folderEp.Show,
                SeasonNumber = folderEp.Season,
                EpisodeNumber = folderEp.Episode,
            };
        }

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
                TvYear = single.Value.Year,
            };
        }

        // A TV pin on a folder with no recognisable season/episode marker:
        // surface for manual review instead of guessing a movie.
        if (hint == VariationCatalog.Hint.Tv)
            return ClassifyTvByContent(fullPath, name)
                ?? new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Unknown };

        // Movie collection husk? A folder with no top-level video files whose
        // sub-dirs are each parseable as movies with years — e.g. "Studio.Ghibli/"
        // holding "Spirited.Away.2001/", "Howl's.Moving.Castle.2004/", etc.
        // Must be checked before ClassifyMoviePath so FileBot doesn't try to rename
        // the husk itself ("Studio Ghibli" → exit 3 — no match).
        if (!NameParser.HasAnySeasonMarker(name) && TopLevelVideos(fullPath).Count == 0 && IsMovieCollection(fullPath))
            return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.MovieCollection };

        // Movie? (video file present + no season marker)
        if (!NameParser.HasAnySeasonMarker(name))
            return ClassifyMoviePath(fullPath, name);

        return new MediaItem { FullPath = fullPath, OriginalName = name, Kind = MediaKind.Unknown };
    }

    /// <summary>
    /// The movie-shaped endgame for a folder: first check for a multi-movie
    /// pack ("The Matrix 1-4 Pack 1999-2021 ..."), then — when the folder name
    /// itself carries no release year — check whether the CONTENT is actually
    /// episodes ("Battletech" holding "Battle tech - 1.09 - ....avi"), and only
    /// then settle on Movie.
    /// </summary>
    private MediaItem ClassifyMoviePath(string fullPath, string name)
    {
        var movie = NameParser.ParseMovie(name, settings.TitleYearOverrides);

        var pack = TryBuildMoviePack(fullPath, name);
        if (pack is not null) return pack;

        if (movie.Year is null && ClassifyTvByContent(fullPath, name) is { } tv)
            return tv;

        return new MediaItem
        {
            FullPath = fullPath,
            OriginalName = name,
            Kind = MediaKind.Movie,
            MovieTitle = movie.Title,
            MovieYear = movie.Year,
        };
    }

    /// <summary>
    /// Detect a folder holding SEVERAL distinct movies: two or more non-sample
    /// videos at the top level, every one carrying its own release year, with
    /// at least two distinct (title, year) pairs. Returns null when the folder
    /// looks like a normal single movie.
    /// </summary>
    private MediaItem? TryBuildMoviePack(string fullPath, string name)
    {
        var videos = TopLevelVideos(fullPath);
        if (videos.Count < 2) return null;

        var children = new List<MoviePackChild>();
        foreach (var file in videos)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var parsed = NameParser.ParseMovie(stem, settings.TitleYearOverrides);
            if (parsed.Year is null || string.IsNullOrWhiteSpace(parsed.Title)) return null;
            children.Add(new MoviePackChild { FilePath = file, Title = parsed.Title, Year = parsed.Year });
        }

        var distinct = children.Select(c => (c.Title.ToUpperInvariant(), c.Year)).Distinct().Count();
        if (distinct < 2) return null; // CD1/CD2-style split of ONE movie — not a pack

        return new MediaItem
        {
            FullPath = fullPath,
            OriginalName = name,
            Kind = MediaKind.MoviePack,
            PackMovies = children,
        };
    }

    /// <summary>
    /// Content-based TV detection for folders whose NAME says nothing useful
    /// ("Battletech"): when every non-sample top-level video parses as an
    /// episode, classify by the seasons found — one season → TvSeason, several
    /// → MultiSeasonParent carrying the files as <see cref="LooseEpisode"/>s.
    /// The show name comes from the folder (it's what we rename), not the files.
    /// </summary>
    private MediaItem? ClassifyTvByContent(string fullPath, string name)
    {
        var videos = TopLevelVideos(fullPath);
        if (videos.Count == 0) return null;

        var episodes = new List<LooseEpisode>();
        foreach (var file in videos)
        {
            var ep = NameParser.ParseEpisode(Path.GetFileName(file));
            if (ep is null) return null;
            episodes.Add(new LooseEpisode { FilePath = file, SeasonNumber = ep.Season, EpisodeNumber = ep.Episode });
        }

        var show = NameParser.CleanShowName(NameParser.Normalize(name));
        if (string.IsNullOrWhiteSpace(show)) return null;

        var seasons = episodes.Select(e => e.SeasonNumber).Distinct().ToList();
        if (seasons.Count == 1)
        {
            return new MediaItem
            {
                FullPath = fullPath,
                OriginalName = name,
                Kind = MediaKind.TvSeason,
                ShowName = show,
                SeasonNumber = seasons[0],
            };
        }

        return new MediaItem
        {
            FullPath = fullPath,
            OriginalName = name,
            Kind = MediaKind.MultiSeasonParent,
            ShowName = show,
            LooseEpisodes = episodes,
            OrphanFilesAtParent = SafeTopLevelFiles(fullPath),
        };
    }

    /// <summary>Top-level non-sample video files of a folder (never throws).</summary>
    private List<string> TopLevelVideos(string fullPath)
    {
        var result = new List<string>();
        foreach (var f in SafeTopLevelFiles(fullPath))
        {
            var fname = Path.GetFileName(f);
            if (!videoExts.Contains(Path.GetExtension(f))) continue;
            if (NameParser.IsSampleName(fname)) continue;
            result.Add(f);
        }
        return result;
    }

    private static IReadOnlyList<string> SafeTopLevelFiles(string fullPath)
    {
        try { return Directory.EnumerateFiles(fullPath).ToList(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (DirectoryNotFoundException)  { return Array.Empty<string>(); }
        catch (IOException)                 { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Classify loose FILES at the source root — torrent clients drop bare
    /// movie/episode files next to the folders ("Frankenstein 2025 ... .mkv").
    /// Dotfiles (".....parts" partial-download markers), non-video files, and
    /// sample clips are skipped entirely; everything else classifies as a
    /// to-be-wrapped Movie, a to-be-consolidated TvEpisode, or Unknown.
    /// </summary>
    private IEnumerable<MediaItem> LooseRootFiles()
    {
        if (!Directory.Exists(settings.SourcePath)) yield break;
        foreach (var file in SafeTopLevelFiles(settings.SourcePath))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            if (!videoExts.Contains(Path.GetExtension(file))) continue;
            if (NameParser.IsSampleName(name)) continue;
            yield return ClassifyLooseFile(file, name);
        }
    }

    private MediaItem ClassifyLooseFile(string file, string name)
    {
        var hint = catalog.LookupHint(name);
        if (hint == VariationCatalog.Hint.Music)
            return new MediaItem { FullPath = file, OriginalName = name, Kind = MediaKind.Music, IsFile = true };

        if (hint != VariationCatalog.Hint.Movie &&
            NameParser.ParseEpisode(name) is { } ep &&
            !string.IsNullOrWhiteSpace(ep.Show))
        {
            return new MediaItem
            {
                FullPath = file,
                OriginalName = name,
                Kind = MediaKind.TvEpisode,
                ShowName = ep.Show,
                SeasonNumber = ep.Season,
                EpisodeNumber = ep.Episode,
                IsFile = true,
            };
        }

        var movie = NameParser.ParseMovie(Path.GetFileNameWithoutExtension(name), settings.TitleYearOverrides);
        if (hint == VariationCatalog.Hint.Movie || movie.Year is not null)
        {
            return new MediaItem
            {
                FullPath = file,
                OriginalName = name,
                Kind = MediaKind.Movie,
                MovieTitle = movie.Title,
                MovieYear = movie.Year,
                IsFile = true,
            };
        }

        return new MediaItem { FullPath = file, OriginalName = name, Kind = MediaKind.Unknown, IsFile = true };
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
        var orphanFiles = new List<string>();
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(fullPath))
            {
                var subName = Path.GetFileName(sub);
                var sn = NameParser.ParseNestedSeasonName(subName);
                if (sn is not null && HasAnyVideo(sub))
                    seasons.Add(new SeasonChild { FullPath = sub, SeasonNumber = sn.Value });
            }

            foreach (var f in Directory.EnumerateFiles(fullPath))
                orphanFiles.Add(f);
        }
        // A protected or race-deleted directory must not unwind the whole scan
        // (which is materialized outside any stage's per-item try/catch). Match
        // ComputeHasAnyVideo's guard set and classify with whatever we gathered.
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException)  { }
        catch (IOException)                 { }

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

        // Flat complete-collection dumps ("Killing Eve - The Complete
        // Collection (2018-2022)") keep every episode loose at the parent with
        // names like "S01 - E01 - Nice Face.mkv". Parse those now so the Rename
        // stage can file each into its "{Show} - Season XX" folder instead of
        // dead-ending on "no season subfolders to hoist".
        var looseEpisodes = new List<LooseEpisode>();
        foreach (var f in orphanFiles)
        {
            var fname = Path.GetFileName(f);
            if (!videoExts.Contains(Path.GetExtension(f))) continue;
            if (NameParser.IsSampleName(fname)) continue;
            if (NameParser.ParseEpisode(fname) is { } ep)
                looseEpisodes.Add(new LooseEpisode { FilePath = f, SeasonNumber = ep.Season, EpisodeNumber = ep.Episode });
        }

        return new MediaItem
        {
            FullPath = fullPath,
            OriginalName = name,
            Kind = MediaKind.MultiSeasonParent,
            ShowName = show,
            Seasons = seasons,
            OrphanFilesAtParent = orphanFiles,
            LooseEpisodes = looseEpisodes,
        };
    }

    /// <summary>
    /// True when the folder holds at least one nested <c>Season XX</c> subfolder
    /// with video. Detecting even a <em>single</em> season subfolder matters: a
    /// folder whose name carries no season marker but contains <c>Season 05</c>
    /// (the canonical Plex <c>Show\Season XX</c> layout) must be treated as a
    /// show parent, not classified as a Movie — otherwise a Relocate pass over
    /// a TV destination would evict whole single-season shows into Movies.
    /// Requires video so empty "Season N" shells don't yield zero hoistable
    /// seasons in <see cref="BuildMultiSeasonParent"/>.
    /// </summary>
    private bool HasSeasonSubfolder(string fullPath)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(fullPath))
            {
                var subName = Path.GetFileName(sub);
                if (NameParser.ParseNestedSeasonName(subName) is not null && HasAnyVideo(sub))
                    return true;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException)  { }
        catch (IOException)                 { }
        return false;
    }

    /// <summary>
    /// True when a folder contains at least two sub-directories that each
    /// (a) parse with a release year via <see cref="NameParser.ParseMovie"/> and
    /// (b) contain at least one video file.
    /// Used to detect collection husks like "Studio.Ghibli/" before the movie-path
    /// classifier blindly treats the husk itself as a movie.
    /// </summary>
    private bool IsMovieCollection(string fullPath)
    {
        var count = 0;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(fullPath))
            {
                var parsed = NameParser.ParseMovie(Path.GetFileName(sub), settings.TitleYearOverrides);
                if (parsed.Year is not null && HasAnyVideo(sub))
                {
                    count++;
                    if (count >= 2) return true;
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException)  { }
        catch (IOException)                 { }
        return false;
    }

    private bool HasAnyAudio(string fullPath)
    {
        var audioExts = new HashSet<string>(settings.AudioExtensions, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(f);
                if (!string.IsNullOrEmpty(ext) && audioExts.Contains(ext)) return true;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException)  { }
        catch (IOException)                 { }
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
        catch (IOException) { /* not-ready drive, path too long, locked subtree — treat as no video */ }
        return false;
    }
}
