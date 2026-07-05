namespace MediaButler.Tests.Fixtures;

/// <summary>
/// Mirror of the user's REAL inboxes as inventoried on 2026-06-12 (plus the
/// 2026-07-04 re-inventory of <c>M:\Torrents</c>):
/// <c>M:\Torrents</c>, <c>M:\Torrents\temp</c>, <c>D:\Downloads</c>, and
/// <c>D:\Downloads\temp</c>. Every distinct naming variation found on disk is
/// represented (episode lists are trimmed — the shapes matter, not the counts).
///
/// <para>This is the dataset MediaButler's conversion contract is tested
/// against: the pipeline must produce a working conversion for every single
/// file and folder here, or deliberately surface it as needs-manual (true
/// duplicates that require a human pick).</para>
///
/// <para>Variations covered:</para>
/// <list type="bullet">
///   <item>TV "Show Season N (quality)" / "Show - Season N (year)" / "Show Season N Complete ... [grp]"</item>
///   <item>TV dotted+spaced mix with doubled dots ("Better.Call.Saul.Season 3 Complete..720p...")</item>
///   <item>TV "SEASON.04.S04.COMPLETE" redundant markers; zero-padded "Season 07"</item>
///   <item>TV multi-season pack with nested "Season 1" ("Breaking Bad (2008) Season 1-5 S01-S05 ...")</item>
///   <item>TV flat Complete Collection with "S01 - E01 - Title.mkv" files (Killing Eve)</item>
///   <item>TV website-prefix folders "[www.protorrent.co.uk] ..." and "[www...]."</item>
///   <item>TV per-episode torrent folders (Ahsoka/Loki, APEX/NTb/LAZYCUNTS shapes, Sample dirs, nfo junk)</item>
///   <item>TV content-only detection ("Battletech" + "Battle tech - 1.09 - ....avi")</item>
///   <item>TV episode-file shapes: SxxEyy, lowercase sxxeyy, S07e07, E21E22, E25-26, 3x09,
///         scene "202", "Episode 05 - Title", show-with-year ("Kingdom 2019")</item>
///   <item>Movies: scene-dotted (+[TGx]), YTS bracket style with Subs, duplicated year
///         ("Interstellar (2014) (2014)"), title-year override (Blade Runner 2049),
///         RERIP/PROPER, DTS-HD.MA.5.1 dots, "Part.1" titles, [En+Hi] tags</item>
///   <item>Movie multi-pack ("The Matrix 1-4 Pack 1999-2021 ...")</item>
///   <item>Loose movie FILES at the source root + a ".parts" partial-download dotfile</item>
///   <item>2026-07-04: dotted group names (MP4-BEN.THE.MEN), UNRATED/HDR10+/MULTi.FRE.LAT,
///         mixed-case DCPRiP, roman-numeral sequel (Mortal Kombat II), numeric sequel with
///         paren year + iTA tags (The Devil Wears Prada 2), TV season among movies (The Bear S05)</item>
/// </list>
/// </summary>
public sealed class RealWorldLibrary : IDisposable
{
    public string Root { get; }
    public string Torrents { get; }
    public string TorrentsTemp { get; }
    public string Downloads { get; }
    public string DownloadsTemp { get; }
    public string TvDestination { get; }
    public string MoviesDestination { get; }
    public string VariationsPath { get; }

    public RealWorldLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "mediabutler-realworld-" + Guid.NewGuid().ToString("N"));
        Torrents          = Path.Combine(Root, "Torrents");
        TorrentsTemp      = Path.Combine(Torrents, "temp");
        Downloads         = Path.Combine(Root, "Downloads");
        DownloadsTemp     = Path.Combine(Downloads, "temp");
        TvDestination     = Path.Combine(Root, "TV");
        MoviesDestination = Path.Combine(Root, "Movies");
        VariationsPath    = Path.Combine(Root, "variations.json");
        foreach (var d in new[] { Torrents, TorrentsTemp, Downloads, DownloadsTemp, TvDestination, MoviesDestination })
            Directory.CreateDirectory(d);
    }

    /// <summary>The four inbox roots in processing order.</summary>
    public string[] Sources => new[] { Torrents, TorrentsTemp, Downloads, DownloadsTemp };

    /// <summary>Materialize the full real-world tree.</summary>
    public void Populate()
    {
        // ---------------- M:\Torrents ----------------
        Dir(Torrents, "Better Call Saul Season 2 (1080p x265 10bit Joy)",
            "Better Call Saul S02E01 Switch (1080p x265 10bit Joy).mkv",
            "Better Call Saul S02E02 Cobbler (1080p x265 10bit Joy).mkv",
            "Encoded by JoyBell.txt",
            "How to play HEVC (THIS FILE).txt",
            "Ninite K-Lite Codecs Unattended Silent Installer and Updater.website");
        Dir(Torrents, "Fallout - Season 2",
            "Fallout - S02E01 - The Innovator.mkv",
            "Fallout - S02E02 - The Golden Rule.mkv");
        Dir(Torrents, "Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG[TGx]",
            "Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG.mkv",
            "NEW upcoming releases by Xclusive.txt",
            "[TGx]Downloaded from torrentgalaxy.to .txt");
        Dir(Torrents, "The.Devil.Wears.Prada.2006.2160p.WEB-DL.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ",
            "The.Devil.Wears.Prada.2006.2160p.WEB-DL.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ.mkv",
            "RARBG.txt");
        Dir(Torrents, "Weapons (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",
            "Weapons.2025.1080p.WEBRip.x264.AAC5.1-[YTS.MX].mp4",
            "Weapons.2025.1080p.WEBRip.x264.AAC5.1-[YTS.MX].srt",
            "www.YTS.MX.jpg",
            "YTSYifyUP... (TOR).txt",
            @"Subs\English (SDH).eng.srt",
            @"Subs\Español (Latinoamérica).spa.srt");

        // ---- 2026-07-04 re-inventory of M:\Torrents ----
        // New shapes: dotted release-group with dots INSIDE the group name
        // (MP4-BEN.THE.MEN), UNRATED/HDR10+/MULTi.FRE.LAT tag runs, mixed-case
        // DCPRiP, roman-numeral sequel, numeric sequel + paren year + iTA scene
        // tags, and a TV season dropped into a "movies" batch.
        Dir(Torrents, "Obsession.2026.2160p.iT.WEB-DL.UNRATED.DV.HDR10+.MULTi.FRE.LAT.DDP5.1.Atmos.H265.MP4-BEN.THE.MEN",
            "Obsession.2026.2160p.iT.WEB-DL.UNRATED.DV.HDR10+.MULTi[Ben The Men].mp4");
        Dir(Torrents, "Project.Hail.Mary.2026.PROPER.HDR.2160p.WEB.h265-GRACE",
            "Project.Hail.Mary.2026.PROPER.HDR.2160p.WEB.h265-GRACE.mkv",
            "project.hail.mary.2026.proper.hdr.2160p.web.h265-grace.nfo");
        Dir(Torrents, "The.Bear.S05.2160p.DSNP.WEB-DL.DV.HDR.DDP5.1.H265.MP4-BEN.THE.MEN",
            "The.Bear.S05E01.2160p.DSNP.WEB-DL.DV.HDR[Ben The Men].mp4",
            "The.Bear.S05E02.2160p.DSNP.WEB-DL.DV.HDR[Ben The Men].mp4");
        LooseFile(Torrents, "Masters.of.the.Universe.2026.2160p.WEB-DL.DDP5.1.H.265-TGS.mkv");
        LooseFile(Torrents, "Mortal.Kombat.II.2026.1080p.DCPRip.x264-FS.mkv");
        LooseFile(Torrents, "Scary Movie 2026 1080p DCPRiP x264-FS.mkv");
        LooseFile(Torrents, "Star.Wars.The.Mandalorian.And.Grogu.2026.1080p.DCPRiP.x264-FS.mkv");
        LooseFile(Torrents, "The Devil Wears Prada 2 (2026) 2160p H265 HDR DV iTA EnG Sub iTA-MIRCrew.mkv");
        LooseFile(Torrents, "The.Sheep.Detectives.2026.1080p.WEBRip.10Bit.DDP5.1.x265-NeoNoir.mkv");

        // ---------------- M:\Torrents\temp ----------------
        LooseFile(TorrentsTemp, ".b43df67a93863ea91f2f773f00361072da771dd3.parts");
        LooseFile(TorrentsTemp, "Anora 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv");
        LooseFile(TorrentsTemp, "Frankenstein 2025 1080p WEB-DL HEVC x265 5.1 BONE.mkv");
        LooseFile(TorrentsTemp, "Nosferatu 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv");

        Dir(TorrentsTemp, "Akira (1988) (1080p Hybrid x265 HEVC 10bit EAC3 7.1 SAMPA)",
            "Akira.1988.1080p.Hybrid.x265.HEVC.10bit.EAC3.7.1.SAMPA.mkv");
        Dir(TorrentsTemp, "Alien Romulus (2024) [1080p] [WEBRip] [5.1] [YTS.MX]",
            "Alien.Romulus.2024.1080p.WEBRip.x264.AAC5.1-[YTS.MX].mp4");
        Dir(TorrentsTemp, "Better Call Saul - Season 6 (2022)",
            "Better Call Saul - S06E01 - Wine and Roses.mkv",
            "Better Call Saul - S06E02 - Carrot and Stick.mkv");
        Dir(TorrentsTemp, "Better Call Saul Season 3 Complete 720p HDTV x264 [i_c]",
            "Better Call Saul S03E01 Mabel.mkv",
            "Better Call Saul S03E02 Witness.mkv");
        // Same season, second rip — every episode duplicates the [i_c] dump.
        Dir(TorrentsTemp, "Better.Call.Saul.Season 3 Complete..720p.HDTV.x264.[FREDDY1714]",
            "Better.Call.Saul.S03E01.720p.HDTV.x264.[FREDDY1714].mkv",
            "Better.Call.Saul.S03E02.720p.HDTV.x264.[FREDDY1714].mkv");
        Dir(TorrentsTemp, "Blade.Runner.2049.2017.1080p.BluRay.H264.AAC-RARBG",
            "Blade.Runner.2049.2017.1080p.BluRay.H264.AAC-RARBG.mp4",
            @"Subs\Blade.Runner.2049.2017.1080p.BluRay.H264.AAC-RARBG.sub");
        Dir(TorrentsTemp, "Blindspot.SEASON.04.S04.COMPLETE.720p.WEBRip.2CH.x265.HEVC-PSA",
            "Blindspot.S04E01.Hella.Duplicitous.720p.WEBRip.2CH.x265.HEVC-PSA.mkv",
            "Blindspot.S04E21E22.Masters.of.War.1.5.8-The.Gang.Gets.Gone.720p.WEBRip.2CH.x265.HEVC-PSA.mkv",
            "PSArips.com.txt");
        Dir(TorrentsTemp, "Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 HEVC 10bit AAC 5.1 Silence)",
            @"Season 1\Breaking Bad (2008) - S01E01 - Pilot (1080p BluRay x265 Silence).mkv",
            @"Season 1\Breaking Bad (2008) - S01E02 - Cat's in the Bag... (1080p BluRay x265 Silence).mkv");
        Dir(TorrentsTemp, "Criminal Minds Season 07 Complete",
            "Criminal.Minds.S07e07.mp4");
        Dir(TorrentsTemp, "Criminal Minds Season 1 Complete WEB x264 [i_c]",
            "Criminal Minds S01E01 Extreme Aggressor.mkv");
        Dir(TorrentsTemp, "Criminal Minds Season 2",
            "criminal.minds.202.hdtv.xvid-xor.mkv",
            "criminal.minds.204.psychodrama.hdtv_xvid-fov.mkv");
        // Complementary subset of the same season — merges cleanly.
        Dir(TorrentsTemp, "Criminal Minds Season 2 Complete WEB x264 [i_c]",
            "Criminal Minds S02E05 Aftermath.mkv");
        Dir(TorrentsTemp, "Criminal Minds Season 3 Complete WEB x264 [i_c]",
            "Criminal Minds S03E04 Children of the Dark.mkv");
        Dir(TorrentsTemp, "Criminal Minds Season 4 Complete WEB x264 [i_c]",
            "Criminal Minds S04E04 Paradise.mkv");
        Dir(TorrentsTemp, "Criminal Minds Season 5 Complete WEB x264 [i_c]",
            "Criminal Minds S05E01 Nameless, Faceless.avi");
        Dir(TorrentsTemp, "Elementary Season 3 Complete 1080p WEB-DL [rartv]",
            "Elementary.S03E01.1080p.WEB-DL.DD5.1.H.264-Juggalotus.mkv");
        // Full-overlap duplicate of Elementary S3 — must surface as a human pick.
        Dir(TorrentsTemp, "Elementary Season 3 Complete 720p WEB-DL x264 [NOSUB] [i_c]",
            "Elementary S03E01 Enough Nemesis to Go Around.mkv");
        Dir(TorrentsTemp, "Elementary Season 7 Mp4 1080p",
            "Elementary S07E01.mp4",
            "Read Me.txt");
        Dir(TorrentsTemp, "Furiosa A Mad Max Saga (2024) [1080p] [WEBRip] [5.1] [YTS.MX]",
            "Furiosa.A.Mad.Max.Saga.2024.1080p.WEBRip.x264.AAC5.1-[YTS.MX].mp4");
        Dir(TorrentsTemp, "Hot Fuzz (2007) [1080p]",
            "Hot.Fuzz.2007.1080p.BRrip.x264.GAZ.YIFY.mp4");
        Dir(TorrentsTemp, "Interstellar (2014) (2014) [1080p]",
            "Interstellar.2014.2014.1080p.BluRay.x264.YIFY.mp4");
        Dir(TorrentsTemp, "Killing Eve - The Complete Collection (2018-2022)",
            "S01 - E01 - Nice Face.mkv",
            "S02 - E03 - The Hungry Caterpillar.mkv",
            "S04 - E08 - Hello, Losers.mkv");
        Dir(TorrentsTemp, "Kingdom 2019 Season 1 Complete 720p WEB-DL x264 [HARDCODED ENG SUBS] [i_c]",
            "Kingdom S01E01 Episode 1.mkv");
        Dir(TorrentsTemp, "Knives.Out.2019.1080p.BluRay.x264.Atmos.TrueHD7.1-HDChina",
            "Knives.Out.2019.1080p.BluRay.x264.Atmos.TrueHD7.1-HDChina.mkv");
        Dir(TorrentsTemp, "Poor Things (2023) [1080p] [WEBRip] [5.1] [YTS.MX]",
            "Poor.Things.2023.1080p.WEBRip.x264.AAC5.1-[YTS.MX].mp4");
        Dir(TorrentsTemp, "The Gorge (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",
            "The.Gorge.2025.1080p.WEBRip.x264.AAC5.1-[YTS.MX].mp4");
        Dir(TorrentsTemp, "The Matrix 1-4 Pack 1999-2021 REMASTERED 1080p BluRay HEVC x265 5.1 BONE",
            "The Matrix 1999 REMASTERED 1080p BluRay HEVC x265 5.1 BONE.mkv",
            "The Matrix Reloaded 2003 REMASTERED 1080p BluRay HEVC x265 5.1 BONE.mkv",
            "The Matrix Resurrections 2021 1080p BluRay HEVC x265 5.1 BONE.mkv",
            "The Matrix Revolutions 2003 REMASTERED 1080p BluRay HEVC x265 5.1 BONE.mkv");
        Dir(TorrentsTemp, "Tron Ares (2025) [1080p] [WEBRip] [5.1] [YTS.LT]",
            "Tron.Ares.2025.1080p.WEBRip.x264.AAC5.1-[YTS.LT].mp4");
        Dir(TorrentsTemp, "Tron.Legacy.2010.RERIP.PROPER.1080p.BluRay.H264.AAC-LAMA[TGx]",
            "Tron.Legacy.2010.RERIP.PROPER.1080p.BluRay.H264.AAC-LAMA.mp4",
            "Tron.Legacy.2010.RERIP.PROPER.1080p.BluRay.H264.AAC-LAMA.mp4.nfo",
            "[TGx]Downloaded from torrentgalaxy.to .txt");
        Dir(TorrentsTemp, "[www.protorrent.co.uk] Criminal Minds Season 3",
            "Criminal Minds 3x09 Penelope_xvid.avi",
            "Criminal Minds 3x15 A higher power_xvid.avi");
        Dir(TorrentsTemp, "[www.protorrent.co.uk] Criminal Minds Season 6",
            "Episode 05 - Safe Haven.mkv",
            "Episode 09 - Into the Woods.mkv");
        Dir(TorrentsTemp, "[www.protorrent.co.uk].Criminal Minds Season 4",
            "Criminal.Minds.S04E02.mkv",
            "Criminal.Minds.S04E25-26.mkv");

        // ---------------- D:\Downloads ----------------
        Dir(Downloads, "Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]",
            "Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv",
            "NEW upcoming releases by Xclusive.txt",
            "[TGx]Downloaded from torrentgalaxy.to .txt");
        Dir(Downloads, "Ahsoka.S01E02.Part.Two.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]",
            "Ahsoka.S01E02.Part.Two.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv");
        Dir(Downloads, "Ahsoka.S01E03.2160p.DSNP.WEB-DL.DDPA5.1.HDR.HEVC-NTb[TGx]",
            "Ahsoka.S01E03.Part.Three.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb.mkv",
            "Ahsoka.S01E03.2160p.DSNP.WEB-DL.DDPA5.1.HDR.HEVC-NTb.nfo");
        Dir(Downloads, "Ahsoka.S01E04.HDR.2160p.WEB.H265-LAZYCUNTS[TGx]",
            "ahsoka.s01e04.hdr.2160p.web.h265-lazycunts.mkv",
            "ahsoka.s01e04.hdr.2160p.web.h265-lazycunts.nfo",
            @"Sample\ahsoka.s01e04.hdr.2160p.web.h265-lazycunts-sample.mkv");
        Dir(Downloads, "Ahsoka.S01E05.Part.Five.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]",
            "Ahsoka.S01E05.Part.Five.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX.mkv");
        Dir(Downloads, "Loki.S02E01.HDR.2160p.WEB.H265-LAZYCUNTS[TGx]",
            "loki.s02e01.hdr.2160p.web.h265-lazycunts.mkv",
            "loki.s02e01.hdr.2160p.web.h265-lazycunts.nfo",
            @"Sample\loki.s02e01.hdr.2160p.web.h265-lazycunts-sample.mkv");
        Dir(Downloads, "Loki.S02E05.2160p.DSNP.WEB-DL.DDPA5.1.HDR.HEVC-NTb[TGx]",
            "Loki.S02E05.Science-Fiction.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb.mkv",
            "Loki.S02E05.2160p.DSNP.WEB-DL.DDPA5.1.HDR.HEVC-NTb.nfo");
        Dir(Downloads, "Loki.S02E06.HDR.2160p.WEB.H265-TESTPRE[TGx]",
            "loki.s02e06.hdr.2160p.web.h265-testpre.mkv",
            @"Sample\loki.s02e06.hdr.2160p.web.h265-testpre-sample.mkv");

        // ---------------- D:\Downloads\temp ----------------
        // Duplicate rips of episodes already present under D:\Downloads.
        Dir(DownloadsTemp, "Ahsoka.S01E01.HDR.2160p.WEB.H265-LAZYCUNTS[TGx]",
            "ahsoka.s01e01.hdr.2160p.web.h265-lazycunts.mkv");
        Dir(DownloadsTemp, "Ahsoka.S01E02.Part.Two.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb[TGx]",
            "Ahsoka.S01E02.Part.Two.2160p.DSNP.WEB-DL.DDP5.1.HDR.H.265-NTb.mkv");
        Dir(DownloadsTemp, "Battletech",
            "Battle tech - 1.09 - Road To Camelot.avi");
        Dir(DownloadsTemp, "The.Hunger.Games.Catching.Fire.2013.Multi.2160p.UHD.Bluray.x265.HDR.Atmos.7.1.[En+Hi]-DTOne",
            "The.Hunger.Games.Catching.Fire.2013.Multi.2160p.UHD.Bluray.x265.HDR.Atmos.7.1.[En+Hi]-DTOne.mkv");
        Dir(DownloadsTemp, "The.Hunger.Games.Mockingjay.Part.1.2014.Multi.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne",
            "The.Hunger.Games.Mockingjay.Part.1.2014.Multi.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne.mkv");
        Dir(DownloadsTemp, "The.Hunger.Games.Mockingjay.Part.2.2015.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne",
            "The.Hunger.Games.Mockingjay.Part.2.2015.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne.mkv");
    }

    private static void Dir(string root, string folderName, params string[] relativeFiles)
    {
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        foreach (var rel in relativeFiles)
        {
            var path = Path.Combine(folder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fake");
        }
    }

    private static void LooseFile(string root, string fileName) =>
        File.WriteAllText(Path.Combine(root, fileName), "fake");

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }
}
