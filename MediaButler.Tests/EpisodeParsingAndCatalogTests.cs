using MediaButler.Media;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Unit coverage for the episode-marker parser and sample detection added for
/// the real-world dataset (per-episode folders, flat collections, scene codes).
/// </summary>
[TestFixture]
public class EpisodeParsingTests
{
    [TestCase("Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]", "Ahsoka", 1, 1, null)]
    [TestCase("ahsoka.s01e04.hdr.2160p.web.h265-lazycunts.mkv", "ahsoka", 1, 4, null)]
    [TestCase("Loki.S02E06.HDR.2160p.WEB.H265-TESTPRE[TGx]", "Loki", 2, 6, null)]
    [TestCase("Better Call Saul S02E01 Switch (1080p x265 10bit Joy).mkv", "Better Call Saul", 2, 1, null)]
    [TestCase("Criminal.Minds.S07e07.mp4", "Criminal Minds", 7, 7, null)]
    [TestCase("Blindspot.S04E21E22.Masters.of.War.720p.mkv", "Blindspot", 4, 21, 22)]
    [TestCase("Criminal.Minds.S04E25-26.mkv", "Criminal Minds", 4, 25, 26)]
    [TestCase("S01 - E01 - Nice Face.mkv", "", 1, 1, null)]
    [TestCase("Criminal Minds 3x09 Penelope_xvid.avi", "Criminal Minds", 3, 9, null)]
    [TestCase("Battle tech - 1.09 - Road To Camelot.avi", "Battle tech", 1, 9, null)]
    public void ParseEpisode_handles_every_real_world_marker_shape(
        string input, string show, int season, int episode, int? episodeEnd)
    {
        var ep = NameParser.ParseEpisode(input);
        Assert.That(ep, Is.Not.Null, input);
        Assert.Multiple(() =>
        {
            Assert.That(ep!.Show, Is.EqualTo(show), $"{input}: show");
            Assert.That(ep.Season, Is.EqualTo(season), $"{input}: season");
            Assert.That(ep.Episode, Is.EqualTo(episode), $"{input}: episode");
            Assert.That(ep.EpisodeEnd, Is.EqualTo(episodeEnd), $"{input}: episode end");
        });
    }

    [TestCase("Frankenstein 2025 1080p WEB-DL HEVC x265 5.1 BONE.mkv")] // 5.1 audio is not 5x01
    [TestCase("The.Hunger.Games.Mockingjay.Part.1.2014.Multi.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.mkv")] // Part.1 + years
    [TestCase("Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG.mkv")]
    [TestCase("The.Devil.Wears.Prada.2006.2160p.WEB-DL.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ")]
    public void ParseEpisode_does_not_false_positive_on_movie_names(string input) =>
        Assert.That(NameParser.ParseEpisode(input), Is.Null, input);

    [TestCase("criminal.minds.202.hdtv.xvid-xor.mkv", 2, 2)]
    [TestCase("criminal.minds.221.hr.hdtv.xvid-ctu.mkv", 2, 21)]
    [TestCase("Episode 05 - Safe Haven.mkv", 6, 5)]
    [TestCase("Criminal Minds 3x09 Penelope_xvid.avi", 3, 9)]
    [TestCase("Elementary S03E01 Bella.mkv", 3, 1)]
    public void ParseEpisodeNumberInSeason_resolves_context_only_shapes(string file, int season, int expected) =>
        Assert.That(NameParser.ParseEpisodeNumberInSeason(file, season), Is.EqualTo(expected));

    [Test]
    public void ParseEpisodeNumberInSeason_rejects_an_episode_from_another_season() =>
        Assert.That(NameParser.ParseEpisodeNumberInSeason("Show.S05E01.mkv", season: 3), Is.Null);

    [TestCase("ahsoka.s01e04.hdr.2160p.web.h265-lazycunts-sample.mkv", true)]
    [TestCase("Sample.mkv", true)]
    [TestCase("sample-show.s01e01.mkv", true)]
    [TestCase("A Sample of Things S01E01.mkv", true)]  // token-based: callers pair with a size guard
    [TestCase("Samples of the Year 2020.mkv", false)]  // "Samples" is not the token "sample"
    [TestCase("Ahsoka.S01E01.mkv", false)]
    public void IsSampleName_detects_sample_tokens(string name, bool expected) =>
        Assert.That(NameParser.IsSampleName(name), Is.EqualTo(expected));

    [TestCase("Killing Eve - The Complete Collection (2018-2022)", true)]
    [TestCase("Bones Complete Series S1-S12", true)]
    [TestCase("Criminal Minds Season 2 Complete WEB x264 [i_c]", false)] // single season, "Complete" alone
    public void Complete_collection_marks_multi_season(string name, bool expected) =>
        Assert.That(NameParser.LooksLikeMultiSeason(name), Is.EqualTo(expected));
}

/// <summary>
/// The persistent naming-variation catalog: records what each run saw,
/// honours hand-edits as classification pins, never clobbers a broken file.
/// </summary>
[TestFixture]
public class VariationCatalogTests
{
    private string path = null!;

    [SetUp]
    public void SetUp() =>
        path = Path.Combine(Path.GetTempPath(), "mb-varcat-" + Guid.NewGuid().ToString("N") + ".json");

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Test]
    public void Records_classified_names_into_sections_and_persists()
    {
        var cat = VariationCatalog.Load(path);
        cat.Record("Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG[TGx]", MediaKind.Movie);
        cat.Record("Fallout - Season 2", MediaKind.TvSeason);
        cat.Record("Ahsoka.S01E01.HDR.2160p.WEB.H265[TGx]", MediaKind.TvEpisode);
        cat.Record("WeirdFolder", MediaKind.Unknown);
        cat.Save();

        var reloaded = VariationCatalog.Load(path);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Movies, Does.Contain("Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG[TGx]"));
            Assert.That(reloaded.Tv, Does.Contain("Fallout - Season 2"));
            Assert.That(reloaded.Tv, Does.Contain("Ahsoka.S01E01.HDR.2160p.WEB.H265[TGx]"));
            Assert.That(reloaded.Unknown, Does.Contain("WeirdFolder"));
        });
    }

    [Test]
    public void Recording_a_name_twice_does_not_duplicate_it()
    {
        var cat = VariationCatalog.Load(path);
        Assert.That(cat.Record("Some Movie (2020)", MediaKind.Movie), Is.True);
        Assert.That(cat.Record("Some Movie (2020)", MediaKind.Movie), Is.False);
        Assert.That(cat.Record("some movie (2020)", MediaKind.TvSeason), Is.False, "case-insensitive dedupe");
    }

    [Test]
    public void Hand_edited_sections_pin_classification_hints()
    {
        File.WriteAllText(path, """{ "movie": ["Pinned As Movie"], "tv": ["Pinned As TV"], "music": ["Some Album"], "unknown": ["Just Cataloged"] }""");
        var cat = VariationCatalog.Load(path);
        Assert.Multiple(() =>
        {
            Assert.That(cat.LookupHint("Pinned As Movie"), Is.EqualTo(VariationCatalog.Hint.Movie));
            Assert.That(cat.LookupHint("pinned as tv"), Is.EqualTo(VariationCatalog.Hint.Tv), "lookup is case-insensitive");
            Assert.That(cat.LookupHint("Some Album"), Is.EqualTo(VariationCatalog.Hint.Music));
            Assert.That(cat.LookupHint("Just Cataloged"), Is.Null, "unknown section never pins");
            Assert.That(cat.LookupHint("Never Seen"), Is.Null);
        });
    }

    [Test]
    public void Corrupted_file_disables_saving_so_user_edits_survive()
    {
        File.WriteAllText(path, "{ this is not valid json ");
        var cat = VariationCatalog.Load(path);
        Assert.That(cat.LoadFailed, Is.True);
        cat.Record("New Thing (2024)", MediaKind.Movie);
        cat.Save();
        Assert.That(File.ReadAllText(path), Is.EqualTo("{ this is not valid json "),
            "a broken (hand-edited) catalog must never be overwritten");
    }

    [Test]
    public void Scanner_consults_music_pins_so_a_music_folder_is_not_deleted_as_empty()
    {
        File.WriteAllText(path, """{ "music": ["Pink Floyd - Discography"] }""");
        var tmp = Path.Combine(Path.GetTempPath(), "mb-varcat-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(tmp, "Pink Floyd - Discography");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "track01.weird"), "not a known audio ext");

            var settings = new MediaButlerSettings
            {
                SourcePath = tmp,
                TvDestination = Path.Combine(tmp, "_tv"),
                MoviesDestination = Path.Combine(tmp, "_movies"),
                VariationCatalogPath = path,
            };
            var item = new MediaScanner(settings).Scan().Single();
            Assert.That(item.Kind, Is.EqualTo(MediaKind.Music),
                "a music-pinned folder must classify Music, not Empty (the delete path)");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Test]
    public void Audio_only_folder_is_detected_as_music_without_any_pin()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mb-audio-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(tmp, "Artist - Album (2003) [FLAC]");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "01 - Track.flac"), "fake");

            var settings = new MediaButlerSettings
            {
                SourcePath = tmp,
                TvDestination = Path.Combine(tmp, "_tv"),
                MoviesDestination = Path.Combine(tmp, "_movies"),
                VariationCatalogPath = path,
            };
            var item = new MediaScanner(settings).Scan().Single();
            Assert.That(item.Kind, Is.EqualTo(MediaKind.Music));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
