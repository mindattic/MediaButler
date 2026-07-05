using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// The duplicate-movie policy (MB-A6): when a movie's destination folder
/// already exists with content, <see cref="DuplicateMovieAction.KeepLargest"/>
/// (the default) keeps whichever copy has the larger primary video and deletes
/// the other; <see cref="DuplicateMovieAction.Flag"/> restores the classic
/// MB-LAW-9 leave-both-and-ask behaviour. Born from the real 2026-07-04 run:
/// a 29.6 GB PROPER arrived for a movie whose 11.5 GB copy was already filed.
/// </summary>
[TestFixture]
public class DuplicateMovieActionTests
{
    private TempDir tmp = null!;
    private string source = null!;
    private string movies = null!;
    private MediaButlerSettings settings = null!;

    [SetUp]
    public void SetUp()
    {
        tmp    = new TempDir();
        source = tmp.MakeDir("Torrents");
        movies = tmp.MakeDir("Movies");
        settings = new MediaButlerSettings
        {
            SourcePath           = source,
            TvDestination        = tmp.MakeDir("TV"),
            MoviesDestination    = movies,
            VariationCatalogPath = System.IO.Path.Combine(tmp.Path, "variations.json"),
            EnableLlmFallback    = false,
        };
    }

    [TearDown]
    public void TearDown() => tmp.Dispose();

    /// <summary>Create the incoming movie folder in the source and the existing one at the destination.</summary>
    private (string Incoming, string Existing) Seed(int incomingBytes, int existingBytes)
    {
        var incoming = System.IO.Path.Combine(source, "Weapons (2025)");
        Directory.CreateDirectory(incoming);
        File.WriteAllBytes(System.IO.Path.Combine(incoming, "Weapons (2025).mkv"), new byte[incomingBytes]);

        var existing = System.IO.Path.Combine(movies, "Weapons (2025)");
        Directory.CreateDirectory(existing);
        File.WriteAllBytes(System.IO.Path.Combine(existing, "Weapons (2025).mkv"), new byte[existingBytes]);
        File.WriteAllText(System.IO.Path.Combine(existing, "poster.jpg"), "art");
        return (incoming, existing);
    }

    private PipelineReport Run()
    {
        var report = new PipelineReport();
        new MoveStage(settings, report).Run();
        return report;
    }

    [Test]
    public void KeepLargest_incoming_larger_replaces_the_destination_video_and_keeps_artwork()
    {
        var (incoming, existing) = Seed(incomingBytes: 2000, existingBytes: 500);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(System.IO.Path.Combine(existing, "Weapons (2025).mkv")).Length,
                Is.EqualTo(2000), "the larger incoming video must win");
            Assert.That(File.Exists(System.IO.Path.Combine(existing, "poster.jpg")), Is.True,
                "existing artwork must survive the replacement");
            Assert.That(Directory.Exists(incoming), Is.False, "the emptied incoming shell must be deleted");
            Assert.That(report.MoviesMoved, Is.EqualTo(1));
            Assert.That(report.Errors, Is.Empty);
        });
    }

    [Test]
    public void KeepLargest_incoming_smaller_is_discarded_and_the_destination_untouched()
    {
        var (incoming, existing) = Seed(incomingBytes: 500, existingBytes: 2000);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(System.IO.Path.Combine(existing, "Weapons (2025).mkv")).Length,
                Is.EqualTo(2000), "the larger existing video must stay");
            Assert.That(Directory.Exists(incoming), Is.False, "the losing incoming copy is deleted");
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.NeedsManual, Is.Empty, "an auto-resolved duplicate needs no human");
        });
    }

    [Test]
    public void Flag_leaves_both_copies_and_surfaces_needs_manual()
    {
        settings.DuplicateMovieAction = DuplicateMovieAction.Flag;
        var (incoming, existing) = Seed(incomingBytes: 2000, existingBytes: 500);

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(incoming), Is.True, "Flag must never touch the incoming copy");
            Assert.That(new FileInfo(System.IO.Path.Combine(existing, "Weapons (2025).mkv")).Length,
                Is.EqualTo(500), "Flag must never touch the existing copy");
            Assert.That(report.NeedsManual, Is.Not.Empty);
        });
    }

    [Test]
    public void KeepLargest_without_a_comparable_video_falls_back_to_flagging()
    {
        // Destination folder exists with content but holds NO video — deleting
        // either side would be a guess, so the classic flag path must run.
        var incoming = System.IO.Path.Combine(source, "Weapons (2025)");
        Directory.CreateDirectory(incoming);
        File.WriteAllBytes(System.IO.Path.Combine(incoming, "Weapons (2025).mkv"), new byte[100]);
        var existing = System.IO.Path.Combine(movies, "Weapons (2025)");
        Directory.CreateDirectory(existing);
        File.WriteAllText(System.IO.Path.Combine(existing, "poster.jpg"), "art");

        var report = Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(incoming), Is.True);
            Assert.That(File.Exists(System.IO.Path.Combine(existing, "poster.jpg")), Is.True);
            Assert.That(report.NeedsManual, Is.Not.Empty);
        });
    }

    [TestCase("keep-largest", DuplicateMovieAction.KeepLargest)]
    [TestCase("KeepLargest",  DuplicateMovieAction.KeepLargest)]
    [TestCase("flag",         DuplicateMovieAction.Flag)]
    public void Duplicates_cli_flag_overlays_the_persisted_setting(string flag, DuplicateMovieAction expected)
    {
        var s = new MediaButlerSettings { DuplicateMovieAction = DuplicateMovieAction.Flag };
        if (expected == DuplicateMovieAction.Flag) s.DuplicateMovieAction = DuplicateMovieAction.KeepLargest;
        new Commands.BaseSettings { Duplicates = flag }.ApplyTo(s);
        Assert.That(s.DuplicateMovieAction, Is.EqualTo(expected));
    }

    [Test]
    public void Duplicates_cli_flag_rejects_unknown_values()
    {
        var s = new MediaButlerSettings();
        Assert.Throws<InvalidOperationException>(
            () => new Commands.BaseSettings { Duplicates = "newest" }.ApplyTo(s));
    }

    [TestCase(2000, 500)]
    [TestCase(500, 2000)]
    public void KeepLargest_dry_run_mutates_nothing_in_either_direction(int incomingBytes, int existingBytes)
    {
        settings.DryRun = true;
        var (incoming, existing) = Seed(incomingBytes, existingBytes);

        Run();

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(System.IO.Path.Combine(incoming, "Weapons (2025).mkv")).Length,
                Is.EqualTo(incomingBytes), "dry-run must not touch the incoming copy");
            Assert.That(new FileInfo(System.IO.Path.Combine(existing, "Weapons (2025).mkv")).Length,
                Is.EqualTo(existingBytes), "dry-run must not touch the existing copy");
        });
    }
}
