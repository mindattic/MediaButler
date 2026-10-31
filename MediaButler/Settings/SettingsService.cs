using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;

namespace MediaButler.Settings;

/// <summary>
/// Read-side facade over MediaButler's <see cref="ISettingsStore{T}"/>. Settings live roaming
/// under <c>%APPDATA%\MindAttic\MediaButler\settings.json</c> and are edited only in
/// MindAttic.Vault.Desktop (LAMP, MB-T27); MediaButler itself never writes them except to seed
/// first-run defaults. Every load goes through <see cref="ISettingsStore{T}.LoadOrSeed"/>, which also
/// keeps <c>settings.manifest.json</c> current so Vault.Desktop can render the MediaButler tab.
/// </summary>
public sealed class SettingsService
{
    public const string AppName = MediaButlerSettings.AppId;

    private readonly JsonSettingsStore<MediaButlerSettings> store;

    /// <summary>The app's store under the current roaming root (<c>MINDATTIC_VAULT_ROAMING_ROOT</c> honored).</summary>
    public SettingsService() : this(JsonSettingsStore<MediaButlerSettings>.ForSettingsApp()) { }

    /// <summary>A store at an explicit location (tests).</summary>
    public SettingsService(JsonSettingsStore<MediaButlerSettings> store) =>
        this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Absolute path to the settings file (shown by <c>status</c>).</summary>
    public string FilePath => store.FilePath;

    /// <summary>The folder holding settings.json and settings.manifest.json.</summary>
    public string Directory => store.Directory;

    /// <summary>Load current settings, seeding today's defaults (and the manifest) on first run.</summary>
    public MediaButlerSettings Load() => store.LoadOrSeed();

    /// <summary>
    /// The one value MediaButler writes itself: the WPF Run page's dry-run safety toggle (forced on at
    /// every GUI launch, flipped by its header switch). It is a run control that happens to persist as
    /// <see cref="MediaButlerSettings.DryRun"/>, exactly as before LAMP; every other setting is edited
    /// only in Vault.Desktop.
    /// </summary>
    public void SetDryRun(bool dryRun) => store.Update(s => s.DryRun = dryRun);

    /// <summary>Folder that contains the settings file (created if missing).</summary>
    public string EnsureFolder() => VaultPaths.Ensure(store.Directory);
}
