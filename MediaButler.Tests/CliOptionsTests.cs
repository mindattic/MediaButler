using MediaButler.App;
using MediaButler.Settings;
using NUnit.Framework;

namespace MediaButler.Tests;

[TestFixture]
public class CliOptionsTests
{
    [Test]
    public void Empty_args_default_to_Menu()
    {
        var o = CliOptions.Parse(Array.Empty<string>());
        Assert.Multiple(() =>
        {
            Assert.That(o.Command,    Is.EqualTo(CliCommand.Menu));
            Assert.That(o.DryRun,     Is.False);
            Assert.That(o.ShowHelp,   Is.False);
            Assert.That(o.UnknownArg, Is.Null);
        });
    }

    [TestCase("run",               CliCommand.RunFull)]
    [TestCase("scan",              CliCommand.Scan)]
    [TestCase("rename",            CliCommand.Rename)]
    [TestCase("filebot-tv",        CliCommand.FileBotTv)]
    [TestCase("filebot-movies",    CliCommand.FileBotMovies)]
    [TestCase("filebot-subtitles", CliCommand.FileBotSubtitles)]
    [TestCase("subtitles",         CliCommand.FileBotSubtitles)]
    [TestCase("move",              CliCommand.Move)]
    [TestCase("status",            CliCommand.Status)]
    [TestCase("RUN",               CliCommand.RunFull)] // case-insensitive
    public void Subcommand_parses_to_expected_enum(string arg, CliCommand expected) =>
        Assert.That(CliOptions.Parse(new[] { arg }).Command, Is.EqualTo(expected));

    [TestCase("--dry-run")]
    [TestCase("-n")]
    public void DryRun_flag_is_picked_up(string flag) =>
        Assert.That(CliOptions.Parse(new[] { "run", flag }).DryRun, Is.True);

    [Test]
    public void Source_override_is_captured()
    {
        var o = CliOptions.Parse(new[] { "scan", "--source", @"M:\Movies" });
        Assert.Multiple(() =>
        {
            Assert.That(o.Command,        Is.EqualTo(CliCommand.Scan));
            Assert.That(o.SourceOverride, Is.EqualTo(@"M:\Movies"));
        });
    }

    [Test]
    public void Multiple_overrides_combine()
    {
        var o = CliOptions.Parse(new[]
        {
            "run", "--dry-run",
            "--source", @"D:\Inbox",
            "--tv-dest", @"E:\TV",
            "--movies-dest", @"E:\Movies",
        });
        Assert.Multiple(() =>
        {
            Assert.That(o.Command,            Is.EqualTo(CliCommand.RunFull));
            Assert.That(o.DryRun,             Is.True);
            Assert.That(o.SourceOverride,     Is.EqualTo(@"D:\Inbox"));
            Assert.That(o.TvDestOverride,     Is.EqualTo(@"E:\TV"));
            Assert.That(o.MoviesDestOverride, Is.EqualTo(@"E:\Movies"));
        });
    }

    [TestCase("--help")]
    [TestCase("-h")]
    [TestCase("-?")]
    [TestCase("help")]
    public void Help_is_recognised_in_all_idiomatic_forms(string arg) =>
        Assert.That(CliOptions.Parse(new[] { arg }).ShowHelp, Is.True);

    [TestCase("--version")]
    [TestCase("-v")]
    public void Version_flag_is_recognised(string flag) =>
        Assert.That(CliOptions.Parse(new[] { flag }).ShowVersion, Is.True);

    [TestCase("--quiet",   Verbosity.Quiet)]
    [TestCase("-q",        Verbosity.Quiet)]
    [TestCase("--verbose", Verbosity.Verbose)]
    public void Verbosity_flag_sets_level(string flag, Verbosity expected) =>
        Assert.That(CliOptions.Parse(new[] { flag }).Verbosity, Is.EqualTo(expected));

    [Test]
    public void Default_verbosity_is_normal() =>
        Assert.That(CliOptions.Parse(Array.Empty<string>()).Verbosity, Is.EqualTo(Verbosity.Normal));

    [Test]
    public void Unknown_subcommand_is_reported()
    {
        var o = CliOptions.Parse(new[] { "frobnicate" });
        Assert.That(o.UnknownArg, Is.EqualTo("frobnicate"));
    }

    [Test]
    public void Unknown_flag_is_reported()
    {
        var o = CliOptions.Parse(new[] { "run", "--frobnicate" });
        Assert.That(o.UnknownArg, Is.EqualTo("--frobnicate"));
    }

    [Test]
    public void ApplyTo_overlays_overrides_onto_settings()
    {
        var s = new MediaButlerSettings
        {
            SourcePath        = @"M:\Torrents",
            TvDestination     = @"M:\TV",
            MoviesDestination = @"M:\Movies",
            DryRun            = false,
        };
        CliOptions.Parse(new[] { "run", "--dry-run", "--source", @"D:\Inbox" }).ApplyTo(s);

        Assert.Multiple(() =>
        {
            Assert.That(s.DryRun,            Is.True);
            Assert.That(s.SourcePath,        Is.EqualTo(@"D:\Inbox"));
            Assert.That(s.TvDestination,     Is.EqualTo(@"M:\TV"),     "non-overridden values are preserved");
            Assert.That(s.MoviesDestination, Is.EqualTo(@"M:\Movies"), "non-overridden values are preserved");
        });
    }
}
