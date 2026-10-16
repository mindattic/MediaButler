using System.Text.Json;
using MediaButler.Media;
using MediaButler.Pipeline;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Direct unit coverage of <see cref="SeasonMerger"/> — the file-level season
/// merge and shell-deletion logic MoveStage/RenameStage both call into. These
/// tests drive <see cref="SeasonMerger.MergeFiles"/> and
/// <see cref="SeasonMerger.TryDeleteShell"/> straight (no scanner, no stage,
/// no CLI) so the merge/collision/safety rules are pinned in isolation from
/// the rest of the pipeline. <see cref="DuplicateEpisodeActionTests"/> already
/// covers the same KeepLargest/Flag policy end-to-end through MoveStage; this
/// file is the unit-level complement.
/// </summary>
[TestFixture]
public class SeasonMergerMergeFilesTests
{
    private TempDir tmp = null!;
    private string source = null!;
    private string target = null!;
    private MediaButlerSettings settings = null!;
    private PipelineReport report = null!;

    [SetUp]
    public void SetUp()
    {
        tmp = new TempDir();
        source = tmp.MakeDir("incoming");
        target = tmp.MakeDir("target");
        settings = new MediaButlerSettings();
        report = new PipelineReport();
    }

    [TearDown]
    public void TearDown() => tmp.Dispose();

    private static void Write(string path, int bytes) => File.WriteAllBytes(path, new byte[bytes]);

    /// <summary>
    /// Audit entries whose from/to mention <paramref name="pathNeedle"/>, in append order.
    /// Opened with <see cref="FileShare.ReadWrite"/> because <see cref="AuditLog"/> keeps
    /// its own append writer open on this file for the lifetime of the process.
    /// </summary>
    private static List<string> AuditOpsFor(string pathNeedle)
    {
        var file = AuditLog.FilePath();
        if (!File.Exists(file)) return [];
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null) lines.Add(line);
        return lines
            .Select(l => JsonDocument.Parse(l).RootElement)
            .Where(e => (e.GetProperty("from").GetString() ?? "").Contains(pathNeedle, StringComparison.OrdinalIgnoreCase)
                     || (e.GetProperty("to").GetString() ?? "").Contains(pathNeedle, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.GetProperty("op").GetString()!)
            .ToList();
    }

    [Test]
    public void Collision_by_identical_file_name_is_flagged_as_conflict()
    {
        settings.DuplicateEpisodeAction = DuplicateMovieAction.Flag;
        Write(Path.Combine(target, "Show - S02E05 - Title.mkv"), 500);
        var incoming = Path.Combine(source, "Show - S02E05 - Title.mkv");
        Write(incoming, 2000);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 1)));
            Assert.That(File.Exists(incoming), Is.True, "Flag must leave the incoming copy in place");
            Assert.That(new FileInfo(Path.Combine(target, "Show - S02E05 - Title.mkv")).Length, Is.EqualTo(500),
                "Flag must leave the existing copy untouched");
        });
    }

    [Test]
    public void Collision_by_parsed_episode_number_with_a_different_file_name_is_flagged_as_conflict()
    {
        settings.DuplicateEpisodeAction = DuplicateMovieAction.Flag;
        Write(Path.Combine(target, "Show - S02E05 - Old Release.mkv"), 500);
        var incoming = Path.Combine(source, "Show.2x05.mkv");
        Write(incoming, 2000);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 1)));
            Assert.That(File.Exists(incoming), Is.True, "the differently-named incoming copy must be left alone under Flag");
        });
    }

    [Test]
    public void Sample_video_is_never_merged_regardless_of_size()
    {
        var incoming = Path.Combine(source, "Show.S02E05.sample.mkv");
        Write(incoming, 1); // tiny — would pass any size guard, but samples never merge from MergeFiles at all

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 0)));
            Assert.That(File.Exists(incoming), Is.True, "samples are left behind for shell cleanup, not merged");
            Assert.That(Directory.EnumerateFiles(target), Is.Empty);
        });
    }

    [TestCase("readme.txt")]
    [TestCase("info.nfo")]
    public void Junk_extensions_are_never_merged(string junkName)
    {
        var incoming = Path.Combine(source, junkName);
        Write(incoming, 100);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 0)));
            Assert.That(File.Exists(incoming), Is.True, "junk stays behind for shell cleanup");
            Assert.That(Directory.EnumerateFiles(target), Is.Empty);
        });
    }

    [Test]
    public void Subtitle_sidecar_with_no_conflict_is_merged()
    {
        var incoming = Path.Combine(source, "Show - S02E06 - Title.srt");
        Write(incoming, 10);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 1, Conflicts: 0)));
            Assert.That(File.Exists(incoming), Is.False, "the subtitle must have moved out of the source");
            Assert.That(File.Exists(Path.Combine(target, "Show - S02E06 - Title.srt")), Is.True);
        });
    }

    [Test]
    public void KeepLargest_incoming_larger_replaces_the_smaller_existing_copy_and_logs_duplicate_replace()
    {
        var existing = Path.Combine(target, "Show - S02E05 - Title.mkv");
        Write(existing, 500);
        var incoming = Path.Combine(source, "Show - S02E05 - Title.mkv");
        Write(incoming, 2000);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 1, Conflicts: 0)));
            Assert.That(File.Exists(incoming), Is.False, "the winning incoming file must have moved out of source");
            Assert.That(new FileInfo(existing).Length, Is.EqualTo(2000), "the larger incoming video must win in place");
            Assert.That(report.NeedsManual, Is.Empty, "an auto-resolved duplicate needs no human");
            Assert.That(AuditOpsFor(tmp.Path), Contains.Item("duplicate-replace"));
        });
    }

    [TestCase(500, 500)] // equal size: incoming does not win ties
    [TestCase(500, 2000)] // incoming strictly smaller
    public void KeepLargest_incoming_equal_or_smaller_is_discarded_and_logs_duplicate_discard(int incomingBytes, int existingBytes)
    {
        var existing = Path.Combine(target, "Show - S02E05 - Title.mkv");
        Write(existing, existingBytes);
        var incoming = Path.Combine(source, "Show - S02E05 - Title.mkv");
        Write(incoming, incomingBytes);

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 0)));
            Assert.That(File.Exists(incoming), Is.False, "the losing incoming copy must be deleted, not left behind");
            Assert.That(new FileInfo(existing).Length, Is.EqualTo(existingBytes), "the existing copy must be untouched");
            Assert.That(report.NeedsManual, Is.Empty);
            Assert.That(AuditOpsFor(tmp.Path), Contains.Item("duplicate-discard"));
        });
    }

    [Test]
    public void Flag_counts_every_conflicting_file_but_records_a_single_NeedsManual_entry()
    {
        settings.DuplicateEpisodeAction = DuplicateMovieAction.Flag;
        for (var ep = 1; ep <= 3; ep++)
        {
            Write(Path.Combine(target, $"Show - S02E0{ep} - Title.mkv"), 500);
            Write(Path.Combine(source, $"Show - S02E0{ep} - Title.mkv"), 2000);
        }

        var result = SeasonMerger.MergeFiles(source, target, season: 2, settings, report);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SeasonMerger.MergeResult(Moved: 0, Conflicts: 3)));
            Assert.That(report.NeedsManual, Has.Count.EqualTo(1), "one manual entry summarizes every conflicting file");
            Assert.That(report.NeedsManual[0].Reason, Does.Contain("3"));
            Assert.That(report.NeedsManual[0].Kind, Is.EqualTo(MediaKind.TvSeason));
        });
    }

    [Test]
    public void Dry_run_returns_the_same_Moved_and_Conflicts_counts_as_a_live_run_with_nothing_touched_on_disk()
    {
        // Live run on one seed.
        Write(Path.Combine(target, "Show - S02E05 - Title.mkv"), 500);           // will be replaced (incoming larger)
        Write(Path.Combine(source, "Show - S02E05 - Title.mkv"), 2000);
        Write(Path.Combine(source, "Show - S02E06 - Title.mkv"), 900);           // no conflict, merges in
        var liveResult = SeasonMerger.MergeFiles(source, target, season: 2, settings, new PipelineReport());

        // Dry run on an identical fresh seed.
        using var dryTmp = new TempDir();
        var drySource = dryTmp.MakeDir("incoming");
        var dryTarget = dryTmp.MakeDir("target");
        Write(Path.Combine(dryTarget, "Show - S02E05 - Title.mkv"), 500);
        var dryIncomingReplace = Path.Combine(drySource, "Show - S02E05 - Title.mkv");
        Write(dryIncomingReplace, 2000);
        var dryIncomingNew = Path.Combine(drySource, "Show - S02E06 - Title.mkv");
        Write(dryIncomingNew, 900);
        var drySettings = new MediaButlerSettings { DryRun = true };

        var dryResult = SeasonMerger.MergeFiles(drySource, dryTarget, season: 2, drySettings, new PipelineReport());

        Assert.Multiple(() =>
        {
            Assert.That(dryResult, Is.EqualTo(liveResult));
            Assert.That(File.Exists(dryIncomingReplace), Is.True, "dry-run must not move or delete the incoming winner");
            Assert.That(new FileInfo(dryIncomingReplace).Length, Is.EqualTo(2000));
            Assert.That(File.Exists(dryIncomingNew), Is.True, "dry-run must not move the non-conflicting file");
            Assert.That(new FileInfo(Path.Combine(dryTarget, "Show - S02E05 - Title.mkv")).Length, Is.EqualTo(500),
                "dry-run must not touch the existing loser");
            Assert.That(Directory.EnumerateFiles(dryTarget).Count(), Is.EqualTo(1),
                "dry-run must not actually land the non-conflicting file at the target either");
        });
    }
}

[TestFixture]
public class SeasonMergerTryDeleteShellTests
{
    private TempDir tmp = null!;
    private string folder = null!;
    private MediaButlerSettings settings = null!;

    [SetUp]
    public void SetUp()
    {
        tmp = new TempDir();
        folder = tmp.MakeDir("shell");
        settings = new MediaButlerSettings();
    }

    [TearDown]
    public void TearDown() => tmp.Dispose();

    private static void Write(string path, int bytes) => File.WriteAllBytes(path, new byte[bytes]);

    [Test]
    public void Refuses_when_a_non_sample_video_remains()
    {
        Write(Path.Combine(folder, "Show - S02E05 - Title.mkv"), 100);

        var ok = SeasonMerger.TryDeleteShell(folder, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(refuseReason, Does.Contain("non-sample video still present"));
            Assert.That(Directory.Exists(folder), Is.True);
        });
    }

    [Test]
    public void Refuses_when_a_sample_named_video_exceeds_SampleMaxBytes()
    {
        settings.SampleMaxBytes = 100;
        Write(Path.Combine(folder, "Show.S02E05.sample.mkv"), 200);

        var ok = SeasonMerger.TryDeleteShell(folder, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(refuseReason, Does.Contain("exceeds SampleMaxBytes"));
            Assert.That(Directory.Exists(folder), Is.True);
        });
    }

    [Test]
    public void Refuses_when_junk_exceeds_EmptyDeleteSafetyBytes()
    {
        settings.EmptyDeleteSafetyBytes = 100;
        Write(Path.Combine(folder, "notes.txt"), 200);

        var ok = SeasonMerger.TryDeleteShell(folder, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(refuseReason, Does.Contain("non-video content"));
            Assert.That(Directory.Exists(folder), Is.True);
        });
    }

    [Test]
    public void Returns_true_for_a_folder_that_no_longer_exists()
    {
        var missing = Path.Combine(tmp.Path, "already-gone");

        var ok = SeasonMerger.TryDeleteShell(missing, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(refuseReason, Is.Null);
        });
    }

    [Test]
    public void Dry_run_reports_success_but_does_not_delete_a_safe_shell()
    {
        settings.DryRun = true;
        Write(Path.Combine(folder, "sample.mkv"), 10); // bona fide sample, well under the safety floors

        var ok = SeasonMerger.TryDeleteShell(folder, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(refuseReason, Is.Null);
            Assert.That(Directory.Exists(folder), Is.True, "dry-run must not actually delete the shell");
        });
    }

    [Test]
    public void Deletes_a_safe_shell_for_real_when_not_a_dry_run()
    {
        Write(Path.Combine(folder, "sample.mkv"), 10);

        var ok = SeasonMerger.TryDeleteShell(folder, settings, out var refuseReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(refuseReason, Is.Null);
            Assert.That(Directory.Exists(folder), Is.False);
        });
    }
}
