using System.Text.Json;
using System.Text.Json.Serialization;
using MediaButler.Settings;

namespace MediaButler.Media;

/// <summary>
/// Persistent, user-appendable catalog of every naming variation MediaButler
/// has seen, kept at <c>%APPDATA%\MindAttic\MediaButler\variations.json</c>
/// (overridable via <see cref="MediaButlerSettings.VariationCatalogPath"/>).
///
/// <para>Two jobs:</para>
/// <list type="number">
///   <item><b>Cataloging</b> — every scan appends newly-seen top-level names
///         into the section matching their classification (<c>movie</c> /
///         <c>tv</c> / <c>music</c> / <c>unknown</c>), so the corpus of real
///         formats grows with every run.</item>
///   <item><b>Hints</b> — the file is meant to be hand-edited: moving an entry
///         from <c>unknown</c> into <c>movie</c>, <c>tv</c>, or <c>music</c>
///         pins that name's category on the next run, overriding the regex
///         classifier's category choice (the parsers still extract
///         title/show/season fields).</item>
/// </list>
///
/// <para>Loading is tolerant: a missing file yields an empty catalog; a
/// corrupted file disables saving for the rest of the run so a hand-edit with
/// a stray comma is never clobbered by MediaButler rewriting the file.</para>
/// </summary>
public sealed class VariationCatalog
{
    /// <summary>Category a hand-curated catalog entry pins a name to.</summary>
    public enum Hint { Movie, Tv, Music }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string path;
    private readonly bool loadFailed;
    private bool dirty;

    private readonly List<string> movie;
    private readonly List<string> tv;
    private readonly List<string> music;
    private readonly List<string> unknown;
    private readonly HashSet<string> movieSet;
    private readonly HashSet<string> tvSet;
    private readonly HashSet<string> musicSet;
    private readonly HashSet<string> unknownSet;

    /// <summary>Default on-disk location, next to settings.json.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", SettingsService.AppName, "variations.json");

    /// <summary>
    /// Effective path for a given settings instance. Precedence: explicit
    /// settings override → <c>MEDIABUTLER_VARIATIONS_PATH</c> environment
    /// variable (used by out-of-process tests to stay hermetic) → default.
    /// </summary>
    public static string ResolvePath(MediaButlerSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.VariationCatalogPath)) return settings.VariationCatalogPath;
        var env = Environment.GetEnvironmentVariable("MEDIABUTLER_VARIATIONS_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return DefaultPath;
    }

    /// <summary>Load the catalog for <paramref name="settings"/> (never throws).</summary>
    public static VariationCatalog Load(MediaButlerSettings settings) => Load(ResolvePath(settings));

    /// <summary>Load the catalog at <paramref name="path"/> (never throws).</summary>
    public static VariationCatalog Load(string path)
    {
        VariationCatalog cat;
        if (!File.Exists(path))
        {
            cat = new VariationCatalog(path, new CatalogDto(), loadFailed: false);
        }
        else
        {
            try
            {
                var dto = JsonSerializer.Deserialize<CatalogDto>(File.ReadAllText(path), JsonOpts) ?? new CatalogDto();
                cat = new VariationCatalog(path, dto, loadFailed: false);
            }
            catch
            {
                // Unparseable user edit — run with an empty catalog but never save
                // over the file, so the user's edits survive to be fixed by hand.
                return new VariationCatalog(path, new CatalogDto(), loadFailed: true);
            }
        }

        // The on-disk file is a CLONE of the hardcoded master list plus
        // everything discovered/appended since. Merging here (instead of only
        // at file creation) lets a MediaButler upgrade introduce new master
        // entries without disturbing user additions.
        cat.SeedFromMaster();
        return cat;
    }

    private void SeedFromMaster()
    {
        foreach (var m in MasterVariations.Movie) Record(m, MediaKind.Movie);
        foreach (var t in MasterVariations.Tv)    Record(t, MediaKind.TvSeason);
        foreach (var mu in MasterVariations.Music) Record(mu, MediaKind.Music);
    }

    private VariationCatalog(string path, CatalogDto dto, bool loadFailed)
    {
        this.path = path;
        this.loadFailed = loadFailed;
        movie   = dto.Movie   ?? new List<string>();
        tv      = dto.Tv      ?? new List<string>();
        music   = dto.Music   ?? new List<string>();
        unknown = dto.Unknown ?? new List<string>();
        movieSet   = new HashSet<string>(movie,   StringComparer.OrdinalIgnoreCase);
        tvSet      = new HashSet<string>(tv,      StringComparer.OrdinalIgnoreCase);
        musicSet   = new HashSet<string>(music,   StringComparer.OrdinalIgnoreCase);
        unknownSet = new HashSet<string>(unknown, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the on-disk file existed but could not be parsed.</summary>
    public bool LoadFailed => loadFailed;

    public IReadOnlyList<string> Movies  => movie;
    public IReadOnlyList<string> Tv      => tv;
    public IReadOnlyList<string> Music   => music;
    public IReadOnlyList<string> Unknown => unknown;

    /// <summary>
    /// Category pin for a name the user has filed under movie/tv/music.
    /// Entries in <c>unknown</c> are catalog-only and never pin a category.
    /// </summary>
    public Hint? LookupHint(string name)
    {
        if (movieSet.Contains(name)) return Hint.Movie;
        if (tvSet.Contains(name))    return Hint.Tv;
        if (musicSet.Contains(name)) return Hint.Music;
        return null;
    }

    /// <summary>
    /// Record a classified name into the right section if it isn't already
    /// cataloged anywhere. Returns true when a new entry was added.
    /// </summary>
    public bool Record(string name, MediaKind kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (movieSet.Contains(name) || tvSet.Contains(name) ||
            musicSet.Contains(name) || unknownSet.Contains(name)) return false;

        switch (kind)
        {
            case MediaKind.Movie:
            case MediaKind.MoviePack:
                movie.Add(name); movieSet.Add(name); break;
            case MediaKind.TvSeason:
            case MediaKind.TvEpisode:
            case MediaKind.MultiSeasonParent:
                tv.Add(name); tvSet.Add(name); break;
            case MediaKind.Music:
                music.Add(name); musicSet.Add(name); break;
            case MediaKind.Unknown:
                unknown.Add(name); unknownSet.Add(name); break;
            default:
                return false; // Empty/Extras shells aren't naming variations worth keeping
        }
        dirty = true;
        return true;
    }

    /// <summary>
    /// Persist the catalog if anything new was recorded this run. Best-effort:
    /// a write failure must never break a pipeline run, and a load failure
    /// permanently disables saving (see <see cref="Load(string)"/>).
    /// </summary>
    public void Save()
    {
        if (!dirty || loadFailed) return;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var dto = new CatalogDto { Movie = movie, Tv = tv, Music = music, Unknown = unknown };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOpts));
            dirty = false;
        }
        catch { /* cataloging is telemetry, not pipeline state */ }
    }

    private sealed class CatalogDto
    {
        [JsonPropertyName("movie")]   public List<string>? Movie   { get; set; }
        [JsonPropertyName("tv")]      public List<string>? Tv      { get; set; }
        [JsonPropertyName("music")]   public List<string>? Music   { get; set; }
        [JsonPropertyName("unknown")] public List<string>? Unknown { get; set; }
    }
}
