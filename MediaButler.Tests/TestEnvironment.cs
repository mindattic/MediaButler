using MindAttic.Vault.Paths;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Assembly-wide environment guard. The MediaScanner appends every name it classifies into the
/// persistent variation catalog (<c>%APPDATA%\MindAttic\MediaButler\variations.json</c> by default),
/// and every settings load seeds <c>settings.json</c> and writes <c>settings.manifest.json</c>
/// (LAMP, MB-T27). Tests must never touch the developer's real files (M.VALT-LAW-6), so the whole
/// run, including the CLI processes the end-to-end tests spawn (they inherit this environment),
/// is redirected to temp roaming/local roots and a temp variation catalog.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    private string variationsPath = null!;
    private string sandbox = null!;

    /// <summary>The temp roaming root every test in the assembly runs under.</summary>
    public static string RoamingRoot { get; private set; } = null!;

    [OneTimeSetUp]
    public void RedirectUserFolders()
    {
        variationsPath = Path.Combine(
            Path.GetTempPath(), "mediabutler-tests-variations-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("MEDIABUTLER_VARIATIONS_PATH", variationsPath);

        sandbox = Path.Combine(Path.GetTempPath(), "mediabutler-tests-" + Guid.NewGuid().ToString("N"));
        RoamingRoot = Path.Combine(sandbox, "Roaming");
        Directory.CreateDirectory(RoamingRoot);
        Directory.CreateDirectory(Path.Combine(sandbox, "Local"));
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, RoamingRoot);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(sandbox, "Local"));
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        Environment.SetEnvironmentVariable("MEDIABUTLER_VARIATIONS_PATH", null);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, null);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, null);
        try { if (File.Exists(variationsPath)) File.Delete(variationsPath); } catch { /* best-effort */ }
        try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true); } catch { /* best-effort */ }
    }
}
