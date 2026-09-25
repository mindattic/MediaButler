using NUnit.Framework;

namespace MediaButler.Wpf.AccessibilityTests;

/// <summary>
/// Assembly-wide environment guard. Rendering <c>Run</c>/<c>Settings</c> (via
/// bUnit or the Blazor Server harness) constructs a real <c>SettingsService</c>,
/// which without this override would read/write the developer's real
/// <c>%APPDATA%\MindAttic\MediaButler\settings.json</c> — <c>Run.OnInitialized</c>
/// unconditionally forces and saves <c>DryRun = true</c> on every render. Redirect
/// the whole run to a temp roaming root, mirroring the variation-catalog
/// hermeticity guard in <c>MediaButler.Tests/TestEnvironment.cs</c>.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    private string roamingRoot = null!;

    [OneTimeSetUp]
    public void RedirectRoamingRoot()
    {
        roamingRoot = Path.Combine(
            Path.GetTempPath(), "mediabutler-a11y-tests-roaming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(roamingRoot);
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_ROAMING_ROOT", roamingRoot);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_ROAMING_ROOT", null);
        try { if (Directory.Exists(roamingRoot)) Directory.Delete(roamingRoot, recursive: true); } catch { /* best-effort */ }
    }
}
