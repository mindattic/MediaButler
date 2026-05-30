using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// End-to-end tests for the local rename pass. Each test sets up a temp source
/// tree, runs the stage, and asserts the on-disk shape + the report fields.
/// </summary>
[TestFixture]
public class RenameStageTests
{
    private static MediaButlerSettings SettingsFor(string source, bool dryRun = false) => new()
    {
        SourcePath        = source,
        TvDestination     = Path.Combine(source, "_tv"),
        MoviesDestination = Path.Combine(source, "_movies"),
        DryRun            = dryRun,
    };

    [Test]
    public void Dry_run_leaves_disk_untouched_but_still_counts_renames()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("Better.Call.Saul.S05.Complete.1080p.x264");
        File.WriteAllText(Path.Combine(folder, "ep1.mkv"), "fake");

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path, dryRun: true), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(folder), Is.True, "dry run should not move the folder");
            Assert.That(report.Renamed,           Is.EqualTo(1));
        });
    }

    [Test]
    public void Live_rename_produces_the_canonical_folder_name()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("Better.Call.Saul.S05.Complete.1080p.x264");
        File.WriteAllText(Path.Combine(folder, "ep1.mkv"), "fake");

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(folder),                                            Is.False);
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Better Call Saul - Season 05")), Is.True);
            Assert.That(report.Renamed,                                                      Is.EqualTo(1));
        });
    }

    [Test]
    public void Idempotent_run_no_ops_when_folder_already_canonical()
    {
        using var tmp = new TempDir();
        var canonical = tmp.MakeDir("Better Call Saul - Season 05");
        File.WriteAllText(Path.Combine(canonical, "ep1.mkv"), "fake");

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(canonical), Is.True);
            Assert.That(report.Renamed,              Is.EqualTo(0));
        });
    }

    [Test]
    public void Empty_disguised_folder_is_deleted()
    {
        using var tmp = new TempDir();
        tmp.MakeDir("Empty Show (2008) Season 1-5"); // no video files inside

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.EnumerateFileSystemEntries(tmp.Path), Is.Empty);
            Assert.That(report.EmptyDeleted,                            Is.EqualTo(1));
        });
    }

    [Test]
    public void Empty_size_guard_refuses_to_delete_a_folder_that_exceeds_the_threshold()
    {
        using var tmp = new TempDir();
        var folder = tmp.MakeDir("Big Movie In Unknown Container");
        // 2 MB file of an extension MediaButler doesn't recognise. The scanner
        // will classify the folder Empty, but the size guard must veto the delete.
        File.WriteAllBytes(Path.Combine(folder, "movie.bigexotic"), new byte[2 * 1024 * 1024]);

        var settings = SettingsFor(tmp.Path);
        settings.EmptyDeleteSafetyBytes = 1L * 1024 * 1024; // 1 MB threshold
        var report = new PipelineReport();
        new RenameStage(settings, report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(folder), Is.True, "size-guarded Empty folder must NOT be deleted");
            Assert.That(report.EmptyDeleted,      Is.EqualTo(0));
            Assert.That(report.NeedsManual,       Has.Some.Matches<ManualItem>(m => m.Kind == MediaKind.Empty));
        });
    }

    [Test]
    public void Multi_season_parent_hoists_seasons_and_records_count()
    {
        using var tmp = new TempDir();
        var parent = tmp.MakeDir("Bones Complete Series S1-S12 x264 + Subs");
        foreach (var n in new[] { 1, 2, 3 })
        {
            var sub = Path.Combine(parent, $"Season {n}");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "ep1.mkv"), "fake");
        }

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Bones - Season 01")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Bones - Season 02")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Bones - Season 03")), Is.True);
            Assert.That(Directory.Exists(parent),                                       Is.False);
            Assert.That(report.Hoisted,                                                 Is.EqualTo(3));
        });
    }

    [Test]
    public void Loose_video_at_multi_season_parent_is_not_misfiled_into_a_season()
    {
        // A multi-season parent with season subfolders PLUS a loose episode at
        // the root. The loose video must not be tucked into Season 01 (wrong
        // season) — it stays put, the parent is kept, and it's flagged for
        // manual sorting. Non-video orphans (artwork/nfo) still get hoisted.
        using var tmp = new TempDir();
        var parent = tmp.MakeDir("Bones Complete Series S1-S12");
        foreach (var n in new[] { 1, 2 })
        {
            var sub = Path.Combine(parent, $"Season {n}");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "ep1.mkv"), "fake");
        }
        var looseVideo = Path.Combine(parent, "Bones.S03E07.stray.mkv");
        File.WriteAllText(looseVideo, "fake");
        File.WriteAllText(Path.Combine(parent, "poster.jpg"), "art");

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            // Seasons hoisted out.
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Bones - Season 01")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(tmp.Path, "Bones - Season 02")), Is.True);
            // Loose video NOT moved into Season 01, and parent kept (not deleted).
            Assert.That(File.Exists(looseVideo), Is.True, "loose episode must stay at the parent");
            Assert.That(File.Exists(Path.Combine(tmp.Path, "Bones - Season 01", "Bones.S03E07.stray.mkv")),
                Is.False, "loose episode must not be misfiled into Season 01");
            // Non-video orphan still hoisted into the first season.
            Assert.That(File.Exists(Path.Combine(tmp.Path, "Bones - Season 01", "poster.jpg")), Is.True);
            // Flagged for manual review.
            Assert.That(report.NeedsManual,
                Has.Some.Matches<ManualItem>(m => m.Kind == MediaKind.MultiSeasonParent));
        });
    }

    [Test]
    public void Extras_folder_is_left_in_place_and_flagged()
    {
        using var tmp = new TempDir();
        var extras = tmp.MakeDir("The Venture Bros. - Extras");
        File.WriteAllText(Path.Combine(extras, "bts.mkv"), "fake");

        var report = new PipelineReport();
        new RenameStage(SettingsFor(tmp.Path), report).Run();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(extras), Is.True);
            Assert.That(report.NeedsManual,       Has.Some.Matches<ManualItem>(m => m.Kind == MediaKind.Extras));
        });
    }
}
