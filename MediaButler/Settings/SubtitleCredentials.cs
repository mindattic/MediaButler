using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Configuration;

namespace MediaButler.Settings;

/// <summary>
/// OpenSubtitles login resolved from the MindAttic Vault chain. Credentials
/// must <b>never</b> live in <c>settings.json</c>; instead place them in the
/// canonical Subtitles credential file
/// <c>%APPDATA%\MindAttic\Subtitles\providers.json</c> (or supply them via
/// environment variables for CI / App Service):
///
/// <code>
/// // %APPDATA%\MindAttic\Subtitles\providers.json
/// {
///   "OpenSubtitles": { "user": "ryandebraal", "password": "***" }
/// }
/// </code>
/// <para>
/// The env-var equivalents are <c>MindAttic__Vault__Subtitles__OpenSubtitles__user</c>
/// and <c>MindAttic__Vault__Subtitles__OpenSubtitles__password</c>.
/// </para>
/// <para>
/// When both <see cref="User"/> and <see cref="Password"/> resolve to non-empty
/// values, <see cref="FileBot.FileBotClient.GetSubtitles"/> passes them to
/// FileBot via <c>--def osdb.user=… osdb.pwd=…</c> so OpenSubtitles can
/// authenticate without relying on FileBot Preferences being pre-configured.
/// </para>
/// </summary>
public sealed record SubtitleCredentials
{
    /// <summary>Configuration section path: <c>MindAttic:Vault:Subtitles:OpenSubtitles</c>.</summary>
    public const string Section = VaultConfigurationKeys.VaultSection + ":Subtitles:OpenSubtitles";

    public string? User { get; init; }
    public string? Password { get; init; }

    /// <summary>True when both fields are present and non-empty.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// Build the configuration chain (Vault files &gt; environment variables)
    /// and resolve credentials. Both legs are optional so the loader never
    /// throws — missing creds simply yield <see cref="IsComplete"/> = false.
    /// Reads <c>%APPDATA%\MindAttic\Subtitles\providers.json</c>.
    /// </summary>
    public static SubtitleCredentials Load()
    {
        var builder = new ConfigurationBuilder();
        builder.AddMindAtticVaultFiles();
        builder.AddEnvironmentVariables();
        return Load(builder.Build());
    }

    /// <summary>Resolve credentials from an existing <see cref="IConfiguration"/> (used by tests).</summary>
    public static SubtitleCredentials Load(IConfiguration config)
    {
        var section = config.GetSection(Section);
        return new SubtitleCredentials
        {
            User     = section["user"],
            Password = section["password"],
        };
    }
}
