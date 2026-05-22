using MediaButler.Menu;

namespace MediaButler.Settings;

/// <summary>
/// Settings sub-menu. Each row reads the current value, lets the user edit it
/// (free-form string, toggle, or list edit), and writes the change back to
/// <c>%APPDATA%\MindAttic\MediaButler\settings.json</c>.
/// </summary>
public sealed class SettingsEditor
{
    private readonly SettingsService settings;

    public SettingsEditor(SettingsService settings) => this.settings = settings;

    public void Show()
    {
        ConsoleMenu.Show(["Settings"], BuildItems);
    }

    private IReadOnlyList<MenuItem> BuildItems()
    {
        var s = settings.Load();
        var path = settings.FilePath;

        return new List<MenuItem>
        {
            new() { Label = "Source Path",         Description = s.SourcePath,        OnSelect = () => EditString("Source path",         v => v.SourcePath,        (v, x) => v.SourcePath = x) },
            new() { Label = "TV Destination",      Description = s.TvDestination,     OnSelect = () => EditString("TV destination",      v => v.TvDestination,     (v, x) => v.TvDestination = x) },
            new() { Label = "Movies Destination",  Description = s.MoviesDestination, OnSelect = () => EditString("Movies destination",  v => v.MoviesDestination, (v, x) => v.MoviesDestination = x) },
            new() { Label = "FileBot Path",        Description = s.FileBotPath,       OnSelect = () => EditString("FileBot path",        v => v.FileBotPath,       (v, x) => v.FileBotPath = x) },
            new() { Label = "Subtitle Language",   Description = s.SubtitleLanguage,  OnSelect = () => EditString("Subtitle language",   v => v.SubtitleLanguage,  (v, x) => v.SubtitleLanguage = x) },
            new() { Label = "Dry Run",             Description = Bool(s.DryRun) + " (no disk mutations when on)", OnSelect = () => Toggle(v => v.DryRun, (v, x) => v.DryRun = x) },
            new() { Label = "Enable Subtitles",    Description = Bool(s.EnableSubtitles), OnSelect = () => Toggle(v => v.EnableSubtitles, (v, x) => v.EnableSubtitles = x) },
            new() { Label = "Rename TV Episodes",  Description = Bool(s.RenameEpisodes),  OnSelect = () => Toggle(v => v.RenameEpisodes,  (v, x) => v.RenameEpisodes = x) },
            new() { Label = "Rename Movies",       Description = Bool(s.RenameMovies),    OnSelect = () => Toggle(v => v.RenameMovies,    (v, x) => v.RenameMovies = x) },
            new() { Label = "Fetch Artwork",       Description = Bool(s.FetchArtwork),    OnSelect = () => Toggle(v => v.FetchArtwork,    (v, x) => v.FetchArtwork = x) },
            new() { Label = "Enable LLM Fallback", Description = Bool(s.EnableLlmFallback) + " (Legion)", OnSelect = () => Toggle(v => v.EnableLlmFallback, (v, x) => v.EnableLlmFallback = x) },
            new() { Label = "LLM Provider",        Description = s.LlmProvider,           OnSelect = () => EditString("LLM provider (claude/openai/gemini/...)", v => v.LlmProvider, (v, x) => v.LlmProvider = x) },
            new() { Label = "Excluded Folders",    Description = string.Join(", ", s.ExcludedFolders),
                                                                                          OnSelect = () => EditList("Excluded folders (comma-separated)", v => v.ExcludedFolders, (v, x) => v.ExcludedFolders = x) },
            new() { Label = "Reset to Defaults",   Description = "overwrites " + path, OnSelect = ResetDefaults },
            new() { Label = "Open Settings File",  Description = path,                   OnSelect = OpenSettingsFile },
        };
    }

    private bool EditString(string label, Func<MediaButlerSettings, string> getter, Action<MediaButlerSettings, string> setter)
    {
        var current = getter(settings.Load());
        var next = ConsoleMenu.Prompt(label, current);
        if (next is null || next == current) return true;
        settings.Update(s => setter(s, next));
        ConsoleMenu.Status("Saved.", ConsoleMenu.Ok);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool Toggle(Func<MediaButlerSettings, bool> getter, Action<MediaButlerSettings, bool> setter)
    {
        var current = getter(settings.Load());
        settings.Update(s => setter(s, !current));
        return true;
    }

    private bool EditList(string label, Func<MediaButlerSettings, string[]> getter, Action<MediaButlerSettings, string[]> setter)
    {
        var current = string.Join(", ", getter(settings.Load()));
        var next = ConsoleMenu.Prompt(label, current);
        if (next is null) return true;
        var parts = next.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        settings.Update(s => setter(s, parts));
        ConsoleMenu.Status("Saved.", ConsoleMenu.Ok);
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool ResetDefaults()
    {
        ConsoleMenu.WriteHeader("Settings", "Reset");
        ConsoleMenu.Status("This overwrites " + settings.FilePath + " with defaults.", ConsoleMenu.Normal);
        ConsoleMenu.Status("Press Y to confirm, any other key to cancel.", ConsoleMenu.Dim);
        var key = Console.ReadKey(intercept: true);
        if (char.ToUpperInvariant(key.KeyChar) == 'Y')
        {
            settings.Save(new MediaButlerSettings());
            ConsoleMenu.Status("Reset.", ConsoleMenu.Ok);
        }
        else
        {
            ConsoleMenu.Status("Cancelled.", ConsoleMenu.Dim);
        }
        ConsoleMenu.WaitForKey();
        return true;
    }

    private bool OpenSettingsFile()
    {
        var path = settings.FilePath;
        if (!File.Exists(path))
        {
            settings.Save(new MediaButlerSettings());
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ConsoleMenu.Status("Could not open: " + ex.Message, ConsoleMenu.Err);
            ConsoleMenu.WaitForKey();
        }
        return true;
    }

    private static string Bool(bool b) => b ? "true" : "false";
}
