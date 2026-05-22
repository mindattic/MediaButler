using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;

namespace MediaButler.Settings;

/// <summary>
/// Persistence facade around <see cref="JsonSettingsStore{T}"/>. Settings live
/// roaming under <c>%APPDATA%\MindAttic\MediaButler\settings.json</c>. The first
/// call writes a defaults file so the user has something concrete to edit.
/// </summary>
public sealed class SettingsService
{
    public const string AppName = "MediaButler";

    private readonly JsonSettingsStore<MediaButlerSettings> store = JsonSettingsStore<MediaButlerSettings>.ForApp(AppName);

    /// <summary>Absolute path to the settings file (used by the Settings menu header).</summary>
    public string FilePath => store.FilePath;

    /// <summary>Load current settings, creating a defaults file on first run.</summary>
    public MediaButlerSettings Load()
    {
        if (!store.Exists())
        {
            var defaults = new MediaButlerSettings();
            store.Save(defaults);
            return defaults;
        }
        return store.Load();
    }

    /// <summary>Persist a settings instance to disk.</summary>
    public void Save(MediaButlerSettings settings) => store.Save(settings);

    /// <summary>Read-modify-write helper used by the Settings menu.</summary>
    public MediaButlerSettings Update(Action<MediaButlerSettings> mutate) => store.Update(mutate);

    /// <summary>Folder that contains the settings file (created if missing).</summary>
    public string EnsureFolder() => VaultPaths.Ensure(store.Directory);
}
