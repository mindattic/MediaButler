using MediaButler.Media;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Filesystem-backed tests for <see cref="MediaScanner"/>. Each test gets a
/// fresh temp directory via <see cref="TempDir"/> so they can mutate the tree
/// without stepping on each other or polluting the user's actual library.
/// </summary>
[TestFixture]
public class MediaScannerTests
{
    private static MediaButlerSettings SettingsFor(string source) => new()
    {
        SourcePath        = source,
        TvDestination     = Path.Combine(source, "_tv_dest"),
        MoviesDestination = Path.Combine(source, "_movies_dest"),
        EnableLlmFallback = false,
    };

    [Test]
    public void Empty_folder_is_classified_as_Empty()
    {
        using var tmp = new TempDir();
        tmp.MakeDir("Empty Show (2008) Season 1-5"); // matches the README's Breaking Bad pitfall

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Kind, Is.EqualTo(MediaKind.Empty));
    }

    [Test]
    public void Folder_with_video_and_year_is_classified_as_Movie()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("Heat (1995) [1080p]");
        File.WriteAllText(Path.Combine(folder, "heat.mkv"), "fake");

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        var item = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.Kind,       Is.EqualTo(MediaKind.Movie));
            Assert.That(item.MovieTitle, Is.EqualTo("Heat"));
            Assert.That(item.MovieYear,  Is.EqualTo(1995));
        });
    }

    [Test]
    public void Folder_with_season_marker_is_classified_as_TvSeason()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("Better.Call.Saul.S05.Complete.1080p.NF.WEBRip.DD+5.1.x264-AJP69");
        File.WriteAllText(Path.Combine(folder, "ep1.mkv"), "fake");

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        var item = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.Kind,         Is.EqualTo(MediaKind.TvSeason));
            Assert.That(item.ShowName,     Is.EqualTo("Better Call Saul"));
            Assert.That(item.SeasonNumber, Is.EqualTo(5));
        });
    }

    [Test]
    public void Folder_with_complete_series_marker_becomes_MultiSeasonParent()
    {
        using var tmp = new TempDir();
        var parent = tmp.MakeDir("Bones Complete Series S1-S12 x264 406p + English Subs (MP4)");
        var s1     = Path.Combine(parent, "Season 1");
        var s2     = Path.Combine(parent, "Season 2");
        Directory.CreateDirectory(s1);
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s1, "ep1.mkv"), "fake");
        File.WriteAllText(Path.Combine(s2, "ep1.mkv"), "fake");

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        var item = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.Kind,         Is.EqualTo(MediaKind.MultiSeasonParent));
            Assert.That(item.ShowName,     Is.EqualTo("Bones"));
            Assert.That(item.Seasons,      Has.Count.EqualTo(2));
            Assert.That(item.Seasons,      Has.Some.Matches<SeasonChild>(c => c.SeasonNumber == 1));
            Assert.That(item.Seasons,      Has.Some.Matches<SeasonChild>(c => c.SeasonNumber == 2));
        });
    }

    [Test]
    public void Structure_only_multi_season_is_detected_without_name_signal()
    {
        // Recreates the "Sherlock" case from the README: parent name doesn't say
        // "Complete Series" but has multiple "Season N" subfolders.
        using var tmp = new TempDir();
        var parent = tmp.MakeDir("Sherlock");
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var sub = Path.Combine(parent, $"Sherlock.Season.{n}.S0{n}.1080p");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "ep1.mkv"), "fake");
        }

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(items[0].Kind,    Is.EqualTo(MediaKind.MultiSeasonParent));
            Assert.That(items[0].Seasons, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void Extras_subfolder_at_root_is_classified_as_Extras()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("The Venture Bros. - Extras");
        File.WriteAllText(Path.Combine(folder, "behind-the-scenes.mkv"), "fake");

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Kind, Is.EqualTo(MediaKind.Extras));
    }

    [Test]
    public void Dotfile_directories_are_skipped_unconditionally()
    {
        using var tmp = new TempDir();
        tmp.MakeDir(".claude");      // claude-code artifact, no videos
        tmp.MakeDir(".stversions");  // syncthing
        var keep = tmp.MakeDir("Okja (2017)");
        File.WriteAllText(Path.Combine(keep, "okja.mkv"), "fake");

        var items = new MediaScanner(SettingsFor(tmp.Path)).Scan().ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].OriginalName, Is.EqualTo("Okja (2017)"));
    }

    [Test]
    public async Task ScanAsync_yields_same_items_as_Scan_when_LLM_disabled()
    {
        using var tmp = new TempDir();
        var f1 = tmp.MakeDir("Heat (1995)");
        File.WriteAllText(Path.Combine(f1, "heat.mkv"), "fake");
        var f2 = tmp.MakeDir("Better.Call.Saul.S05");
        File.WriteAllText(Path.Combine(f2, "ep1.mkv"), "fake");

        var scanner = new MediaScanner(SettingsFor(tmp.Path));
        var sync   = scanner.Scan().ToList();
        var async_ = new List<MediaItem>();
        await foreach (var item in scanner.ScanAsync()) async_.Add(item);

        Assert.That(async_.Select(i => (i.OriginalName, i.Kind)),
            Is.EquivalentTo(sync.Select(i => (i.OriginalName, i.Kind))));
    }

    [Test]
    public void Excluded_folder_names_are_skipped()
    {
        using var tmp = new TempDir();
        var settings = SettingsFor(tmp.Path);
        settings.ExcludedFolders = new[] { ".temp", "incomplete" };

        var temp = tmp.MakeDir(".temp");
        File.WriteAllText(Path.Combine(temp, "leftover.mkv"), "fake");
        var keep = tmp.MakeDir("Okja (2017)");
        File.WriteAllText(Path.Combine(keep, "okja.mkv"), "fake");

        var items = new MediaScanner(settings).Scan().Select(i => i.OriginalName).ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0], Is.EqualTo("Okja (2017)"));
    }
}

/// <summary>Disposable temp directory rooted under the test runner's TempPath.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mediabutler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string MakeDir(string name)
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best effort — Windows holds locks briefly after enumeration */ }
    }
}
