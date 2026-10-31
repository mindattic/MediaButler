using System.Text.Json;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// <see cref="PipelineRunner"/> clones the settings once per source before running a stage
/// (MB-T28). A property the clone forgets is silently reset to its default for every pipeline
/// run, which is how the TVDB/TMDB id overrides stopped reaching FileBot.
/// </summary>
[TestFixture]
public class PipelineRunnerCloneTests
{
    [Test]
    public void Clone_keeps_tvdb_and_tmdb_id_overrides()
    {
        var s = new MediaButlerSettings
        {
            TvdbIdOverrides = { ["Kians Bizarre B and B"] = "453851" },
            TmdbIdOverrides = { ["Heat"] = "949" },
        };

        var clone = PipelineRunner.Clone(s);

        Assert.Multiple(() =>
        {
            Assert.That(clone.GetTvdbIdOverride("kians bizarre b and b"), Is.EqualTo("453851"));
            Assert.That(clone.GetTmdbIdOverride("heat"), Is.EqualTo("949"));
        });
    }

    [Test]
    public void Clone_copies_every_setting_except_recursive()
    {
        // Recursive is deliberately cleared: sources are already expanded before cloning.
        var s = NonDefaultSettings();
        s.Recursive = false;

        var clone = PipelineRunner.Clone(s);

        Assert.That(JsonSerializer.Serialize(clone), Is.EqualTo(JsonSerializer.Serialize(s)));
    }

    /// <summary>Every property set to something other than its default.</summary>
    internal static MediaButlerSettings NonDefaultSettings() => new()
    {
        SourcePath = @"X:\In",
        ExtraSources = [@"X:\In2"],
        TvDestination = @"X:\TV",
        MoviesDestination = @"X:\Movies",
        MusicDestination = @"X:\Music",
        Recursive = true,
        FileBotPath = @"X:\FileBot\filebot.exe",
        FileBotTrustAll = true,
        SubtitleLanguage = "de",
        EnableSubtitles = true,
        RenameEpisodes = false,
        RenameMovies = false,
        FetchArtwork = false,
        DryRun = true,
        Limit = 7,
        NoGuard = true,
        EnableLlmFallback = true,
        LlmProvider = "openai",
        ExcludedFolders = ["skip"],
        VideoExtensions = [".mkv"],
        EmptyDeleteSafetyBytes = 42,
        AudioExtensions = [".mp3"],
        DuplicateMovieAction = DuplicateMovieAction.Flag,
        DuplicateEpisodeAction = DuplicateMovieAction.Flag,
        SampleMaxBytes = 43,
        SubtitleExtensions = [".srt"],
        VariationCatalogPath = @"X:\variations.json",
        TitleYearOverrides = ["2049"],
        TvdbIdOverrides = { ["Show"] = "1" },
        TmdbIdOverrides = { ["Movie"] = "2" },
    };
}
