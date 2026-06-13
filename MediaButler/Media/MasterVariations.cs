namespace MediaButler.Media;

/// <summary>
/// The hardcoded master list of media naming-format variations, compiled from
/// scene release rules (scenerules.org BluRay/x265/TV/MP3/FLAC), Plex,
/// Jellyfin, and Kodi naming documentation, TRaSH Guides (Radarr/Sonarr
/// recommended schemes), FileBot format docs, anime fansub conventions, and
/// the warez Standard reference — plus every variation inventoried from the
/// user's real inboxes.
///
/// <para>The on-disk variation catalog
/// (<c>%APPDATA%\MindAttic\MediaButler\variations.json</c>) is created as a
/// CLONE of this list and then grows: every scan appends newly-discovered
/// names, and the user can hand-append entries (moving one into
/// <c>movie</c>/<c>tv</c>/<c>music</c> pins its category). Upgrading
/// MediaButler merges any new master entries into the existing file without
/// disturbing user additions.</para>
/// </summary>
public static class MasterVariations
{
    public static readonly string[] Movie =
    [
        // Scene / P2P release shapes (sources, codecs, audio, HDR, editions)
        "Movie.Title.2019.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.1080p.BluRay.x265-GROUP",
        "Movie.Title.2019.720p.BluRay.x264-GROUP",
        "Movie.Title.2019.480p.BluRay.XviD-GROUP",
        "Movie.Title.2019.2160p.UHD.BluRay.x265-GROUP",
        "Movie.Title.2019.1080p.WEB-DL.H264-GROUP",
        "Movie.Title.2019.1080p.WEB.x264-GROUP",
        "Movie.Title.2019.720p.WEBRip.x264-GROUP",
        "Movie.Title.2019.1080p.WEBRip.x265-GROUP",
        "Movie.Title.2019.1080p.AMZN.WEB-DL.x264-GROUP",
        "Movie.Title.2019.1080p.NF.WEB-DL.x264-GROUP",
        "Movie.Title.2019.1080p.HULU.WEB-DL.x264-GROUP",
        "Movie.Title.2019.1080p.DSNP.WEB-DL.x264-GROUP",
        "Movie.Title.2019.1080p.ATVP.WEB-DL.x265-GROUP",
        "Movie.Title.2019.720p.HDTV.x264-GROUP",
        "Movie.Title.2019.720p.HDRip.x264-GROUP",
        "Movie.Title.2019.DVDRip.XviD-GROUP",
        "Movie.Title.2019.DVDScr.x264-GROUP",
        "Movie.Title.2019.BDRip.x265-GROUP",
        "Movie.Title.2019.1080p.REMUX.AVC.DTS-HD.MA-GROUP",
        "Movie.Title.2019.2160p.REMUX.HEVC.TrueHD.Atmos-GROUP",
        "Movie.Title.2019.Hybrid.REMUX.2160p.HEVC.TrueHD.7.1.Atmos-GROUP",
        "Movie.Title.2019.CAM.x264-GROUP",
        "Movie.Title.2019.TS.x264-GROUP",
        "Movie.Title.2019.HDCAM.x264-GROUP",
        "Movie.Title.2019.REMASTERED.1080p.BluRay.x265-GROUP",
        "Movie.Title.2019.1080p.BluRay.DTS-HD.MA.5.1.x264-GROUP",
        "Movie.Title.2019.1080p.BluRay.TrueHD.Atmos.7.1.x264-GROUP",
        "Movie.Title.2019.1080p.BluRay.DD5.1.x264-GROUP",
        "Movie.Title.2019.1080p.WEB-DL.AAC2.0.x264-GROUP",
        "Movie.Title.2019.2160p.BluRay.HDR10.x265-GROUP",
        "Movie.Title.2019.2160p.BluRay.HDR10Plus.HEVC-GROUP",
        "Movie.Title.2019.2160p.BluRay.DV.x265-GROUP",
        "Movie.Title.2019.2160p.WEB-DL.DV.HDR10.x265-GROUP",
        "Movie.Title.2019.2160p.REMUX.HEVC.DV.HDR10Plus.TrueHD.Atmos.7.1-GROUP",
        "Movie.Title.2019.EXTENDED.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.UNRATED.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.Directors.Cut.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.THEATRICAL.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.IMAX.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.LIMITED.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.REMASTERED.PROPER.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.REPACK.1080p.WEB-DL.x264-GROUP",
        "Movie.Title.2019.RERIP.720p.BluRay.x264-GROUP",
        "Movie.Title.2019.INTERNAL.1080p.BluRay.x265-GROUP",
        "Movie.Title.2019.COMPLETE.BLURAY-GROUP",
        "Movie.Title.2019.MULTi.COMPLETE.BLURAY-GROUP",
        "Movie.Title.2019.FRENCH.1080p.WEB-DL.x264-GROUP",
        "Movie.Title.2019.GERMAN.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.1080p.BluRay.x264.CD1-GROUP",
        "Movie.Title.2019.Part.1.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.3D.HSBS.1080p.BluRay.x264-GROUP",
        "Movie.Title.2019.UNCUT.1080p.BluRay.x264-GROUP",
        // Library / curated shapes
        "Movie Title (2019)",
        "Movie Title (2019) [1080p]",
        "Movie Title (2019) [2160p] [4K] [BluRay] [5.1] [YTS.MX]",
        "Movie Title (2019) [imdbid-tt1234567]",
        "Movie Title (2019) {tmdb-345691}",
        "Movie Title (2019) - Directors Cut.mkv",
        "Movie Title (2019) - [Criterion Collection].mkv",
        // Real-inbox shapes (inventoried 2026-06-12)
        "Oppenheimer.2023.1080p.BluRay.DD5.1.x264-GalaxyRG[TGx]",
        "The.Devil.Wears.Prada.2006.2160p.WEB-DL.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ",
        "Weapons (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",
        "Blade.Runner.2049.2017.1080p.BluRay.H264.AAC-RARBG",
        "Knives.Out.2019.1080p.BluRay.x264.Atmos.TrueHD7.1-HDChina",
        "Tron.Legacy.2010.RERIP.PROPER.1080p.BluRay.H264.AAC-LAMA[TGx]",
        "Interstellar (2014) (2014) [1080p]",
        "The Matrix 1-4 Pack 1999-2021 REMASTERED 1080p BluRay HEVC x265 5.1 BONE",
        "The.Hunger.Games.Mockingjay.Part.1.2014.Multi.2160p.UHD.BluRay.x265.HDR.Atmos.7.1.[En+Hi]-DTOne",
        "Frankenstein 2025 1080p WEB-DL HEVC x265 5.1 BONE.mkv",
        // 2026-06-12 dry-run additions
        "Akira (1988) (1080p Hybrid x265 HEVC 10bit EAC3 7.1 SAMPA)",
        "Anora 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv",
        "Furiosa A Mad Max Saga (2024) [1080p] [WEBRip] [5.1] [YTS.MX]",
        "Nosferatu 2024 1080p WEB-DL HEVC x265 5.1 BONE.mkv",
        "Poor Things (2023) [1080p] [WEBRip] [5.1] [YTS.MX]",
        "The Gorge (2025) [1080p] [WEBRip] [5.1] [YTS.MX]",
        "Three Flavours Cornetto Trilogy REMASTERED Shaun Of The Dead 2004, Hot Fuzz 2007, The Worlds End 2013 1080p (Multi) BluRay HEVC H265 5.1 BONE",
        "Tron Ares (2025) [1080p] [WEBRip] [5.1] [YTS.LT]",
    ];

    public static readonly string[] Tv =
    [
        // Scene / P2P episode shapes
        "Weekly.TV.Show.S01E01.720p.HDTV.x264-GROUP",
        "Weekly.TV.Show.S01E01.1080p.AMZN.WEB-DL.x264-GROUP",
        "Weekly.TV.Show.S01E01.Episode.Title.720p.HDTV.x264-GROUP",
        "Weekly.TV.Show.S01E01E02.720p.HDTV.x264-GROUP",
        "Weekly.TV.Show.S01E01-E03.1080p.WEB-DL.x265-GROUP",
        "Weekly.TV.Show.S01E01.PROPER.720p.HDTV.x264-GROUP",
        "Weekly.TV.Show.S01E01.REPACK.1080p.WEB-DL.x264-GROUP",
        "Weekly.TV.Show.US.S01E01.720p.HDTV.x264-GROUP",
        "Weekly.TV.Show.S01E01.2160p.WEB-DL.DV.HDR10.HEVC-GROUP",
        "Weekly.TV.Show.S01E00.Special.Title.720p.HDTV.x264-GROUP",
        "Miniseries.Show.Name.Part.1.Episode.Title.720p.WEB-DL.x264-GROUP",
        "Daily.TV.Show.2024.01.15.720p.HDTV.x264-GROUP",
        "Daily.TV.Show.2024-01-15.1080p.WEB-DL.x264-GROUP",
        "Show.Name.1x01.720p.HDTV.x264-GROUP",
        "show.name.s01e01.720p.hdtv.x264-group",
        "Show.Name.S01.Complete.1080p.WEB-DL.x264-GROUP",
        "Show.Name.2024.S01E01.720p.WEB-DL.x264-GROUP",
        // Library / curated shapes
        "Show Name (2010) - S01E01 - Episode Title.mkv",
        "Show Name - Season 01",
        "Show Name (2010) {tvdb-123456}",
        "Show Name - 1x01 - Episode Title.avi",
        "Show Name - s02e18-e19 - Episode Title.mkv",
        // Anime fansub shapes
        "[SubsPlease] Anime Title - 90 (1080p) [4B8B1261].mkv",
        "[Erai-raws] Anime Title - 01 [1080p CR WEB-DL AVC AAC][MultiSub][457B1F06].mkv",
        "[HorribleSubs] Anime Title - 01 [720p].mkv",
        "[Group]_Anime_Title_-_01_[1280x720_XviD_MP3][3E16AF40].mkv",
        "Anime Title - 001 - Episode Title [Group][720p][ABCD1234].mkv",
        // Real-inbox shapes (inventoried 2026-06-12)
        "Better Call Saul Season 2 (1080p x265 10bit Joy)",
        "Better Call Saul - Season 6 (2022)",
        "Better Call Saul Season 3 Complete 720p HDTV x264 [i_c]",
        "Better.Call.Saul.Season 3 Complete..720p.HDTV.x264.[FREDDY1714]",
        "Blindspot.SEASON.04.S04.COMPLETE.720p.WEBRip.2CH.x265.HEVC-PSA",
        "Breaking Bad (2008) Season 1-5 S01-S05 (1080p BluRay x265 HEVC 10bit AAC 5.1 Silence)",
        "Criminal Minds Season 07 Complete",
        "Killing Eve - The Complete Collection (2018-2022)",
        "Kingdom 2019 Season 1 Complete 720p WEB-DL x264 [HARDCODED ENG SUBS] [i_c]",
        "[www.protorrent.co.uk] Criminal Minds Season 3",
        "Ahsoka.S01E01.Part.One.2160p.WEB-DL.DDP5.1.Atmos.H.265-APEX[TGx]",
        "Loki.S02E06.HDR.2160p.WEB.H265-TESTPRE[TGx]",
        "Elementary Season 7 Mp4 1080p",
        "Battle tech - 1.09 - Road To Camelot.avi",
        "S01 - E01 - Episode Title.mkv",
        "Episode 05 - Episode Title.mkv",
        "criminal.minds.202.hdtv.xvid-xor.mkv",
        "Criminal Minds 3x09 Episode Title_xvid.avi",
        "Criminal.Minds.S04E25-26.mkv",
        // 2026-06-12 dry-run additions
        "[www.protorrent.co.uk] Criminal Minds Season 6",
        "[www.protorrent.co.uk].Criminal Minds Season 4",
        "Criminal Minds Season 2",
        "Criminal Minds Season 3 Complete WEB x264 [i_c]",
        "Criminal Minds Season 4 Complete WEB x264 [i_c]",
        "Criminal Minds Season 5 Complete WEB x264 [i_c]",
        "Elementary Season 3 Complete 1080p WEB-DL [rartv]",
        "Elementary Season 3 Complete 720p WEB-DL x264 [NOSUB] [i_c]",
        "Fallout - Season 2",
    ];

    public static readonly string[] Music =
    [
        // Scene release shapes (MP3/FLAC rules)
        "Artist-Album_Title-2021-GROUP",
        "Artist-Album_Title-WEB-2021-GROUP",
        "Artist-Album_Title-CD-2021-GROUP",
        "Artist-Album_Title-2CD-2021-GROUP",
        "Artist-Album_Title-LP-2021-GROUP",
        "Artist-Album_Title-VINYL-2021-GROUP",
        "Artist-Album_Title-CDS-2021-GROUP",
        "Artist-Album_Title-CDM-2021-GROUP",
        "Artist-Album_Title-CDEP-2021-GROUP",
        "Artist-Album_Title-WEB-SINGLE-2021-GROUP",
        "Artist-Album_Title-SACD-2021-GROUP",
        "Artist-Album_Title-TAPE-2021-GROUP",
        "Artist-Album_Title-FM-2021-GROUP",
        "Artist-Album_Title-SBD-2021-GROUP",
        "Artist-Album_Title-BOOTLEG-VINYL-2021-GROUP",
        "VA-Compilation_Title-2021-GROUP",
        "VA-Movie_Title-OST-WEB-2021-GROUP",
        "Artist-Album_Title-WEB-FLAC-2021-GROUP",
        "Artist-Album_Title-CD-FLAC-2021-GROUP",
        "Artist-Album_Title-VINYL-FLAC-2021-GROUP",
        "Artist-Album_Title-Deluxe_Edition-WEB-FLAC-2021-GROUP",
        "Artist-Album_Title-Remastered-CD-FLAC-2021-GROUP",
        "Artist-Album_Title-Promo-WEB-FLAC-2021-GROUP",
        "Artist-EP_Title-EP-WEB-FLAC-2021-GROUP",
        "Artist-Live_Album_Title-LIVE-FLAC-2021-GROUP",
        "Artist-Discography-1990-2020-GROUP",
        // Library layouts (folder-per-album)
        "Artist - Album Title (2021)",
        "Album Title (2021) [FLAC]",
        "Artist - Album Title (2003) [MP3 320]",
        "Artist - Album Title (2003) [V0]",
        "Artist - Album Title (2003) [ALAC]",
        "Various Artists - Compilation Title (2003)",
        "[1984] Album Title",
        "Artist - Album Title - Disc 1",
        "01 - Track Title.flac",
        "Artist Name - 01 - Track Title.mp3",
    ];
}
