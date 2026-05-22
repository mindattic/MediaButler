using MediaButler.FileBot;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Pure tests for FileBot argument construction and result parsing. We don't
/// spawn filebot.exe here — these helpers exist precisely so the arg shape
/// can be locked down without IO.
/// </summary>
[TestFixture]
public class FileBotClientTests
{
    [Test]
    public void BuildRenameTvArgs_uses_MOVE_in_live_mode()
    {
        var args = FileBotClient.BuildRenameTvArgs(@"M:\TV\Show - Season 01", dryRun: false);
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-rename"));
            Assert.That(args, Does.Contain(@"M:\TV\Show - Season 01"));
            Assert.That(args, Does.Contain("--db"));
            Assert.That(args, Does.Contain("TheTVDB"));
            Assert.That(args, Does.Contain("--action"));
            Assert.That(args, Does.Contain("MOVE"));
            Assert.That(args, Does.Not.Contain("TEST"));
            Assert.That(args, Does.Contain("-non-strict"));
        });
    }

    [Test]
    public void BuildRenameTvArgs_uses_TEST_in_dry_run()
    {
        var args = FileBotClient.BuildRenameTvArgs(@"M:\TV\Show - Season 01", dryRun: true);
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("TEST"));
            Assert.That(args, Does.Not.Contain("MOVE"));
        });
    }

    [Test]
    public void BuildRenameMovieArgs_uses_TheMovieDB_and_year_format()
    {
        var args = FileBotClient.BuildRenameMovieArgs(@"M:\Movies\Heat (1995)", dryRun: false);
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("TheMovieDB"));
            Assert.That(args, Does.Contain("{n} ({y})"));
            Assert.That(args, Does.Contain("MOVE"));
        });
    }

    [Test]
    public void BuildRenameMovieArgs_uses_TEST_in_dry_run()
    {
        var args = FileBotClient.BuildRenameMovieArgs(@"M:\Movies\Heat (1995)", dryRun: true);
        Assert.That(args, Does.Contain("TEST"));
    }

    [Test]
    public void BuildFetchTvArtworkArgs_invokes_artwork_tvdb_script()
    {
        var args = FileBotClient.BuildFetchTvArtworkArgs(@"M:\TV\Show\Season 01");
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-script"));
            Assert.That(args, Does.Contain("fn:artwork.tvdb"));
        });
    }

    [Test]
    public void BuildFetchMovieArtworkArgs_uses_generic_artwork_script()
    {
        // Critical: NOT fn:artwork.tmdb, which is broken in FileBot 5.2.1.
        var args = FileBotClient.BuildFetchMovieArtworkArgs(@"M:\Movies\Heat (1995)");
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("fn:artwork"));
            Assert.That(args, Does.Not.Contain("fn:artwork.tmdb"));
        });
    }

    [Test]
    public void BuildGetSubtitlesArgs_omits_creds_when_missing()
    {
        var args = FileBotClient.BuildGetSubtitlesArgs(@"M:\TV\Show - Season 01", "en", credentials: null);
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-get-subtitles"));
            Assert.That(args, Does.Contain("--lang"));
            Assert.That(args, Does.Contain("en"));
            Assert.That(args, Does.Not.Contain("--def"));
        });
    }

    [Test]
    public void BuildGetSubtitlesArgs_injects_credentials_when_complete()
    {
        var creds = new SubtitleCredentials { User = "ryandebraal", Password = "secret" };
        var args  = FileBotClient.BuildGetSubtitlesArgs(@"M:\TV\Show - Season 01", "en", creds);

        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("osdb.user=ryandebraal"));
            Assert.That(args, Does.Contain("osdb.pwd=secret"));
            // --def appears twice (once per definition)
            Assert.That(args.Count(a => a == "--def"), Is.EqualTo(2));
        });
    }

    [Test]
    public void BuildGetSubtitlesArgs_omits_creds_when_partial()
    {
        var creds = new SubtitleCredentials { User = "ryandebraal" /* password missing */ };
        var args  = FileBotClient.BuildGetSubtitlesArgs(@"M:\TV", "en", creds);
        Assert.That(args, Does.Not.Contain("--def"));
    }

    [Test]
    public void LooksLikeAuthFailure_detects_401_in_stdout()
    {
        var r = new FileBotResult { ExitCode = 1, StdOut = "Could not log in: 401 Unauthorized", StdErr = "" };
        Assert.That(r.LooksLikeAuthFailure, Is.True);
    }

    [Test]
    public void LooksLikeAuthFailure_detects_401_in_stderr()
    {
        var r = new FileBotResult { ExitCode = 1, StdOut = "", StdErr = "HTTP 401 Unauthorized" };
        Assert.That(r.LooksLikeAuthFailure, Is.True);
    }

    [Test]
    public void LooksLikeAuthFailure_detects_invalid_credentials_message()
    {
        var r = new FileBotResult { ExitCode = 1, StdOut = "OpenSubtitles: invalid username/password", StdErr = "" };
        Assert.That(r.LooksLikeAuthFailure, Is.True);
    }

    [Test]
    public void LooksLikeAuthFailure_is_false_for_other_errors()
    {
        var r = new FileBotResult { ExitCode = 4, StdOut = "No match found", StdErr = "Lookup failed" };
        Assert.That(r.LooksLikeAuthFailure, Is.False);
    }

    [Test]
    public void LastInterestingLine_prefers_stderr_over_stdout()
    {
        var r = new FileBotResult
        {
            ExitCode = 1,
            StdOut = "Processing...\n",
            StdErr = "Match not found for season folder",
        };
        Assert.That(r.LastInterestingLine(), Is.EqualTo("Match not found for season folder"));
    }

    [Test]
    public void LastInterestingLine_falls_back_to_stdout_when_stderr_empty()
    {
        var r = new FileBotResult
        {
            ExitCode = 0,
            StdOut = "Step 1\nStep 2\nDone.",
            StdErr = "",
        };
        Assert.That(r.LastInterestingLine(), Is.EqualTo("Done."));
    }
}
