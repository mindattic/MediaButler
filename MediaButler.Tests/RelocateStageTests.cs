using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// End-to-end tests for the library-cleanup relocate pass. Sets up a temp
/// "Movies" or "TV" root, plants a misplaced item, runs the stage, asserts
/// the on-disk shape afterwards.
/// </summary>
[TestFixture]
public class RelocateStageTests
{
    private static MediaButlerSettings SettingsFor(string root, string source, bool dryRun = false) => new()
    {
        SourcePath        = source,
        MoviesDestination = Path.Combine(root, "Movies"),
        TvDestination     = Path.Combine(root, "TV"),
        DryRun            = dryRun,
    };

    [Test]
    public void TvSeason_living_in_MoviesDestination_is_relocated_to_TvDestination()
    {
        using var tmp = new TempDir();
        var movies = Path.Combine(tmp.Path, "Movies");
        var tv     = Path.Combine(tmp.Path, "TV");
        Directory.CreateDirectory(movies);
        Directory.CreateDirectory(tv);

        // Pre-existing correctly-placed movie
        var heat = Path.Combine(movies, "Heat (1995)");
        Directory.CreateDirectory(heat);
        File.WriteAllText(Path.Combine(heat, "heat.mkv"), "fake");

        // The intruder: TV show living in Movies
        var theEnglish = Path.Combine(movies, "The English - Season 01");
        Directory.CreateDirectory(theEnglish);
        File.WriteAllText(Path.Combine(theEnglish, "S01E01.mkv"), "fake");

        var report = new PipelineReport();
        new RelocateStage(SettingsFor(tmp.Path, movies), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(theEnglish),                                                  Is.False, "TV folder should have been evicted from Movies");
            Assert.That(Directory.Exists(Path.Combine(tv, "The English", "Season 01")),                Is.True,  "TV folder should land in TvDestination");
            Assert.That(Directory.Exists(heat),                                                        Is.True,  "correctly-placed movie should be left alone");
            Assert.That(report.TvMoved,                                                                Is.EqualTo(1));
            Assert.That(report.Errors,                                                                 Is.Empty);
        });
    }

    [Test]
    public void Movie_living_in_TvDestination_is_relocated_to_MoviesDestination()
    {
        using var tmp = new TempDir();
        var movies = Path.Combine(tmp.Path, "Movies");
        var tv     = Path.Combine(tmp.Path, "TV");
        Directory.CreateDirectory(movies);
        Directory.CreateDirectory(tv);

        var stray = Path.Combine(tv, "Heat (1995)");
        Directory.CreateDirectory(stray);
        File.WriteAllText(Path.Combine(stray, "heat.mkv"), "fake");

        var report = new PipelineReport();
        new RelocateStage(SettingsFor(tmp.Path, tv), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(stray),                              Is.False);
            Assert.That(Directory.Exists(Path.Combine(movies, "Heat (1995)")), Is.True);
            Assert.That(report.MoviesMoved,                                    Is.EqualTo(1));
        });
    }

    [Test]
    public void Dry_run_does_not_move_anything()
    {
        using var tmp = new TempDir();
        var movies = Path.Combine(tmp.Path, "Movies");
        var tv     = Path.Combine(tmp.Path, "TV");
        Directory.CreateDirectory(movies);
        Directory.CreateDirectory(tv);

        var theEnglish = Path.Combine(movies, "The English - Season 01");
        Directory.CreateDirectory(theEnglish);
        File.WriteAllText(Path.Combine(theEnglish, "S01E01.mkv"), "fake");

        var report = new PipelineReport();
        new RelocateStage(SettingsFor(tmp.Path, movies, dryRun: true), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(theEnglish),                                  Is.True, "dry run must not move the folder");
            Assert.That(Directory.Exists(Path.Combine(tv, "The English", "Season 01")), Is.False);
        });
    }

    [TestCase(@"C:\Media\Movies", @"C:\Media", false)]    // haystack must contain needle
    [TestCase(@"C:\Media",        @"C:\Media\Movies", true)]
    [TestCase(@"C:\Media\Movies", @"C:\Media\Movies", true)]
    [TestCase(@"C:\Media\TV",     @"C:\Media\Movies", false)]
    public void PathContains_matches_self_and_descendants(string haystack, string needle, bool expected) =>
        Assert.That(RelocateStage.PathContains(haystack, needle), Is.EqualTo(expected));

    [Test]
    public void InferExpectedKind_returns_Movie_when_source_is_inside_MoviesDestination()
    {
        var s = new MediaButlerSettings
        {
            SourcePath        = @"C:\Media\Movies",
            MoviesDestination = @"C:\Media\Movies",
            TvDestination     = @"C:\Media\TV",
        };
        Assert.That(RelocateStage.InferExpectedKind(s), Is.EqualTo(MediaKind.Movie));
    }

    [Test]
    public void InferExpectedKind_returns_TvSeason_when_source_is_inside_TvDestination()
    {
        var s = new MediaButlerSettings
        {
            SourcePath        = @"C:\Media\TV",
            MoviesDestination = @"C:\Media\Movies",
            TvDestination     = @"C:\Media\TV",
        };
        Assert.That(RelocateStage.InferExpectedKind(s), Is.EqualTo(MediaKind.TvSeason));
    }

    [Test]
    public void InferExpectedKind_returns_null_for_a_generic_source()
    {
        var s = new MediaButlerSettings
        {
            SourcePath        = @"C:\Media\Torrents",
            MoviesDestination = @"C:\Media\Movies",
            TvDestination     = @"C:\Media\TV",
        };
        Assert.That(RelocateStage.InferExpectedKind(s), Is.Null);
    }
}
