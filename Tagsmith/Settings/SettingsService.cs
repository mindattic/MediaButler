using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;

namespace Tagsmith.Settings;

/// <summary>
/// Persistence facade around <see cref="JsonSettingsStore{T}"/>. Settings live
/// roaming under <c>%APPDATA%\MindAttic\Tagsmith\settings.json</c>. The first
/// call writes a defaults file so the user has something concrete to edit.
/// </summary>
public sealed class SettingsService
{
    public const string AppName = "Tagsmith";

    private readonly JsonSettingsStore<TagsmithSettings> store = JsonSettingsStore<TagsmithSettings>.ForApp(AppName);

    /// <summary>Absolute path to the settings file (used by the Settings menu header).</summary>
    public string FilePath => store.FilePath;

    /// <summary>Load current settings, creating a defaults file on first run.</summary>
    public TagsmithSettings Load()
    {
        if (!store.Exists())
        {
            var defaults = new TagsmithSettings();
            store.Save(defaults);
            return defaults;
        }
        return store.Load();
    }

    /// <summary>Persist a settings instance to disk.</summary>
    public void Save(TagsmithSettings settings) => store.Save(settings);

    /// <summary>Read-modify-write helper used by the Settings menu.</summary>
    public TagsmithSettings Update(Action<TagsmithSettings> mutate) => store.Update(mutate);

    /// <summary>Folder that contains the settings file (created if missing).</summary>
    public string EnsureFolder() => VaultPaths.Ensure(store.Directory);
}
