using MediaButler.FileBot;
using MediaButler.Llm;
using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests.Lamp;

/// <summary>
/// LAMP (MB-T27): one test per MediaButler setting, each proving it end to end — today's value in
/// the user's settings.json imports into the right property, is named
/// <c>MindAttic:Settings:MediaButler:{name}</c> in the manifest and in Vault's configuration tree,
/// round-trips through the store, and is the value the consuming code actually uses.
/// </summary>
[TestFixture]
public class MediaButlerSettingTests : LampTestBase
{
    private PipelineReport report = null!;

    [SetUp]
    public void SetUpReport() => report = new PipelineReport();

    private string MovieInSource(string folder = "Heat (1995)", string? root = null)
    {
        var dir = Path.Combine(root ?? Source, folder);
        MakeFile(dir, folder + ".mkv", 2000);
        return dir;
    }

    private string SeasonInSource(string folder = "Better.Call.Saul.S05.1080p")
    {
        var dir = Path.Combine(Source, folder);
        MakeFile(dir, "Better.Call.Saul.S05E01.mkv", 2000);
        return dir;
    }

    // ---- Folders --------------------------------------------------------------------------

    [Test]
    public void SourcePath_imports_and_is_the_folder_the_move_stage_empties()
    {
        var inbox = Tmp.MakeDir("Inbox");
        var s = ImportAndRoundTrip("sourcePath", inbox);
        var movie = MovieInSource(root: inbox);

        new MoveStage(s, report).Run();

        Assert.That(Directory.Exists(movie), Is.False, "the movie in the configured source should have moved");
        Assert.That(Directory.Exists(Path.Combine(Movies, "Heat (1995)")), Is.True);
    }

    [Test]
    public void ExtraSources_imports_and_is_processed_after_the_source()
    {
        var extra = Tmp.MakeDir("Extra Inbox");
        var s = ImportAndRoundTrip("extraSources", new[] { extra });

        Assert.That(PipelineRunner.EffectiveSources(s), Is.EqualTo(new[] { Source, extra }));
    }

    [Test]
    public void Recursive_imports_and_turns_container_subfolders_into_inboxes()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Source, "temp")).FullName;
        var s = ImportAndRoundTrip("recursive", true);

        Assert.That(PipelineRunner.EffectiveSources(s), Is.EqualTo(new[] { Source, temp }));
    }

    [Test]
    public void ExcludedFolders_imports_and_the_scanner_skips_them()
    {
        var s = ImportAndRoundTrip("excludedFolders", new[] { "holding" });
        MovieInSource("holding");
        MovieInSource();

        var names = new MediaScanner(s).Scan().Select(i => i.OriginalName).ToList();

        Assert.That(names, Does.Not.Contain("holding"));
        Assert.That(names, Does.Contain("Heat (1995)"));
    }

    [Test]
    public void TvDestination_imports_and_is_where_seasons_move()
    {
        var tv2 = Tmp.MakeDir("TV2");
        var s = ImportAndRoundTrip("tvDestination", tv2);
        SeasonInSource();

        new MoveStage(s, report).Run();

        Assert.That(Directory.EnumerateFiles(tv2, "*.mkv", SearchOption.AllDirectories).Count(), Is.EqualTo(1));
    }

    [Test]
    public void MoviesDestination_imports_and_is_where_movies_move()
    {
        var movies2 = Tmp.MakeDir("Movies2");
        var s = ImportAndRoundTrip("moviesDestination", movies2);
        MovieInSource();

        new MoveStage(s, report).Run();

        Assert.That(Directory.Exists(Path.Combine(movies2, "Heat (1995)")), Is.True);
    }

    [Test]
    public void MusicDestination_imports_and_is_where_music_moves()
    {
        var music = Tmp.MakeDir("Music");
        var s = ImportAndRoundTrip("musicDestination", music);
        MakeFile(Path.Combine(Source, "Some Album"), "01 Track.mp3", 500);

        new MoveStage(s, report).Run();

        Assert.That(File.Exists(Path.Combine(music, "Some Album", "01 Track.mp3")), Is.True);
    }

    // ---- FileBot --------------------------------------------------------------------------

    [Test]
    public void FileBotPath_imports_and_is_the_exe_FileBot_stages_run()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("fileBotPath", fake.ExePath);
        SeasonInSource();

        var client = FileBotClient.TryCreate(s);
        Assert.That(client!.ExePath, Is.EqualTo(fake.ExePath));
        new FileBotStage(s, client, report, new SubtitleCredentials()).RunTv();

        Assert.That(fake.Log, Does.Contain("-rename"));
    }

    [Test]
    public void FileBotTrustAll_imports_and_FileBot_runs_with_trust_all_certs()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("fileBotTrustAll", true);
        s.FileBotPath = fake.ExePath;

        var client = FileBotClient.TryCreate(s)!;
        client.Run("-version");

        Assert.That(client.TrustAll, Is.True);
        Assert.That(fake.Log, Does.Contain("-Dtrust.all.certs=true"));
    }

    [Test]
    public void RenameEpisodes_imports_and_off_means_no_TheTVDB_rename()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("renameEpisodes", false);
        s.FileBotPath = fake.ExePath;
        s.DryRun = true;
        SeasonInSource();

        new FileBotStage(s, FileBotClient.TryCreate(s)!, report, new SubtitleCredentials()).RunTv();

        Assert.That(fake.Log, Does.Not.Contain("TheTVDB"));
    }

    [Test]
    public void RenameMovies_imports_and_off_means_no_TheMovieDB_rename()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("renameMovies", false);
        s.FileBotPath = fake.ExePath;
        s.DryRun = true;
        MovieInSource();

        new FileBotStage(s, FileBotClient.TryCreate(s)!, report, new SubtitleCredentials()).RunMovies();

        Assert.That(fake.Log, Does.Not.Contain("TheMovieDB"));
    }

    [Test]
    public void FetchArtwork_imports_and_off_means_no_artwork_script()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("fetchArtwork", false);
        s.FileBotPath = fake.ExePath;
        SeasonInSource();

        new FileBotStage(s, FileBotClient.TryCreate(s)!, report, new SubtitleCredentials()).RunTv();

        Assert.That(fake.Log, Does.Contain("TheTVDB"), "the stage did run");
        Assert.That(fake.Log, Does.Not.Contain("-script"));
    }

    [Test]
    public void TvdbIdOverrides_imports_and_reaches_FileBot_as_q_through_a_pipeline_run()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("tvdbIdOverrides", new Dictionary<string, string> { ["Better Call Saul"] = "273181" });
        s.FileBotPath = fake.ExePath;
        s.DryRun = true;
        SeasonInSource();

        new PipelineRunner(new SettingsService()).RunFileBotTv(s);

        Assert.That(fake.Log, Does.Contain("--q 273181"));
    }

    [Test]
    public void TmdbIdOverrides_imports_and_reaches_FileBot_as_q_through_a_pipeline_run()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("tmdbIdOverrides", new Dictionary<string, string> { ["Heat"] = "949" });
        s.FileBotPath = fake.ExePath;
        s.DryRun = true;
        MovieInSource();

        new PipelineRunner(new SettingsService()).RunFileBotMovies(s);

        Assert.That(fake.Log, Does.Contain("--q 949"));
    }

    // ---- Subtitles ------------------------------------------------------------------------

    [Test]
    public void EnableSubtitles_imports_and_on_makes_the_FileBot_stage_fetch_subtitles()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("enableSubtitles", true);
        s.FileBotPath = fake.ExePath;
        MovieInSource();

        new FileBotStage(s, FileBotClient.TryCreate(s)!, report, new SubtitleCredentials()).Run();

        Assert.That(fake.Log, Does.Contain("-get-subtitles"));
    }

    [Test]
    public void SubtitleLanguage_imports_and_is_passed_to_FileBot_as_lang()
    {
        var fake = new FakeFileBot(Tmp.MakeDir("FileBot"));
        var s = ImportAndRoundTrip("subtitleLanguage", "de");
        s.FileBotPath = fake.ExePath;
        MovieInSource();

        new FileBotStage(s, FileBotClient.TryCreate(s)!, report, new SubtitleCredentials()).RunSubtitles();

        Assert.That(fake.Log, Does.Contain("--lang de"));
    }

    [Test]
    public void SubtitleExtensions_imports_and_those_sidecars_travel_in_a_season_merge()
    {
        var s = ImportAndRoundTrip("subtitleExtensions", new[] { ".vtt" });
        var incoming = Path.Combine(Source, "Show.S01");
        MakeFile(incoming, "Show.S01E02.mkv");
        MakeFile(incoming, "Show.S01E02.vtt");
        var target = Tmp.MakeDir("Show - Season 01");

        SeasonMerger.MergeFiles(incoming, target, 1, s, report);

        Assert.That(File.Exists(Path.Combine(target, "Show.S01E02.vtt")), Is.True);
    }

    // ---- Safety ---------------------------------------------------------------------------

    [Test]
    public void DryRun_imports_and_on_means_the_move_stage_moves_nothing()
    {
        var s = ImportAndRoundTrip("dryRun", true);
        var movie = MovieInSource();

        new MoveStage(s, report).Run();

        Assert.That(Directory.Exists(movie), Is.True);
        Assert.That(Directory.EnumerateFileSystemEntries(Movies), Is.Empty);
    }

    [Test]
    public void Limit_imports_and_caps_the_items_a_stage_processes()
    {
        var s = ImportAndRoundTrip("limit", 1);
        MovieInSource("Heat (1995)");
        MovieInSource("Ronin (1998)");

        new MoveStage(s, report).Run();

        Assert.That(Directory.EnumerateDirectories(Movies).Count(), Is.EqualTo(1));
    }

    [Test]
    public void NoGuard_imports_and_lets_a_run_use_a_source_inside_a_destination()
    {
        var s = ImportAndRoundTrip("noGuard", true);
        s.SourcePath = Directory.CreateDirectory(Path.Combine(Movies, "inbox")).FullName;
        var runner = new PipelineRunner(new SettingsService());

        var guarded = new MediaButlerSettings { SourcePath = s.SourcePath, TvDestination = Tv, MoviesDestination = Movies, VariationCatalogPath = Catalog };
        Assert.That(runner.RunRename(guarded), Is.EqualTo(PipelineRunner.ExitErrors), "without NoGuard the overlap is refused");
        Assert.That(runner.RunRename(s), Is.Not.EqualTo(PipelineRunner.ExitErrors));
    }

    [Test]
    public void EmptyDeleteSafetyBytes_imports_and_is_the_junk_floor_for_deleting_a_shell()
    {
        var s = ImportAndRoundTrip("emptyDeleteSafetyBytes", 10L);
        var shell = Path.Combine(Source, "Old.Shell");
        MakeFile(shell, "notes.txt", 100);

        var deleted = SeasonMerger.TryDeleteShell(shell, s, out var reason);

        Assert.That(deleted, Is.False, "100 bytes of junk is over a 10-byte floor (the 1 MB default would delete it)");
        Assert.That(reason, Does.Contain("non-video"));
    }

    [Test]
    public void SampleMaxBytes_imports_and_is_the_ceiling_for_junk_sample_clips()
    {
        var s = ImportAndRoundTrip("sampleMaxBytes", 10L);
        var shell = Path.Combine(Source, "Old.Shell");
        MakeFile(shell, "Show.S01E01-sample.mkv", 100);

        var deleted = SeasonMerger.TryDeleteShell(shell, s, out var reason);

        Assert.That(deleted, Is.False);
        Assert.That(reason, Does.Contain("SampleMaxBytes"));
    }

    [Test]
    public void DuplicateMovieAction_imports_and_Flag_leaves_both_copies()
    {
        var s = ImportAndRoundTrip("duplicateMovieAction", DuplicateMovieAction.Flag);
        var incoming = MovieInSource();
        MakeFile(Path.Combine(Movies, "Heat (1995)"), "Heat (1995).mkv", 500);

        new MoveStage(s, report).Run();

        Assert.That(Directory.Exists(incoming), Is.True, "Flag keeps the incoming copy for review");
        Assert.That(new FileInfo(Path.Combine(Movies, "Heat (1995)", "Heat (1995).mkv")).Length, Is.EqualTo(500));
        Assert.That(report.NeedsManual, Is.Not.Empty);
    }

    [Test]
    public void DuplicateEpisodeAction_imports_and_Flag_leaves_both_episode_copies()
    {
        var s = ImportAndRoundTrip("duplicateEpisodeAction", DuplicateMovieAction.Flag);
        var incoming = Path.Combine(Source, "Show.S01");
        MakeFile(incoming, "Show.S01E01.mkv", 2000);
        var target = Tmp.MakeDir("Show - Season 01");
        MakeFile(target, "Show - S01E01 - Pilot.mkv", 500);

        var result = SeasonMerger.MergeFiles(incoming, target, 1, s, report);

        Assert.That(result.Conflicts, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(incoming, "Show.S01E01.mkv")), Is.True);
    }

    // ---- File types -----------------------------------------------------------------------

    [Test]
    public void VideoExtensions_imports_and_decides_what_the_scanner_calls_video()
    {
        var s = ImportAndRoundTrip("videoExtensions", new[] { ".xyz" });
        MakeFile(Path.Combine(Source, "Heat (1995)"), "heat.xyz");

        var item = new MediaScanner(s).Scan().Single();

        Assert.That(item.Kind, Is.EqualTo(MediaKind.Movie));
    }

    [Test]
    public void AudioExtensions_imports_and_decides_what_the_scanner_calls_music()
    {
        var s = ImportAndRoundTrip("audioExtensions", new[] { ".zzz" });
        MakeFile(Path.Combine(Source, "Some Album"), "01 Track.zzz");

        var item = new MediaScanner(s).Scan().Single();

        Assert.That(item.Kind, Is.EqualTo(MediaKind.Music));
    }

    // ---- Name parsing ---------------------------------------------------------------------

    [Test]
    public void TitleYearOverrides_imports_and_keeps_the_year_in_the_title()
    {
        var s = ImportAndRoundTrip("titleYearOverrides", new[] { "Zebra 2049" });
        MakeFile(Path.Combine(Source, "Zebra.2049.2017.1080p"), "zebra.mkv");

        var item = new MediaScanner(s).Scan().Single();

        Assert.That(item.MovieTitle, Is.EqualTo("Zebra 2049"));
        Assert.That(item.MovieYear, Is.EqualTo(2017));
    }

    [Test]
    public void VariationCatalogPath_imports_and_is_where_the_scanner_records_names()
    {
        var catalog = Path.Combine(Tmp.Path, "catalogs", "mine.json");
        var s = ImportAndRoundTrip("variationCatalogPath", catalog);
        MovieInSource();

        new MediaScanner(s).Scan().ToList();

        Assert.That(VariationCatalog.ResolvePath(s), Is.EqualTo(catalog));
        Assert.That(File.ReadAllText(catalog), Does.Contain("Heat (1995)"));
    }

    [Test]
    public void EnableLlmFallback_imports_and_on_sends_unparseable_names_to_the_LLM()
    {
        var s = ImportAndRoundTrip("enableLlmFallback", true);
        var calls = 0;
        var parser = new LegionFallbackParser(s, (_, _, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult("""{"kind":"movie","title":"Heat","year":1995}""");
        });

        var guess = parser.ClassifyAsync("zzqx", new[] { "a.mkv" }).GetAwaiter().GetResult();

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(guess!.Title, Is.EqualTo("Heat"));
    }

    [Test]
    public void LlmProvider_imports_and_is_the_provider_the_fallback_calls()
    {
        var s = ImportAndRoundTrip("llmProvider", "openai");
        s.EnableLlmFallback = true;
        string? provider = null;
        var parser = new LegionFallbackParser(s, (p, _, _, _, _, _) =>
        {
            provider = p;
            return Task.FromResult("{}");
        });

        parser.ClassifyAsync("zzqx", new[] { "a.mkv" }).GetAwaiter().GetResult();

        Assert.That(provider, Is.EqualTo("openai"));
    }

    /// <summary>Guard: the per-setting tests above cover every property of the settings class.</summary>
    [Test]
    public void Every_setting_has_its_own_test()
    {
        var tested = typeof(MediaButlerSettingTests).GetMethods()
            .Where(m => m.IsDefined(typeof(TestAttribute), false))
            .Select(m => m.Name.Split('_')[0])
            .ToHashSet();
        var settings = typeof(MediaButlerSettings).GetProperties().Select(p => p.Name);

        Assert.That(settings.Where(n => !tested.Contains(n)), Is.Empty, "settings without their own end-to-end test");
    }
}
