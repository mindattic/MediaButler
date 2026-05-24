using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class MainMenuCommand : Command<MainMenuCommand.Settings>
{
    public sealed class Settings : BaseSettings { }

    public override int Execute(CommandContext context, Settings cli)
    {
        Status.Verbosity = cli.Verbosity;

        var runner = new PipelineRunner(new SettingsService());
        // Materialize the settings folder so the Settings menu's "Open Settings
        // File" row has a real path to point at on a fresh install. The first
        // LoadEffective() inside the loop will write the defaults file.
        runner.Settings.EnsureFolder();

        while (true)
        {
            var s = runner.LoadEffective(cli.ApplyTo);
            var items = BuildItems(s, cli);

            Screen.Header();
            var sel = Menu.Prompt("[cyan1]MediaButler — choose an action:[/]", items, allowBack: false);
            if (sel is null) return 0;

            switch (sel.Tag)
            {
                case "run":         RunFull(runner, cli, forceDryRun: false); break;
                case "run-dry":     RunFull(runner, cli, forceDryRun: true);  break;
                case "rename":      WrapStage("Rename & Hoist",                 runner, cli, runner.RunRename);          break;
                case "filebot-tv":  WrapStage("FileBot: TV Episodes + Artwork", runner, cli, runner.RunFileBotTv);       break;
                case "filebot-mov": WrapStage("FileBot: Movies + Artwork",      runner, cli, runner.RunFileBotMovies);   break;
                case "filebot-sub": WrapStage("FileBot: Subtitles",             runner, cli, runner.RunFileBotSubtitles); break;
                case "move":        WrapStage("Move to Plex",                   runner, cli, runner.RunMove);            break;
                case "relocate":    RunRelocateInteractive(runner, cli); break;
                case "settings":    new SettingsEditor(runner.Settings).Show(); break;
                case "status":      RunStatusInteractive(runner, cli); break;
                case "exit":        return 0;
            }
        }
    }

    private static List<MenuItem> BuildItems(MediaButlerSettings s, BaseSettings cli)
    {
        var fileBot = FileBotClient.TryLocate(s.FileBotPath);
        var fileBotDesc = fileBot is null
            ? "NOT FOUND — set FileBot Path in Settings"
            : "ready: " + fileBot;

        var modeLabel = s.DryRun
            ? (cli.DryRun ? "DRY RUN (CLI --dry-run)" : "DRY RUN (Settings toggle)")
            : "LIVE";

        return new List<MenuItem>
        {
            new() { Name = $"Run Full Pipeline  [{modeLabel}]",
                    Description = "rename + hoist, FileBot rename + artwork, subtitles, move to Plex",
                    Tag = "run" },
            new() { Name = "Run Full Pipeline (Dry Run)",
                    Description = "preview every change without touching disk",
                    Tag = "run-dry" },
            new() { Name = "1. Rename & Hoist",
                    Description = "clean folder names, hoist nested seasons, pad to Season 01",
                    Tag = "rename" },
            new() { Name = "2. FileBot: TV Episodes + Artwork",
                    Description = "rename episodes via TheTVDB, fetch posters/banners/fanart",
                    Tag = "filebot-tv",  Disabled = fileBot is null },
            new() { Name = "3. FileBot: Movies + Artwork",
                    Description = "rename movies via TheMovieDB, fetch poster + backdrop",
                    Tag = "filebot-mov", Disabled = fileBot is null },
            new() { Name = "4. FileBot: Subtitles",
                    Description = "OpenSubtitles via User Secrets (or FileBot Preferences)",
                    Tag = "filebot-sub", Disabled = fileBot is null },
            new() { Name = "5. Move to Plex",
                    Description = $"to {s.TvDestination} and {s.MoviesDestination}",
                    Tag = "move" },
            new() { Name = "Relocate misplaced items",
                    Description = "scan a destination dir and evict items whose kind doesn't belong",
                    Tag = "relocate" },
            new() { Name = "Settings",
                    Description = "source, destinations, FileBot path, options, dry-run",
                    Tag = "settings" },
            new() { Name = "Status",
                    Description = $"FileBot: {fileBotDesc}",
                    Tag = "status" },
            new() { Name = "Exit",
                    Description = "quit MediaButler",
                    Tag = "exit" },
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
        runner.RunFull(s);
        Screen.PressAnyKey();
    }

    private static void WrapStage(
        string title,
        PipelineRunner runner,
        BaseSettings cli,
        Func<MediaButlerSettings, int> action)
    {
        Screen.Header(title);
        var s = runner.LoadEffective(cli.ApplyTo);
        action(s);
        Screen.PressAnyKey();
    }

    private static void RunStatusInteractive(PipelineRunner runner, BaseSettings cli)
    {
        Screen.Header("Status");
        var s = runner.LoadEffective(cli.ApplyTo);
        runner.ShowStatus(s);
        Screen.PressAnyKey();
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
        runner.RunRelocate(s);
        Screen.PressAnyKey();
    }
}
