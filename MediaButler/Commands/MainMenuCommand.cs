using MediaButler.FileBot;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MediaButler.Ui;
using Spectre.Console.Cli;

namespace MediaButler.Commands;

/// <summary>
/// Default (no-arg) command: shows the interactive arrow-key menu for users
/// running <c>mediabutler</c> with no subcommand. Each menu action dispatches
/// through the shared <see cref="PipelineRunner"/> so behavior matches the
/// headless subcommands exactly.
/// </summary>
public sealed class MainMenuCommand : Command<BaseSettings>
{
    public override int Execute(CommandContext context, BaseSettings cli)
    {
        Status.Verbosity = cli.Verbosity;

        var runner = new PipelineRunner(new SettingsService());
        // Materialize the settings folder so the Settings menu's "Open Settings
        // File" row has a real path to point at on a fresh install. The first
        // LoadEffective() inside the loop will write the defaults file.
        runner.Settings.EnsureFolder();

        var exit = false;
        while (!exit)
        {
            var s = runner.LoadEffective(cli.ApplyTo);
            var items = BuildItems(s, cli, runner, () => exit = true);

            Screen.Header();
            var sel = Menu.Prompt("[cyan1]MediaButler — choose an action:[/]", items, allowBack: false);
            if (sel is null) return 0;
            if (sel.Tag is Action action) action();
        }
        return 0;
    }

    private static List<MenuItem> BuildItems(
        MediaButlerSettings s,
        BaseSettings cli,
        PipelineRunner runner,
        Action requestExit)
    {
        var fileBot = FileBotClient.TryLocate(s.FileBotPath);
        var fileBotDesc = fileBot is null
            ? "NOT FOUND — set FileBot Path in Settings"
            : "ready: " + fileBot;

        var modeLabel = s.DryRun
            ? (cli.DryRun ? "DRY RUN (CLI --dry-run)" : "DRY RUN (Settings toggle)")
            : "LIVE";

        Action stage(string title, Func<MediaButlerSettings, int> body) => () =>
        {
            Screen.Header(title);
            var snapshot = runner.LoadEffective(cli.ApplyTo);
            ReportExit(body(snapshot));
            Screen.PressAnyKey();
        };

        return new List<MenuItem>
        {
            new() { Name = $"Run Full Pipeline  [{modeLabel}]",
                    Description = "rename + hoist, FileBot rename + artwork, subtitles, move to Plex",
                    Tag = (Action)(() => RunFull(runner, cli, forceDryRun: false)) },
            new() { Name = "Run Full Pipeline (Dry Run)",
                    Description = "preview every change without touching disk",
                    Tag = (Action)(() => RunFull(runner, cli, forceDryRun: true)) },
            new() { Name = "1. Rename & Hoist",
                    Description = "clean folder names, hoist nested seasons, pad to Season 01",
                    Tag = stage("Rename & Hoist", runner.RunRename) },
            new() { Name = "2. FileBot: TV Episodes + Artwork",
                    Description = "rename episodes via TheTVDB, fetch posters/banners/fanart",
                    Tag = stage("FileBot: TV Episodes + Artwork", runner.RunFileBotTv),
                    Disabled = fileBot is null },
            new() { Name = "3. FileBot: Movies + Artwork",
                    Description = "rename movies via TheMovieDB, fetch poster + backdrop",
                    Tag = stage("FileBot: Movies + Artwork", runner.RunFileBotMovies),
                    Disabled = fileBot is null },
            new() { Name = "4. FileBot: Subtitles",
                    Description = "OpenSubtitles via User Secrets (or FileBot Preferences)",
                    Tag = stage("FileBot: Subtitles", runner.RunFileBotSubtitles),
                    Disabled = fileBot is null },
            new() { Name = "5. Move to Plex",
                    Description = $"to {s.TvDestination} and {s.MoviesDestination}",
                    Tag = stage("Move to Plex", runner.RunMove) },
            new() { Name = "Relocate misplaced items",
                    Description = "scan a destination dir and evict items whose kind doesn't belong",
                    Tag = (Action)(() => RunRelocateInteractive(runner, cli)) },
            new() { Name = "Settings",
                    Description = "source, destinations, FileBot path, options, dry-run",
                    Tag = (Action)(() => new SettingsEditor(runner.Settings).Show()) },
            new() { Name = "Status",
                    Description = $"FileBot: {fileBotDesc}",
                    Tag = stage("Status", runner.ShowStatus) },
            new() { Name = "Exit",
                    Description = "quit MediaButler",
                    Tag = (Action)requestExit },
        };
    }

    private static void RunFull(PipelineRunner runner, BaseSettings cli, bool forceDryRun)
    {
        Screen.Header(forceDryRun ? "Run Full Pipeline (Dry Run)" : "Run Full Pipeline");
        var s = runner.LoadEffective(o =>
        {
            cli.ApplyTo(o);
            if (forceDryRun) o.DryRun = true;
        });
        ReportExit(runner.RunFull(s));
        Screen.PressAnyKey();
    }

    /// <summary>
    /// Surface a stage's exit code to the interactive user. The headless
    /// subcommands return this code to the shell; in the menu the report is
    /// already printed, so a one-line closing status is all that's needed —
    /// otherwise an errored or needs-manual run looks identical to a clean one.
    /// </summary>
    private static void ReportExit(int code)
    {
        switch (code)
        {
            case PipelineRunner.ExitErrors:
                Status.Print("Completed with errors — see the summary above.", Theme.Err);
                break;
            case PipelineRunner.ExitNeedsManual:
                Status.Print("Completed — some items need manual review (see above).", Theme.Active);
                break;
        }
    }

    private static void RunRelocateInteractive(PipelineRunner runner, BaseSettings cli)
    {
        Screen.Header("Relocate misplaced items");
        var s = runner.LoadEffective(cli.ApplyTo);
        var prompt = Screen.Prompt(
            "Folder to scan (e.g. M:\\Movies or M:\\TV)",
            currentValue: s.SourcePath);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Status.Print("Cancelled — no folder supplied.", Theme.Dim);
            Screen.PressAnyKey();
            return;
        }
        s.SourcePath = prompt;
        ReportExit(runner.RunRelocate(s));
        Screen.PressAnyKey();
    }
}
