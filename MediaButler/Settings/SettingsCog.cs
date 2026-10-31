using MindAttic.Vault.Launch;

namespace MediaButler.Settings;

/// <summary>
/// What every MediaButler "Settings" entry point does (the WPF cog, the console menu's Settings
/// row): opens MindAttic.Vault.Desktop at the MediaButler tab (LAMP, MB-T27). MediaButler has no
/// settings editor of its own.
/// </summary>
public static class SettingsCog
{
    /// <summary>The outcome; <see cref="Message"/> is what to show the user.</summary>
    public sealed record Result(bool Opened, string Message);

    /// <summary>
    /// Opens Vault.Desktop at the MediaButler tab, scrolled to <paramref name="setting"/> when given
    /// (a full name such as <c>MindAttic:Settings:MediaButler:fileBotPath</c>).
    /// </summary>
    public static Result Open(string? setting = null)
    {
        if (VaultSettings.Open(MediaButlerSettings.AppId, setting))
            return new Result(true, "Opened MediaButler settings in MindAttic Vault.");

        string file;
        try { file = new SettingsService().FilePath; }
        catch { file = "%APPDATA%\\MindAttic\\MediaButler\\settings.json"; }
        return new Result(false,
            "MindAttic Vault isn't installed, so settings can't be opened. Install MindAttic.Vault.Desktop " +
            "(or set MINDATTIC_VAULT_DESKTOP_EXE). Settings file: " + file);
    }
}
