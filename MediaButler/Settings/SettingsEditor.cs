using MediaButler.Ui;

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
        while (true)
        {
            var items = BuildItems();
            Screen.Header("Settings");
            var sel = Menu.Prompt("[cyan1]Settings — choose a field to edit:[/]", items, allowBack: true);
            if (sel is null) return;
            if (sel.Tag is Action action)
            {
                action();
            }
        }
    }

    private IReadOnlyList<MenuItem> BuildItems()
    {
        var s = settings.Load();
        var path = settings.FilePath;

        return new List<MenuItem>
        {
            new() { Name = "Source Path",         Description = s.SourcePath,
                    Tag = (Action)(() => EditString("Source path",         v => v.SourcePath,        (v, x) => v.SourcePath = x)) },
            new() { Name = "TV Destination",      Description = s.TvDestination,
                    Tag = (Action)(() => EditString("TV destination",      v => v.TvDestination,     (v, x) => v.TvDestination = x)) },
            new() { Name = "Movies Destination",  Description = s.MoviesDestination,
                    Tag = (Action)(() => EditString("Movies destination",  v => v.MoviesDestination, (v, x) => v.MoviesDestination = x)) },
            new() { Name = "FileBot Path",        Description = s.FileBotPath,
                    Tag = (Action)(() => EditString("FileBot path",        v => v.FileBotPath,       (v, x) => v.FileBotPath = x)) },
            new() { Name = "Subtitle Language",   Description = s.SubtitleLanguage,
                    Tag = (Action)(() => EditString("Subtitle language",   v => v.SubtitleLanguage,  (v, x) => v.SubtitleLanguage = x)) },
            new() { Name = "Dry Run",             Description = Bool(s.DryRun) + " (no disk mutations when on)",
                    Tag = (Action)(() => Toggle(v => v.DryRun, (v, x) => v.DryRun = x)) },
            new() { Name = "Enable Subtitles",    Description = Bool(s.EnableSubtitles),
                    Tag = (Action)(() => Toggle(v => v.EnableSubtitles, (v, x) => v.EnableSubtitles = x)) },
            new() { Name = "Rename TV Episodes",  Description = Bool(s.RenameEpisodes),
                    Tag = (Action)(() => Toggle(v => v.RenameEpisodes,  (v, x) => v.RenameEpisodes = x)) },
            new() { Name = "Rename Movies",       Description = Bool(s.RenameMovies),
                    Tag = (Action)(() => Toggle(v => v.RenameMovies,    (v, x) => v.RenameMovies = x)) },
            new() { Name = "Fetch Artwork",       Description = Bool(s.FetchArtwork),
                    Tag = (Action)(() => Toggle(v => v.FetchArtwork,    (v, x) => v.FetchArtwork = x)) },
            new() { Name = "Enable LLM Fallback", Description = Bool(s.EnableLlmFallback) + " (Legion)",
                    Tag = (Action)(() => Toggle(v => v.EnableLlmFallback, (v, x) => v.EnableLlmFallback = x)) },
            new() { Name = "LLM Provider",        Description = s.LlmProvider,
                    Tag = (Action)(() => EditString("LLM provider (claude/openai/gemini/...)", v => v.LlmProvider, (v, x) => v.LlmProvider = x)) },
            new() { Name = "Excluded Folders",    Description = string.Join(", ", s.ExcludedFolders),
                    Tag = (Action)(() => EditList("Excluded folders (comma-separated)", v => v.ExcludedFolders, (v, x) => v.ExcludedFolders = x)) },
            new() { Name = "Reset to Defaults",   Description = "overwrites " + path,
                    Tag = (Action)ResetDefaults },
            new() { Name = "Open Settings File",  Description = path,
                    Tag = (Action)OpenSettingsFile },
        };
    }

    private void EditString(string label, Func<MediaButlerSettings, string> getter, Action<MediaButlerSettings, string> setter)
    {
        var current = getter(settings.Load());
        var next = Screen.Prompt(label, current);
        if (next is null || next == current) return;
        settings.Update(s => setter(s, next));
        Status.Print("Saved.", Theme.Ok);
        Screen.PressAnyKey();
    }

    private void Toggle(Func<MediaButlerSettings, bool> getter, Action<MediaButlerSettings, bool> setter)
    {
        var current = getter(settings.Load());
        settings.Update(s => setter(s, !current));
    }

    private void EditList(string label, Func<MediaButlerSettings, string[]> getter, Action<MediaButlerSettings, string[]> setter)
    {
        var current = string.Join(", ", getter(settings.Load()));
        var next = Screen.Prompt(label, current);
        if (next is null) return;
        var parts = next.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        settings.Update(s => setter(s, parts));
        Status.Print("Saved.", Theme.Ok);
        Screen.PressAnyKey();
    }

    private void ResetDefaults()
    {
        Screen.Header("Settings", "Reset");
        Status.Print("This overwrites " + settings.FilePath + " with defaults.", Theme.Normal);
        Status.Print("Press Y to confirm, any other key to cancel.", Theme.Dim);
        ConsoleKeyInfo key;
        try { key = Console.ReadKey(intercept: true); }
        catch (InvalidOperationException) { return; }
        if (char.ToUpperInvariant(key.KeyChar) == 'Y')
        {
            settings.Save(new MediaButlerSettings());
            Status.Print("Reset.", Theme.Ok);
        }
        else
        {
            Status.Print("Cancelled.", Theme.Dim);
        }
        Screen.PressAnyKey();
    }

    private void OpenSettingsFile()
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
            Status.Print("Could not open: " + ex.Message, Theme.Err);
            Screen.PressAnyKey();
        }
    }

    private static string Bool(bool b) => b ? "true" : "false";
}
