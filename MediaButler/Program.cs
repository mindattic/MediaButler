using MediaButler.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

// UTF-8 for both interactive and headless modes — torrent dumps frequently
// carry non-ASCII characters in folder names. Tolerate redirected stdout.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected */ }

// Spectre.Console.Cli parses "--"-prefixed args as options first, so a
// .WithAlias("--version") on VersionCommand never matches. Translate the
// idiomatic --version / -v shapes into the bare subcommand so users get the
// version output they expect.
if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
    args = new[] { "version" };

var app = new CommandApp<MainMenuCommand>();

app.Configure(config =>
{
    config.SetApplicationName("mediabutler");

    // Surface real exception detail in the outer try/catch below instead of
    // letting Spectre print a one-line "Error: ..." with no stack. Easier to
    // diagnose stage failures during development and gives cron-driven runs
    // a real trace to land in the audit log.
    config.PropagateExceptions();

    config.AddCommand<RunCommand>("run")
        .WithExample("run")
        .WithExample("run", "--dry-run");

    config.AddCommand<ScanCommand>("scan")
        .WithExample("scan")
        .WithExample("scan", "--source", @"M:\Movies");

    config.AddCommand<RenameCommand>("rename")
        .WithExample("rename", "--dry-run", "--source", @"D:\Temp\Inbox");

    config.AddCommand<FileBotTvCommand>("filebot-tv")
        .WithExample("filebot-tv")
        .WithExample("filebot-tv", "--dry-run");

    config.AddCommand<FileBotMoviesCommand>("filebot-movies")
        .WithExample("filebot-movies")
        .WithExample("filebot-movies", "--dry-run");

    config.AddCommand<FileBotSubtitlesCommand>("filebot-subtitles")
        .WithAlias("subtitles")
        .WithExample("subtitles");

    config.AddCommand<MoveCommand>("move")
        .WithExample("move")
        .WithExample("move", "--dry-run");

    config.AddCommand<RelocateCommand>("relocate")
        .WithExample("relocate", "--dry-run", "--source", @"M:\Movies");

    config.AddCommand<StatusCommand>("status")
        .WithExample("status");

    config.AddCommand<VersionCommand>("version");
});

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}
