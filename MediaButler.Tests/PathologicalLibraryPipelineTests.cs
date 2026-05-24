using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Tests.Fixtures;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// End-to-end integration tests against the <see cref="PathologicalLibrary"/>
/// fixture. Each test runs a real pipeline stage in-process against a synthesized
/// temp library full of the messiest folder shapes from <c>NameParser.cs</c>'s
/// canonical pitfall list, then asserts the on-disk result matches Plex's
/// canonical naming as codified in <see cref="PlexStandard"/>.
///
/// <para>These tests do NOT spawn FileBot — FileBot is the only stage we mock
/// out, because we want a hermetic test that doesn't depend on the local
/// FileBot install or network access to TheTVDB / TMDB / OpenSubtitles. The
/// RenameStage produces FileBot-ready staging names, and MoveStage relocates
/// them to the destinations, both of which are pure local IO.</para>
/// </summary>
[TestFixture]
public class PathologicalLibraryPipelineTests
{
    private PathologicalLibrary fixture = null!;
    private IReadOnlyList<FixtureCase> cases = null!;
    private MediaButlerSettings settings = null!;

    [SetUp]
    public void SetUp()
    {
        fixture = new PathologicalLibrary();
        cases   = fixture.Populate();
        settings = new MediaButlerSettings
        {
            SourcePath        = fixture.SourcePath,
            TvDestination     = fixture.TvDestination,
            MoviesDestination = fixture.MoviesDestination,
            // Defaults from production settings — anything we override here
            // is deliberate to keep the test hermetic.
            EnableLlmFallback = false,
            DryRun            = false,
        };
    }

    [TearDown]
    public void TearDown() => fixture.Dispose();

    [Test]
    public void Scanner_classifies_every_pathological_case_as_expected()
    {
        var actual = new MediaScanner(settings).Scan().ToDictionary(i => i.OriginalName, i => i);

        Assert.Multiple(() =>
        {
            foreach (var c in cases)
            {
                Assert.That(actual.ContainsKey(c.Input), $"scanner missed input folder: {c.Input}");
                var item = actual[c.Input];
                var expectedKind = MapKind(c.Kind);
                Assert.That(item.Kind, Is.EqualTo(expectedKind),
                    $"{c.Input}: expected kind {expectedKind}, got {item.Kind}");

                if (c.Kind == ExpectedKind.TvSeason)
                {
                    Assert.That(item.ShowName, Is.EqualTo(c.Show),     $"{c.Input}: show name");
                    Assert.That(item.SeasonNumber, Is.EqualTo(c.Season), $"{c.Input}: season number");
                }
                else if (c.Kind == ExpectedKind.MultiSeasonParent)
                {
                    Assert.That(item.ShowName, Is.EqualTo(c.Show), $"{c.Input}: multi-season show name");
                    Assert.That(item.Seasons.Count, Is.GreaterThan(0), $"{c.Input}: no nested seasons detected");
                }
                else if (c.Kind == ExpectedKind.Movie)
                {
                    Assert.That(item.MovieTitle, Is.EqualTo(c.MovieTitle), $"{c.Input}: movie title");
                    Assert.That(item.MovieYear,  Is.EqualTo(c.Year),       $"{c.Input}: movie year");
                }
            }
        });
    }

    [Test]
    public void RenameStage_produces_FileBot_ready_staging_names_for_every_case()
    {
        var report = new PipelineReport();
        new RenameStage(settings, report).Run();

        Assert.Multiple(() =>
        {
            // Single-season TV: folder renamed in place to "{Show} - Season XX".
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.TvSeason))
            {
                var expected = Path.Combine(settings.SourcePath,
                    NameParser.FormatSeasonFolder(c.Show!, c.Season!.Value));
                Assert.That(Directory.Exists(expected),
                    $"single-season rename missing for {c.Input}: expected {expected}");
            }

            // Multi-season parent: each nested season hoisted to source root.
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.MultiSeasonParent))
            {
                foreach (var season in c.NestedSeasonsToCreate)
                {
                    var expected = Path.Combine(settings.SourcePath,
                        NameParser.FormatSeasonFolder(c.Show!, season));
                    Assert.That(Directory.Exists(expected),
                        $"hoist missing for {c.Input} season {season}: expected {expected}");
                }
                // Parent shell should be gone — all video moved out.
                var parent = Path.Combine(settings.SourcePath, c.Input);
                Assert.That(Directory.Exists(parent), Is.False,
                    $"parent shell {c.Input} should have been deleted after hoist");
            }

            // Movies: renamed to "{Title} (YYYY)".
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.Movie))
            {
                var expected = Path.Combine(settings.SourcePath,
                    NameParser.FormatMovieFolder(c.MovieTitle!, c.Year));
                Assert.That(Directory.Exists(expected),
                    $"movie rename missing for {c.Input}: expected {expected}");
            }

            // Empty: deleted.
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.Empty))
            {
                var path = Path.Combine(settings.SourcePath, c.Input);
                Assert.That(Directory.Exists(path), Is.False,
                    $"Empty folder {c.Input} should have been deleted");
            }

            // EmptyButLarge: refused — must stay AND appear in NeedsManual.
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.EmptyButLarge))
            {
                var path = Path.Combine(settings.SourcePath, c.Input);
                Assert.That(Directory.Exists(path), Is.True,
                    $"EmptyButLarge folder {c.Input} should NOT have been deleted");
                Assert.That(report.NeedsManual.Any(m => m.Path == path), Is.True,
                    $"EmptyButLarge folder {c.Input} should be in NeedsManual");
            }

            // Extras: left in place, surfaced in NeedsManual.
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.Extras))
            {
                var path = Path.Combine(settings.SourcePath, c.Input);
                Assert.That(Directory.Exists(path), Is.True,
                    $"Extras folder {c.Input} should remain in place");
                Assert.That(report.NeedsManual.Any(m => m.Path == path), Is.True,
                    $"Extras folder {c.Input} should be in NeedsManual");
            }

            // Unknown: left in place, surfaced in NeedsManual.
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.Unknown))
            {
                var path = Path.Combine(settings.SourcePath, c.Input);
                Assert.That(Directory.Exists(path), Is.True,
                    $"Unknown folder {c.Input} should remain in place");
                Assert.That(report.NeedsManual.Any(m => m.Path == path), Is.True,
                    $"Unknown folder {c.Input} should be in NeedsManual");
            }
        });
    }

    [Test]
    public void RenameThenMove_lands_every_TV_season_at_Plex_canonical_path()
    {
        var report = new PipelineReport();
        new RenameStage(settings, report).Run();
        new MoveStage(settings, report).Run();

        Assert.Multiple(() =>
        {
            // Single-season TV ends up at {TvDestination}\{Show}\Season XX
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.TvSeason))
            {
                var expected = PlexStandard.TvSeasonPath(
                    fixture.TvDestination, c.Show!, c.Season!.Value);
                Assert.That(Directory.Exists(expected),
                    $"TV move target missing for {c.Input}: expected {expected}");
            }

            // Multi-season parents: each hoisted season lands at its Plex slot
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.MultiSeasonParent))
            {
                foreach (var season in c.NestedSeasonsToCreate)
                {
                    var expected = PlexStandard.TvSeasonPath(
                        fixture.TvDestination, c.Show!, season);
                    Assert.That(Directory.Exists(expected),
                        $"hoisted season missing for {c.Input}/Season {season}: expected {expected}");
                }
            }
        });
    }

    [Test]
    public void RenameThenMove_lands_every_movie_at_Plex_canonical_path()
    {
        var report = new PipelineReport();
        new RenameStage(settings, report).Run();
        new MoveStage(settings, report).Run();

        Assert.Multiple(() =>
        {
            foreach (var c in cases.Where(x => x.Kind == ExpectedKind.Movie))
            {
                var expected = PlexStandard.MoviePath(
                    fixture.MoviesDestination, c.MovieTitle!, c.Year!.Value);
                Assert.That(Directory.Exists(expected),
                    $"movie move target missing for {c.Input}: expected {expected}");
            }
        });
    }

    [Test]
    public void DryRun_does_not_mutate_anything_on_disk()
    {
        settings.DryRun = true;
        var beforeSnapshot = Directory.EnumerateFileSystemEntries(
            settings.SourcePath, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();

        var report = new PipelineReport();
        new RenameStage(settings, report).Run();
        new MoveStage(settings, report).Run();

        var afterSnapshot = Directory.EnumerateFileSystemEntries(
            settings.SourcePath, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();

        Assert.That(afterSnapshot, Is.EqualTo(beforeSnapshot),
            "dry-run must not modify, create, or delete any path under the source root");

        // Destinations must also remain empty (we created them in fixture setup).
        Assert.That(Directory.EnumerateFileSystemEntries(fixture.TvDestination).Any(),
            Is.False, "dry-run leaked into TV destination");
        Assert.That(Directory.EnumerateFileSystemEntries(fixture.MoviesDestination).Any(),
            Is.False, "dry-run leaked into Movies destination");
    }

    [Test]
    public void Pipeline_re_runs_are_idempotent_on_an_already_organized_library()
    {
        var report1 = new PipelineReport();
        new RenameStage(settings, report1).Run();
        new MoveStage(settings, report1).Run();

        // Snapshot destination state after the first pass.
        var firstPassTv = Directory.EnumerateFileSystemEntries(
            fixture.TvDestination, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();
        var firstPassMovies = Directory.EnumerateFileSystemEntries(
            fixture.MoviesDestination, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();

        // Second run on an empty source (no new content) — must not corrupt
        // the destination structure or duplicate anything.
        var report2 = new PipelineReport();
        new RenameStage(settings, report2).Run();
        new MoveStage(settings, report2).Run();

        var secondPassTv = Directory.EnumerateFileSystemEntries(
            fixture.TvDestination, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();
        var secondPassMovies = Directory.EnumerateFileSystemEntries(
            fixture.MoviesDestination, "*", SearchOption.AllDirectories).OrderBy(s => s).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(secondPassTv,     Is.EqualTo(firstPassTv),     "second TV pass mutated destination");
            Assert.That(secondPassMovies, Is.EqualTo(firstPassMovies), "second movies pass mutated destination");
            Assert.That(report2.Errors,   Is.Empty, "idempotent re-run produced errors");
        });
    }

    [Test]
    public void Relocate_evicts_a_TvSeason_dropped_into_the_movies_destination()
    {
        // Simulate a user manually misfiling a TV season into the movies dest.
        var misfiled = Path.Combine(fixture.MoviesDestination, "Better Call Saul - Season 03");
        Directory.CreateDirectory(misfiled);
        File.WriteAllBytes(Path.Combine(misfiled, "BCS.S03E01.mkv"), Array.Empty<byte>());

        // Run RelocateStage against the movies dest as the source.
        settings.SourcePath = fixture.MoviesDestination;
        var report = new PipelineReport();
        new RelocateStage(settings, report).Run();

        var expected = PlexStandard.TvSeasonPath(fixture.TvDestination, "Better Call Saul", 3);
        Assert.That(Directory.Exists(expected),
            $"RelocateStage should have moved the misfiled season to {expected}");
        Assert.That(Directory.Exists(misfiled), Is.False,
            "RelocateStage should have removed the misfiled folder from movies");
    }

    [Test]
    public void Pipeline_returns_NeedsManual_exit_code_when_only_extras_remain()
    {
        // Build a runner against a settings store. SettingsService writes to %APPDATA%,
        // so for the test we hand-roll the equivalent of RunStage.
        var report = new PipelineReport();
        new RenameStage(settings, report).Run();
        new MoveStage(settings, report).Run();

        Assert.That(report.NeedsManual.Count, Is.GreaterThan(0),
            "fixture intentionally contains Extras/Unknown — they must surface in NeedsManual");

        // Mimic PipelineRunner.RunStage's exit-code logic.
        var exit = report.Errors.Count > 0 ? PipelineRunner.ExitErrors
                 : report.NeedsManual.Count > 0 ? PipelineRunner.ExitNeedsManual
                 : PipelineRunner.ExitOk;

        Assert.That(exit, Is.EqualTo(PipelineRunner.ExitNeedsManual),
            "clean run with manual-review items must return exit code 2, not 0");
    }

    private static MediaKind MapKind(ExpectedKind k) => k switch
    {
        ExpectedKind.TvSeason          => MediaKind.TvSeason,
        ExpectedKind.MultiSeasonParent => MediaKind.MultiSeasonParent,
        ExpectedKind.Movie             => MediaKind.Movie,
        ExpectedKind.Extras            => MediaKind.Extras,
        ExpectedKind.Empty             => MediaKind.Empty,
        ExpectedKind.EmptyButLarge     => MediaKind.Empty, // scanner reports Empty; RenameStage refuses delete
        ExpectedKind.Unknown           => MediaKind.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
}
