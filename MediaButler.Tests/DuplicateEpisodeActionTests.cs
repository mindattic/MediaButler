using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// The duplicate-episode policy (MB-A9): when a TV season merge finds an
/// incoming episode whose name or parsed episode number already exists at the
/// destination season folder, <see cref="DuplicateMovieAction.KeepLargest"/>
/// (the default) keeps whichever copy has the larger video and deletes the
/// other, mirroring <see cref="DuplicateMovieActionTests"/>;
/// <see cref="DuplicateMovieAction.Flag"/> restores the classic MB-LAW-9
/// leave-both-and-ask behaviour. Born from the real 2026-08-07 run: "A Knight
/// Of The Seven Kingdoms" S01 re-arrived and every episode collided with an
/// already-filed copy, needing a manual pick for all 6.
/// </summary>
[TestFixture]
public class DuplicateEpisodeActionTests
{
    private TempDir tmp = null!;
    private string source = null!;
    private string tv = null!;
    private MediaButlerSettings settings = null!;

    [SetUp]
    public void SetUp()
    {
        tmp    = new TempDir();
        source = tmp.MakeDir("Torrents");
        tv     = tmp.MakeDir("TV");
        settings = new MediaButlerSettings
        {
            SourcePath           = source,
            TvDestination        = tv,
            MoviesDestination    = tmp.MakeDir("Movies"),
            VariationCatalogPath = System.IO.Path.Combine(tmp.Path, "variations.json"),
            EnableLlmFallback    = false,
        };
    }

    [TearDown]
    public void TearDown() => tmp.Dispose();

    /// <summary>
    /// Seed an incoming season-01 dump at the source and an already-filed
    /// Season 01 at the destination, both holding the same single episode
    /// under different release names (so name AND parsed-episode collide).
    /// </summary>
    private (string Incoming, string ExistingSeason, string ExistingFile) Seed(int incomingBytes, int existingBytes)
    {
        var incoming = System.IO.Path.Combine(source, "Ted Lasso - Season 01");
        Directory.CreateDirectory(incoming);
        var incomingFile = System.IO.Path.Combine(incoming, "Ted Lasso - S01E01 - Pilot.mkv");
        File.WriteAllBytes(incomingFile, new byte[incomingBytes]);

        var existingSeason = System.IO.Path.Combine(tv, "Ted Lasso", "Season 01");
        Directory.CreateDirectory(existingSeason);
        var existingFile = System.IO.Path.Combine(existingSeason, "Ted Lasso - S01E01 - Pilot (alt).mkv");
        File.WriteAllBytes(existingFile, new byte[existingBytes]);

        return (incoming, existingSeason, existingFile);
    }

    private PipelineReport Run()
    {
        var report = new PipelineReport();
        new MoveStage(settings, report).Run();
        return report;
    }

    [Test]
    public void KeepLargest_incoming_larger_replaces_the_existing_episode_video()
    {
        var (incoming, existingSeason, existingFile) = Seed(incomingBytes: 2000, existingBytes: 500);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(existingFile), Is.False, "the smaller existing video must be deleted");
            var winner = Directory.GetFiles(existingSeason, "*.mkv").Single();
            Assert.That(new FileInfo(winner).Length, Is.EqualTo(2000), "the larger incoming video must win");
            Assert.That(Directory.Exists(incoming), Is.False, "the emptied incoming shell must be deleted");
            Assert.That(report.NeedsManual, Is.Empty, "an auto-resolved duplicate needs no human");
            Assert.That(report.Errors, Is.Empty);
        });
    }

    [Test]
    public void KeepLargest_incoming_smaller_is_discarded_and_the_destination_untouched()
    {
        var (incoming, existingSeason, existingFile) = Seed(incomingBytes: 500, existingBytes: 2000);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(existingFile), Is.True, "the larger existing video must stay");
            Assert.That(new FileInfo(existingFile).Length, Is.EqualTo(2000));
            Assert.That(Directory.GetFiles(existingSeason, "*.mkv"), Has.Length.EqualTo(1),
                "the losing incoming copy must not also land at the destination");
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.NeedsManual, Is.Empty, "an auto-resolved duplicate needs no human");
        });
    }

    [Test]
    public void Flag_leaves_both_copies_and_surfaces_needs_manual()
    {
        settings.DuplicateEpisodeAction = DuplicateMovieAction.Flag;
        var (incoming, existingSeason, existingFile) = Seed(incomingBytes: 2000, existingBytes: 500);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(System.IO.Path.Combine(incoming, "Ted Lasso - S01E01 - Pilot.mkv")), Is.True,
                "Flag must never touch the incoming copy");
            Assert.That(File.Exists(existingFile), Is.True, "Flag must never touch the existing copy");
            Assert.That(new FileInfo(existingFile).Length, Is.EqualTo(500));
            Assert.That(report.NeedsManual, Is.Not.Empty);
        });
    }

    [TestCase("keep-largest", DuplicateMovieAction.KeepLargest)]
    [TestCase("KeepLargest",  DuplicateMovieAction.KeepLargest)]
    [TestCase("flag",         DuplicateMovieAction.Flag)]
    public void TvDuplicates_cli_flag_overlays_the_persisted_setting(string flag, DuplicateMovieAction expected)
    {
        var s = new MediaButlerSettings { DuplicateEpisodeAction = DuplicateMovieAction.Flag };
        if (expected == DuplicateMovieAction.Flag) s.DuplicateEpisodeAction = DuplicateMovieAction.KeepLargest;
        new Commands.BaseSettings { TvDuplicates = flag }.ApplyTo(s);
        Assert.That(s.DuplicateEpisodeAction, Is.EqualTo(expected));
    }

    [Test]
    public void TvDuplicates_cli_flag_rejects_unknown_values()
    {
        var s = new MediaButlerSettings();
        Assert.Throws<InvalidOperationException>(
            () => new Commands.BaseSettings { TvDuplicates = "newest" }.ApplyTo(s));
    }

    [TestCase(2000, 500)]
    [TestCase(500, 2000)]
    public void KeepLargest_dry_run_mutates_nothing_in_either_direction(int incomingBytes, int existingBytes)
    {
        settings.DryRun = true;
        var (incoming, existingSeason, existingFile) = Seed(incomingBytes, existingBytes);
        var incomingFile = System.IO.Path.Combine(incoming, "Ted Lasso - S01E01 - Pilot.mkv");

        Run();

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(incomingFile).Length, Is.EqualTo(incomingBytes), "dry-run must not touch the incoming copy");
            Assert.That(new FileInfo(existingFile).Length, Is.EqualTo(existingBytes), "dry-run must not touch the existing copy");
        });
    }

    [Test]
    public void Non_conflicting_episodes_still_merge_normally_alongside_a_resolved_duplicate()
    {
        // Partial-season scenario: incoming brings a new episode (no conflict)
        // plus a re-rip of an episode the destination already has (conflict,
        // auto-resolved). Both must be handled independently in one pass.
        var (incoming, existingSeason, existingFile) = Seed(incomingBytes: 2000, existingBytes: 500);
        File.WriteAllBytes(System.IO.Path.Combine(incoming, "Ted Lasso - S01E02 - Biscuits.mkv"), new byte[900]);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(System.IO.Path.Combine(existingSeason, "Ted Lasso - S01E02 - Biscuits.mkv")), Is.True,
                "the non-conflicting new episode must merge in");
            Assert.That(File.Exists(existingFile), Is.False, "the smaller existing E01 must be replaced");
            Assert.That(report.NeedsManual, Is.Empty);
        });
    }
}
