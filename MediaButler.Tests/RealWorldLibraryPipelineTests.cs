using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Tests.Fixtures;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// The conversion contract against the REAL inbox dataset
/// (<see cref="RealWorldLibrary"/>): every file and folder inventoried from
/// <c>M:\Torrents</c>, <c>M:\Torrents\temp</c>, <c>D:\Downloads</c>, and
/// <c>D:\Downloads\temp</c> must either land at its Plex-canonical destination
/// or surface as a deliberate needs-manual item (true duplicate rips).
///
/// <para>FileBot is not spawned — RenameStage produces FileBot-ready staging
/// folders and MoveStage relocates them; both are pure local IO. The FileBot
/// invocation contract is covered by <c>FileBotClientTests</c>.</para>
/// </summary>
[TestFixture]
public class RealWorldLibraryPipelineTests
{
    private RealWorldLibrary fixture = null!;

    [SetUp]
    public void SetUp()
    {
        fixture = new RealWorldLibrary();
        fixture.Populate();
    }

    [TearDown]
    public void TearDown() => fixture.Dispose();

    private MediaButlerSettings SettingsFor(string source, bool dryRun = false, DuplicateMovieAction? duplicateEpisodeAction = null) => new()
    {
        SourcePath             = source,
        TvDestination          = fixture.TvDestination,
        MoviesDestination      = fixture.MoviesDestination,
        VariationCatalogPath   = fixture.VariationsPath,
        EnableLlmFallback      = false,
        DryRun                 = dryRun,
        DuplicateEpisodeAction = duplicateEpisodeAction ?? DuplicateMovieAction.KeepLargest,
    };

    /// <summary>Run rename + move over all four inboxes, like `mediabutler run` (minus FileBot).</summary>
    private PipelineReport RunLocalPipelineOverAllSources(bool dryRun = false, DuplicateMovieAction? duplicateEpisodeAction = null)
    {
        var report = new PipelineReport();
        foreach (var source in fixture.Sources)
        {
            var s = SettingsFor(source, dryRun, duplicateEpisodeAction);
            new RenameStage(s, report).Run();
            new MoveStage(s, report).Run();
        }
        return report;
    }

    // ---------------------------------------------------------------------
    // Classification of every top-level variation
    // ---------------------------------------------------------------------

    private static readonly (string Source, string Name, MediaKind Kind, string? A, int? Season)[] ExpectedClassifications =
    {
        // Source key: T = Torrents, TT = Torrents\temp, D = Downloads, DT = Downloads\temp
        ("T",  "Better Call Saul Season 2 (1080p x265 10bit Joy)",                MediaKind.TvSeason, "Better Call Saul", 2),
        ("T",  "Fallout - Season 2",                                              MediaKind.TvSeason, "Fallout", 2),
        ("T",  "Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG[TGx]",          MediaKind.Movie, "Oppenheimer", null),
        ("T",  "The.Devil.Wears.Prada.2006.2160p.WEB-DL.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ", MediaKind.Movie, "The Devil Wears Prada", null),
        ("T",  "Weapons (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",                  MediaKind.Movie, "Weapons", null),
        // 2026-07-04 re-inventory
        ("T",  "Obsession.2026.2160p.iT.WEB-DL.UNRATED.DV.HDR10+.MULTi.FRE.LAT.DDP5.1.Atmos.H265.MP4-BEN.THE.MEN", MediaKind.Movie, "Obsession", null),
        ("T",  "Project.Hail.Mary.2026.PROPER.HDR.2160p.WEB.h265-GRACE",          MediaKind.Movie, "Project Hail Mary", null),
        ("T",  "The.Bear.S05.2160p.DSNP.WEB-DL.DV.HDR.DDP5.1.H265.MP4-BEN.THE.MEN", MediaKind.TvSeason, "The Bear", 5),
        // 2026-07-17 — LHOTP reboot; exercises ITA+ENG language codes in show segment (pre-quality)
        ("T",  "Little.House.on.the.Prairie.2026.S01.1080p.NF.WEB-DL.DDP5.1.ENG.Atmos.ITA.H265-TheBlackKing", MediaKind.TvSeason, "Little House on the Prairie", 1),
        ("T",  "Masters.of.the.Universe.2026.2160p.WEB-DL.DDP5.1.H.265-TGS.mkv",  MediaKind.Movie, "Masters of the Universe", null),
        ("T",  "Mortal.Kombat.II.2026.1080p.DCPRip.x264-FS.mkv",                  MediaKind.Movie, "Mortal Kombat II", null),
        ("T",  "Scary Movie 2026 1080p DCPRiP x264-FS.mkv",                       MediaKind.Movie, "Scary Movie", null),
        ("T",  "Star.Wars.The.Mandalorian.And.Grogu.2026.1080p.DCPRiP.x264-FS.mkv", MediaKind.Movie, "Star Wars The Mandalorian And Grogu", null),
        ("T",  "The Devil Wears Prada 2 (2026) 2160p H265 HDR DV iTA EnG Sub iTA-MIRCrew.mkv", MediaKind.Movie, "The Devil Wears Prada 2", null),
        ("T",  "The.Sheep.Detectives.2026.1080p.WEBRip.10Bit.DDP5.1.x265-NeoNoir.mkv", MediaKind.Movie, "The Sheep Detectives", null),

        ("TT", "Akira (1988) (1080p Hybrid x265 HEVC 10bit EAC3 7.1 SAMPA)",        MediaKind.Movie, "Akira", null),
        ("TT", "Alien Romulus (2024) [1080p] [WEBRip] [5.1] [YTS.MX]",            MediaKind.Movie, "Alien Romulus", null),
        ("TT", "Anora 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv",                  MediaKind.Movie, "Anora", null),
        ("TT", "Frankenstein 2025 1080p WEB-DL HEVC x265 5.1 BONE.mkv",           MediaKind.Movie, "Frankenstein", null),
        ("TT", "Furiosa A Mad Max Saga (2024) [1080p] [WEBRip] [5.1] [YTS.MX]",   MediaKind.Movie, "Furiosa A Mad Max Saga", null),
        ("TT", "Nosferatu 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv",              MediaKind.Movie, "Nosferatu", null),
        ("TT", "Poor Things (2023) [1080p] [WEBRip] [5.1] [YTS.MX]",              MediaKind.Movie, "Poor Things", null),
        ("TT", "The Gorge (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",                MediaKind.Movie, "The Gorge", null),
        ("TT", "Tron Ares (2025) [1080p] [WEBRip] [5.1] [YTS.LT]",                MediaKind.Movie, "Tron Ares", null),
        ("TT", "Better Call Saul - Season 6 (2022)",                              MediaKind.TvSeason, "Better Call Saul", 6),
        ("TT", "Better Call Saul Season 3 Complete 720p HDTV x264 [i_c]",         MediaKind.TvSeason, "Better Call Saul", 3),
        ("TT", "Better.Call.Saul.Season 3 Complete..720p.HDTV.x264.[FREDDY1714]", MediaKind.TvSeason, "Better Call Saul", 3),
        ("TT", "Blade.Runner.2049.2017.1080p.BluRay.H264.AAC-RARBG",              MediaKind.Movie, "Blade Runner 2049", null),
        ("TT", "Blindspot.SEASON.04.S04.COMPLETE.720p.WEBRip.2CH.x265.HEVC-PSA",  MediaKind.TvSeason, "Blindspot", 4),
        ("TT", "Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 HEVC 10bit AAC 5.1 Silence)", MediaKind.MultiSeasonParent, "Breaking Bad", null),
        ("TT", "Criminal Minds Season 07 Complete",                               MediaKind.TvSeason, "Criminal Minds", 7),
        ("TT", "Criminal Minds Season 1 Complete WEB x264 [i_c]",                 MediaKind.TvSeason, "Criminal Minds", 1),
        ("TT", "Criminal Minds Season 2",                                         MediaKind.TvSeason, "Criminal Minds", 2),
        ("TT", "Criminal Minds Season 2 Complete WEB x264 [i_c]",                 MediaKind.TvSeason, "Criminal Minds", 2),
        ("TT", "Criminal Minds Season 3 Complete WEB x264 [i_c]",                 MediaKind.TvSeason, "Criminal Minds", 3),
        ("TT", "Criminal Minds Season 4 Complete WEB x264 [i_c]",                 MediaKind.TvSeason, "Criminal Minds", 4),
        ("TT", "Criminal Minds Season 5 Complete WEB x264 [i_c]",                 MediaKind.TvSeason, "Criminal Minds", 5),
        ("TT", "Elementary Season 3 Complete 1080p WEB-DL [rartv]",               MediaKind.TvSeason, "Elementary", 3),
        ("TT", "Elementary Season 3 Complete 720p WEB-DL x264 [NOSUB] [i_c]",     MediaKind.TvSeason, "Elementary", 3),
        ("TT", "Elementary Season 7 Mp4 1080p",                                   MediaKind.TvSeason, "Elementary", 7),
        ("TT", "Hot Fuzz (2007) [1080p]",                                         MediaKind.Movie, "Hot Fuzz", null),
        ("TT", "Interstellar (2014) (2014) [1080p]",                              MediaKind.Movie, "Interstellar", null),
        ("TT", "Killing Eve - The Complete Collection (2018-2022)",               MediaKind.MultiSeasonParent, "Killing Eve", null),
        ("TT", "Kingdom 2019 Season 1 Complete 720p WEB-DL x264 [HARDCODED ENG SUBS] [i_c]", MediaKind.TvSeason, "Kingdom", 1),
        ("TT", "Knives.Out.2019.1080p.BluRay.x264.Atmos.TrueHD7.1-HDChina",       MediaKind.Movie, "Knives Out", null),
        ("TT", "The Matrix 1-4 Pack 1999-2021 REMASTERED 1080p BluRay HEVC x265 5.1 BONE", MediaKind.MoviePack, null, null),
        ("TT", "Tron.Legacy.2010.RERIP.PROPER.1080p.BluRay.H264.AAC-LAMA[TGx]",   MediaKind.Movie, "Tron Legacy", null),
        ("TT", "[www.protorrent.co.uk] Criminal Minds Season 3",                  MediaKind.TvSeason, "Criminal Minds", 3),
        ("TT", "[www.protorrent.co.uk] Criminal Minds Season 6",                  MediaKind.TvSeason, "Criminal Minds", 6),
        ("TT", "[www.protorrent.co.uk].Criminal Minds Season 4",                  MediaKind.TvSeason, "Criminal Minds", 4),

        ("D",  "Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]", MediaKind.TvEpisode, "Ahsoka", 1),
        ("D",  "Ahsoka.S01E03.2160p.DSNP.WEB-DL.DDPA5.1.HDR.HEVC-NTb[TGx]",        MediaKind.TvEpisode, "Ahsoka", 1),
        ("D",  "Ahsoka.S01E04.HDR.2160p.WEB.H265-LAZYCUNTS[TGx]",                  MediaKind.TvEpisode, "Ahsoka", 1),
        ("D",  "Loki.S02E01.HDR.2160p.WEB.H265-LAZYCUNTS[TGx]",                    MediaKind.TvEpisode, "Loki", 2),
        ("D",  "Loki.S02E06.HDR.2160p.WEB.H265-TESTPRE[TGx]",                      MediaKind.TvEpisode, "Loki", 2),

        ("DT", "Battletech",                                                       MediaKind.TvSeason, "Battletech", 1),
        ("DT", "The.Hunger.Games.Catching.Fire.2013.Multi.2160p.UHD.Bluray.x265.HDR.Atmos.7.1.[En+Hi]-DTOne", MediaKind.Movie, "The Hunger Games Catching Fire", null),
        ("DT", "The.Hunger.Games.Mockingjay.Part.1.2014.Multi.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne", MediaKind.Movie, "The Hunger Games Mockingjay Part 1", null),
        ("DT", "The.Hunger.Games.Mockingjay.Part.2.2015.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne", MediaKind.Movie, "The Hunger Games Mockingjay Part 2", null),
    };

    [Test]
    public void Scanner_classifies_every_real_world_variation_as_expected()
    {
        var bySource = new Dictionary<string, Dictionary<string, MediaItem>>
        {
            ["T"]  = ScanOf(fixture.Torrents),
            ["TT"] = ScanOf(fixture.TorrentsTemp),
            ["D"]  = ScanOf(fixture.Downloads),
            ["DT"] = ScanOf(fixture.DownloadsTemp),
        };

        Assert.Multiple(() =>
        {
            foreach (var (src, name, kind, showOrTitle, season) in ExpectedClassifications)
            {
                Assert.That(bySource[src].ContainsKey(name), Is.True, $"scanner missed: {name}");
                if (!bySource[src].TryGetValue(name, out var item)) continue;
                Assert.That(item.Kind, Is.EqualTo(kind), $"{name}: kind");
                if (kind is MediaKind.TvSeason or MediaKind.TvEpisode)
                {
                    Assert.That(item.ShowName, Is.EqualTo(showOrTitle), $"{name}: show");
                    Assert.That(item.SeasonNumber, Is.EqualTo(season), $"{name}: season");
                }
                else if (kind == MediaKind.Movie && showOrTitle is not null)
                {
                    Assert.That(item.MovieTitle, Is.EqualTo(showOrTitle), $"{name}: title");
                }
            }
        });
    }

    private Dictionary<string, MediaItem> ScanOf(string source) =>
        new MediaScanner(SettingsFor(source)).Scan().ToDictionary(i => i.OriginalName, i => i);

    [Test]
    public void Dot_parts_partial_download_file_is_ignored_entirely()
    {
        var items = ScanOf(fixture.TorrentsTemp);
        Assert.That(items.Keys, Has.None.Contains(".parts"),
            "qBittorrent .parts dotfiles must never be classified or touched");
    }

    [Test]
    public void Matrix_pack_is_split_into_four_distinct_movies()
    {
        var items = ScanOf(fixture.TorrentsTemp);
        var pack = items["The Matrix 1-4 Pack 1999-2021 REMASTERED 1080p BluRay HEVC x265 5.1 BONE"];
        Assert.That(pack.PackMovies.Select(p => $"{p.Title} ({p.Year})"), Is.EquivalentTo(new[]
        {
            "The Matrix (1999)",
            "The Matrix Reloaded (2003)",
            "The Matrix Resurrections (2021)",
            "The Matrix Revolutions (2003)",
        }));
    }

    [Test]
    public void Killing_Eve_flat_collection_carries_parsed_loose_episodes()
    {
        var items = ScanOf(fixture.TorrentsTemp);
        var ke = items["Killing Eve - The Complete Collection (2018-2022)"];
        Assert.Multiple(() =>
        {
            Assert.That(ke.ShowName, Is.EqualTo("Killing Eve"));
            Assert.That(ke.LooseEpisodes.Select(e => (e.SeasonNumber, e.EpisodeNumber)),
                Is.EquivalentTo(new[] { (1, 1), (2, 3), (4, 8) }));
        });
    }

    // ---------------------------------------------------------------------
    // Full local pipeline: every item converts or is deliberately flagged
    // ---------------------------------------------------------------------

    [Test]
    public void Full_local_pipeline_lands_every_tv_season_at_plex_canonical_paths()
    {
        RunLocalPipelineOverAllSources();

        var expectedSeasons = new (string Show, int Season, string[] Files)[]
        {
            ("Ahsoka", 1, new[]
            {
                "Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv",
                "Ahsoka.S01E02.Part.Two.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv",
                "Ahsoka.S01E03.Part.Three.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb.mkv",
                "ahsoka.s01e04.hdr.2160p.web.h265-lazycunts.mkv",
                "Ahsoka.S01E05.Part.Five.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv",
            }),
            ("Loki", 2, new[]
            {
                "loki.s02e01.hdr.2160p.web.h265-lazycunts.mkv",
                "Loki.S02E05.Science-Fiction.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb.mkv",
                "loki.s02e06.hdr.2160p.web.h265-testpre.mkv",
            }),
            ("Battletech", 1, new[] { "Battle tech - 1.09 - Road To Camelot.avi" }),
            ("The Bear", 5, new[]
            {
                "The.Bear.S05E01.2160p.DSNP.WEB-DL.DV.HDR[Ben The Men].mp4",
                "The.Bear.S05E02.2160p.DSNP.WEB-DL.DV.HDR[Ben The Men].mp4",
            }),
            ("Better Call Saul", 2, new[] { "Better Call Saul S02E01 Switch (1080p x265 10bit Joy).mkv" }),
            ("Better Call Saul", 3, new[] { "Better Call Saul S03E01 Mabel.mkv", "Better Call Saul S03E02 Witness.mkv" }),
            ("Better Call Saul", 6, new[] { "Better Call Saul - S06E01 - Wine and Roses.mkv" }),
            ("Blindspot", 4, new[]
            {
                "Blindspot.S04E01.Hella.Duplicitous.720p.WEBRip.2CH.x265.HEVC-PSA.mkv",
                "Blindspot.S04E21E22.Masters.of.War.1.5.8-The.Gang.Gets.Gone.720p.WEBRip.2CH.x265.HEVC-PSA.mkv",
            }),
            ("Breaking Bad", 1, new[] { "Breaking Bad (2008) - S01E01 - Pilot (1080p BluRay x265 Silence).mkv" }),
            ("Criminal Minds", 1, new[] { "Criminal Minds S01E01 Extreme Aggressor.mkv" }),
            // Scene-code dump + complementary [i_c] subset merged into ONE season.
            ("Criminal Minds", 2, new[]
            {
                "criminal.minds.202.hdtv.xvid-xor.mkv",
                "criminal.minds.204.psychodrama.hdtv_xvid-fov.mkv",
                "Criminal Minds S02E05 Aftermath.mkv",
            }),
            // [i_c] E04 + protorrent 3x09/3x15 merged (no episode overlap).
            ("Criminal Minds", 3, new[]
            {
                "Criminal Minds S03E04 Children of the Dark.mkv",
                "Criminal Minds 3x09 Penelope_xvid.avi",
                "Criminal Minds 3x15 A higher power_xvid.avi",
            }),
            ("Criminal Minds", 4, new[]
            {
                "Criminal Minds S04E04 Paradise.mkv",
                "Criminal.Minds.S04E02.mkv",
                "Criminal.Minds.S04E25-26.mkv",
            }),
            ("Criminal Minds", 5, new[] { "Criminal Minds S05E01 Nameless, Faceless.avi" }),
            ("Criminal Minds", 6, new[] { "Episode 05 - Safe Haven.mkv", "Episode 09 - Into the Woods.mkv" }),
            ("Criminal Minds", 7, new[] { "Criminal.Minds.S07e07.mp4" }),
            ("Elementary", 3, new[] { "Elementary.S03E01.1080p.WEB-DL.DD5.1.H.264-Juggalotus.mkv" }),
            ("Elementary", 7, new[] { "Elementary S07E01.mp4" }),
            ("Fallout", 2, new[] { "Fallout - S02E01 - The Innovator.mkv" }),
            ("Killing Eve", 1, new[] { "S01 - E01 - Nice Face.mkv" }),
            ("Killing Eve", 2, new[] { "S02 - E03 - The Hungry Caterpillar.mkv" }),
            ("Killing Eve", 4, new[] { "S04 - E08 - Hello, Losers.mkv" }),
            ("Kingdom", 1, new[] { "Kingdom S01E01 Episode 1.mkv" }),
        };

        Assert.Multiple(() =>
        {
            foreach (var (show, season, files) in expectedSeasons)
            {
                var seasonDir = PlexStandard.TvSeasonPath(fixture.TvDestination, show, season);
                Assert.That(Directory.Exists(seasonDir), Is.True, $"missing season dir: {seasonDir}");
                if (!Directory.Exists(seasonDir)) continue;
                foreach (var f in files)
                    Assert.That(File.Exists(Path.Combine(seasonDir, f)), Is.True,
                        $"missing episode: {show} S{season:D2} \\ {f}");
            }
        });
    }

    [Test]
    public void Full_local_pipeline_lands_every_movie_at_plex_canonical_paths()
    {
        RunLocalPipelineOverAllSources();

        var expectedMovies = new[]
        {
            "Oppenheimer (2023)",
            "The Devil Wears Prada (2006)",
            "Weapons (2025)",
            "Akira (1988)",
            "Alien Romulus (2024)",
            "Blade Runner 2049 (2017)",
            "Furiosa A Mad Max Saga (2024)",
            "Hot Fuzz (2007)",
            "Interstellar (2014)",          // duplicated year collapsed
            "Knives Out (2019)",
            "Poor Things (2023)",
            "The Gorge (2025)",
            "Tron Ares (2025)",
            "Tron Legacy (2010)",
            "Anora (2024)",                 // loose root file, wrapped + renamed
            "Frankenstein (2025)",          // loose root file, wrapped + renamed
            "Nosferatu (2024)",             // loose root file, wrapped + renamed
            "The Matrix (1999)",            // pack split x4
            "The Matrix Reloaded (2003)",
            "The Matrix Resurrections (2021)",
            "The Matrix Revolutions (2003)",
            "The Hunger Games Catching Fire (2013)",
            "The Hunger Games Mockingjay Part 1 (2014)",
            "The Hunger Games Mockingjay Part 2 (2015)",
            // 2026-07-04 re-inventory
            "Obsession (2026)",
            "Project Hail Mary (2026)",
            "Masters of the Universe (2026)",                // loose root file, wrapped + renamed
            "Mortal Kombat II (2026)",                       // roman-numeral sequel survives cleaning
            "Scary Movie (2026)",
            "Star Wars The Mandalorian And Grogu (2026)",
            "The Devil Wears Prada 2 (2026)",                // numeric sequel + paren year + iTA tags
            "The Sheep Detectives (2026)",
        };

        Assert.Multiple(() =>
        {
            foreach (var m in expectedMovies)
            {
                var dir = Path.Combine(fixture.MoviesDestination, m);
                Assert.That(Directory.Exists(dir), Is.True, $"missing movie dir: {m}");
                if (Directory.Exists(dir))
                    Assert.That(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(),
                        Is.True, $"movie dir is empty: {m}");
            }
        });
    }

    [Test]
    public void True_duplicate_rips_stay_behind_and_are_flagged_for_a_human()
    {
        // MB-A9 made KeepLargest the default for TV episodes too (see
        // DuplicateEpisodeActionTests); this test exercises the Flag opt-out,
        // which restores the original MB-LAW-9 leave-both-and-ask behaviour.
        var report = RunLocalPipelineOverAllSources(duplicateEpisodeAction: DuplicateMovieAction.Flag);

        Assert.Multiple(() =>
        {
            // FREDDY1714's BCS S3 duplicates every [i_c] episode — must remain and be flagged.
            var freddy = Directory.EnumerateDirectories(fixture.TorrentsTemp)
                .Select(Path.GetFileName)
                .Where(n => n!.Contains("FREDDY1714"))
                .ToList();
            Assert.That(freddy, Is.Not.Empty, "fully-duplicate BCS S3 rip must not vanish");

            // Elementary S3 720p duplicates the 1080p E01 — one of the two stays.
            var elementaryLeftover = Directory.EnumerateDirectories(fixture.TorrentsTemp)
                .Select(Path.GetFileName)
                .Where(n => n!.StartsWith("Elementary Season 3", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.That(elementaryLeftover, Is.Not.Empty, "duplicate Elementary S3 rip must not vanish");

            // Downloads\temp Ahsoka episodes duplicate Downloads episodes 1 and 2.
            Assert.That(report.NeedsManual, Is.Not.Empty,
                "duplicate episodes must surface in the needs-manual list");

            // And the duplicates were NOT silently double-filed into the destination.
            var ahsokaSeason = PlexStandard.TvSeasonPath(fixture.TvDestination, "Ahsoka", 1);
            var e01Count = Directory.EnumerateFiles(ahsokaSeason)
                .Count(f => NameParser.ParseEpisodeNumberInSeason(Path.GetFileName(f), 1) == 1);
            Assert.That(e01Count, Is.EqualTo(1), "episode 1 must exist exactly once at the destination");
        });
    }

    [Test]
    public void Junk_and_sample_shells_are_cleaned_up_after_consolidation()
    {
        RunLocalPipelineOverAllSources();

        Assert.Multiple(() =>
        {
            // Per-episode shells (nfo/txt/Sample) are gone from D:\Downloads.
            Assert.That(Directory.EnumerateDirectories(fixture.Downloads)
                    .Select(Path.GetFileName)
                    .Where(n => n!.StartsWith("Ahsoka", StringComparison.OrdinalIgnoreCase) ||
                                n.StartsWith("Loki", StringComparison.OrdinalIgnoreCase)),
                Is.Empty, "per-episode torrent shells must be deleted after consolidation");

            // No sample file may reach the destination.
            Assert.That(Directory.EnumerateFiles(fixture.TvDestination, "*", SearchOption.AllDirectories)
                    .Where(f => NameParser.IsSampleName(Path.GetFileName(f))),
                Is.Empty, "sample clips must never travel to the library");

            // The Matrix pack shell is gone.
            Assert.That(Directory.Exists(Path.Combine(fixture.TorrentsTemp,
                "The Matrix 1-4 Pack 1999-2021 REMASTERED 1080p BluRay HEVC x265 5.1 BONE")), Is.False);
        });
    }

    [Test]
    public void Dry_run_over_all_sources_mutates_nothing_anywhere()
    {
        var before = Snapshot();
        RunLocalPipelineOverAllSources(dryRun: true);
        var after = Snapshot();

        Assert.That(after, Is.EqualTo(before), "dry-run must not touch any source or destination");
    }

    private string[] Snapshot() =>
        Directory.EnumerateFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("variations.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    [Test]
    public void Reruns_never_touch_destinations_and_sources_converge_to_a_steady_state()
    {
        // Run 1 converts everything convertible. Stuck duplicate rips may
        // still settle into their canonical staging name on run 2 (their
        // target name frees up once run 1's move completes) — but the
        // DESTINATIONS must never change after run 1, and the whole tree must
        // be a strict no-op from run 2 onward.
        RunLocalPipelineOverAllSources();
        var destAfter1 = DestinationSnapshot();

        var report2 = RunLocalPipelineOverAllSources();
        var destAfter2 = DestinationSnapshot();
        var fullAfter2 = Snapshot();

        var report3 = RunLocalPipelineOverAllSources();
        var fullAfter3 = Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(destAfter2, Is.EqualTo(destAfter1), "re-run mutated the destination libraries");
            Assert.That(fullAfter3, Is.EqualTo(fullAfter2), "third run must be a strict no-op");
            Assert.That(report2.Errors, Is.Empty, "re-run produced errors");
            Assert.That(report3.Errors, Is.Empty, "steady-state run produced errors");
        });
    }

    private string[] DestinationSnapshot() =>
        Directory.EnumerateFileSystemEntries(fixture.TvDestination, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFileSystemEntries(fixture.MoviesDestination, "*", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
}
