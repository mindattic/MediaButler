using MediaButler.Ui;
using MindAttic.Vault.Credentials;

namespace MediaButler.Settings;

/// <summary>
/// Settings sub-menu. Each row reads the current value, lets the user edit it
/// (free-form string, toggle, or list edit), and writes the change back to
/// <c>%APPDATA%\MindAttic\MediaButler\settings.json</c>.
/// </summary>
public sealed class SettingsEditor
{
    private readonly SettingsService settings;

    /// <summary>
    /// MediaButler's own Vault-scoped LLM keys (<c>"mediabutler-{providerId}"</c>) — checked
    /// by <see cref="Llm.LegionFallbackParser"/> before the shared cross-app default. Lives here
    /// (not settings.json) so a key never gets echoed back in plain text via "Open Settings File".
    /// </summary>
    private static readonly ICredentialStore OwnKeys =
        new AppScopedCredentialStore("mediabutler", LlmCredentialStore.Default);

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
            new() { Name = "Extra Sources",       Description = s.ExtraSources.Length == 0 ? "(none)" : string.Join(", ", s.ExtraSources),
                    Tag = (Action)(() => EditList("Extra source folders (comma-separated)", v => v.ExtraSources, (v, x) => v.ExtraSources = x)) },
            new() { Name = "TV Destination",      Description = s.TvDestination,
                    Tag = (Action)(() => EditString("TV destination",      v => v.TvDestination,     (v, x) => v.TvDestination = x)) },
            new() { Name = "Movies Destination",  Description = s.MoviesDestination,
                    Tag = (Action)(() => EditString("Movies destination",  v => v.MoviesDestination, (v, x) => v.MoviesDestination = x)) },
            new() { Name = "Music Destination",   Description = string.IsNullOrWhiteSpace(s.MusicDestination) ? "(disabled — music is flagged, not moved)" : s.MusicDestination,
                    Tag = (Action)(() => EditString("Music destination (empty to disable)", v => v.MusicDestination, (v, x) => v.MusicDestination = x)) },
            new() { Name = "Recursive Sources",   Description = Bool(s.Recursive) + " (also process temp/incomplete container subfolders)",
                    Tag = (Action)(() => Toggle(v => v.Recursive, (v, x) => v.Recursive = x)) },
            new() { Name = "FileBot Path",        Description = s.FileBotPath,
                    Tag = (Action)(() => EditString("FileBot path",        v => v.FileBotPath,       (v, x) => v.FileBotPath = x)) },
            new() { Name = "FileBot Trust All Certs", Description = Bool(s.FileBotTrustAll) + " (bypass SSL — use when FileBot hits SunCertPathBuilderException)",
                    Tag = (Action)(() => Toggle(v => v.FileBotTrustAll, (v, x) => v.FileBotTrustAll = x)) },
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
            new() { Name = "LLM API Key",         Description = DescribeProviderKey(s.LlmProvider, OwnKeys),
                    Tag = (Action)EditProviderKey },
            new() { Name = "Excluded Folders",    Description = string.Join(", ", s.ExcludedFolders),
                    Tag = (Action)(() => EditList("Excluded folders (comma-separated)", v => v.ExcludedFolders, (v, x) => v.ExcludedFolders = x)) },
            new() { Name = "Open Variations File", Description = Media.VariationCatalog.ResolvePath(s) + " (movie/tv/music sections; hand-edits pin a name's category)",
                    Tag = (Action)OpenVariationsFile },
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

    internal static string DescribeProviderKey(string providerId, ICredentialStore ownKeys)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return "(set LLM Provider first)";
        var hasKey = !string.IsNullOrWhiteSpace(ownKeys.GetKey(providerId));
        return hasKey ? "Configured" : "Not configured (falls back to the shared default)";
    }

    /// <summary>
    /// Edits this app's own Vault-scoped key for the currently-configured
    /// <see cref="MediaButlerSettings.LlmProvider"/> — never the shared cross-app id, and never
    /// written to settings.json (so it can't be echoed back via "Open Settings File"). Blank input
    /// keeps the existing key; "clear" removes it and falls back to the shared default.
    /// </summary>
    private void EditProviderKey()
    {
        var providerId = settings.Load().LlmProvider;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            Status.Print("Set an LLM Provider first.", Theme.Err);
            Screen.PressAnyKey();
            return;
        }

        var hint = DescribeProviderKey(providerId, OwnKeys) == "Configured"
            ? "configured — blank keeps it, type clear to remove"
            : "not configured";
        var input = Screen.Prompt($"API key for '{providerId}'", hint);
        if (input is null || input == hint) return;

        if (string.Equals(input, "clear", StringComparison.OrdinalIgnoreCase))
        {
            OwnKeys.SetKey(providerId, "");
            Status.Print("Cleared — falls back to the shared default.", Theme.Ok);
        }
        else
        {
            OwnKeys.SetKey(providerId, input);
            Status.Print("Saved.", Theme.Ok);
        }
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

    private void OpenVariationsFile()
    {
        var path = Media.VariationCatalog.ResolvePath(settings.Load());
        if (!File.Exists(path))
        {
            // Materialize an empty sectioned file so the user has something to append to.
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{\n  \"movie\": [],\n  \"tv\": [],\n  \"music\": [],\n  \"unknown\": []\n}\n");
            }
            catch (Exception ex)
            {
                Status.Print("Could not create: " + ex.Message, Theme.Err);
                Screen.PressAnyKey();
                return;
            }
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
