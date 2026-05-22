using MediaButler.FileBot;
using MediaButler.Media;
using MediaButler.Menu;
using MediaButler.Pipeline;
using MediaButler.Settings;

namespace MediaButler.App;

/// <summary>
/// Top-level MediaButler application: owns the settings service, builds the main
/// menu, and dispatches to each pipeline stage. Always re-reads settings
/// inside menu callbacks so the user can change Source/Destination/FileBot
/// path mid-session without restarting.
/// </summary>
public sealed class MediaButlerApp
{
    private readonly SettingsService settings = new();

    public Task<int> RunAsync(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected */ }

        // Ensure settings.json exists so the user can see a concrete defaults
        // file the first time they open the Settings menu.
        settings.EnsureFolder();
        _ = settings.Load();

        ConsoleMenu.Show(Array.Empty<string>(), BuildMainMenu);
        return Task.FromResult(0);
    }

    private IReadOnlyList<MenuItem> BuildMainMenu()
    {
        var s = settings.Load();
        var fileBot = FileBotClient.TryLocate(s.FileBotPath);
        var fileBotDesc = fileBot is null
            ? "NOT FOUND — set FileBot Path in Settings"
            : "ready: " + fileBot;

        return new List<MenuItem>
        {
            new()
            {
                Label = "Run Full Pipeline",
                Description = "rename + hoist, FileBot rename + artwork, move to Plex",
                OnSelect = RunFullPipeline,
            },
            new()
            {
                Label = "1. Rename & Hoist",
                Description = "clean folder names, hoist nested seasons, pad to Season 01",
                OnSelect = RunRenameAndHoist,
            },
            new()
            {
                Label = "2. FileBot: TV Episodes + Artwork",
                Description = "rename episodes via TheTVDB, fetch posters/banners/fanart",
                OnSelect = RunFileBotTv,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "3. FileBot: Movies + Artwork",
                Description = "rename movies via TheMovieDB, fetch poster + backdrop",
                OnSelect = RunFileBotMovies,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "4. FileBot: Subtitles",
                Description = "needs OpenSubtitles login configured in FileBot",
                OnSelect = RunFileBotSubtitles,
                Disabled = fileBot is null,
            },
            new()
            {
                Label = "5. Move to Plex",
                Description = $"to {s.TvDestination} and {s.MoviesDestination}",
                OnSelect = RunMoveToPlex,
            },
            new()
            {
                Label = "Settings",
                Description = "source, destinations, FileBot path, options",
                OnSelect = () => { new SettingsEditor(settings).Show(); return true; },
            },
            new()
            {
                Label = "Status",
                Description = $"FileBot: {fileBotDesc}",
                OnSelect = () => { ShowStatus(); return true; },
            },
            new()
            {
                Label = "Exit",
                Description = "quit MediaButler",
                OnSelect = () => false,
            },
        };
    }

    private bool RunFullPipeline()
    {
        ConsoleMenu.WriteHeader("Run Full Pipeline");
        var s = settings.Load();
        if (!ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }

        new RenameStage(s).Run();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null)
        {
            ConsoleMenu.Status("FileBot not found at " + s.FileBotPath + " — skipping FileBot stages.", ConsoleMenu.Err);
        }
        else
        {
            new FileBotStage(s, fb).Run();
        }
        new MoveStage(s).Run();

        ConsoleMenu.Status("Pipeline complete.", ConsoleMenu.Ok);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunRenameAndHoist()
    {
        ConsoleMenu.WriteHeader("Rename & Hoist");
        var s = settings.Load();
        if (!ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }
        new RenameStage(s).Run();
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotTv()
    {
        ConsoleMenu.WriteHeader("FileBot: TV Episodes + Artwork");
        var s = settings.Load();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }
        new FileBotStage(s, fb).RunTv();
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotMovies()
    {
        ConsoleMenu.WriteHeader("FileBot: Movies + Artwork");
        var s = settings.Load();
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }
        new FileBotStage(s, fb).RunMovies();
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunFileBotSubtitles()
    {
        ConsoleMenu.WriteHeader("FileBot: Subtitles");
        var s = settings.Load();
        if (!s.EnableSubtitles)
        {
            ConsoleMenu.Status("Subtitles are disabled in Settings.", ConsoleMenu.Dim);
            ConsoleMenu.WaitForKey();
            return true;
        }
        var fb = FileBotClient.TryCreate(s);
        if (fb is null || !ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }
        new FileBotStage(s, fb).RunSubtitles();
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool RunMoveToPlex()
    {
        ConsoleMenu.WriteHeader("Move to Plex");
        var s = settings.Load();
        if (!ValidateSource(s)) { ConsoleMenu.WaitForKey(); return true; }
        new MoveStage(s).Run();
        ConsoleMenu.WaitForKey();
        return true;
    }

    private void ShowStatus()
    {
        ConsoleMenu.WriteHeader("Status");
        var s = settings.Load();
        ConsoleMenu.Status("Settings file: " + settings.FilePath, ConsoleMenu.Normal);
        ConsoleMenu.Status("Source       : " + s.SourcePath + " " + (Directory.Exists(s.SourcePath) ? "[ok]" : "[MISSING]"), Directory.Exists(s.SourcePath) ? ConsoleMenu.Ok : ConsoleMenu.Err);
        ConsoleMenu.Status("TV dest      : " + s.TvDestination, ConsoleMenu.Normal);
        ConsoleMenu.Status("Movies dest  : " + s.MoviesDestination, ConsoleMenu.Normal);
        var fb = FileBotClient.TryLocate(s.FileBotPath);
        ConsoleMenu.Status("FileBot      : " + (fb ?? "NOT FOUND"), fb is null ? ConsoleMenu.Err : ConsoleMenu.Ok);

        if (Directory.Exists(s.SourcePath))
        {
            var scanner = new MediaScanner(s);
            var items = scanner.Scan().ToList();
            ConsoleMenu.Status($"Scanned {items.Count} root folder(s):", ConsoleMenu.Normal);
            foreach (var grp in items.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString()))
                ConsoleMenu.Status($"  {grp.Key,-20} {grp.Count()}", ConsoleMenu.Dim);
        }

        ConsoleMenu.WaitForKey();
    }

    private static bool ValidateSource(MediaButlerSettings s)
    {
        if (!Directory.Exists(s.SourcePath))
        {
            ConsoleMenu.Status("Source path not found: " + s.SourcePath, ConsoleMenu.Err);
            ConsoleMenu.Status("Set it via the Settings menu.", ConsoleMenu.Dim);
            return false;
        }
        return true;
    }
}
