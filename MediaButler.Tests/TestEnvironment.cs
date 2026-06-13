using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Assembly-wide environment guard. The MediaScanner now appends every name
/// it classifies into the persistent variation catalog
/// (<c>%APPDATA%\MindAttic\MediaButler\variations.json</c> by default) — tests
/// must never pollute the developer's real catalog, so the whole run is
/// redirected to a temp file via the same environment variable the CLI
/// end-to-end tests use per-process.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    private string variationsPath = null!;

    [OneTimeSetUp]
    public void RedirectVariationCatalog()
    {
        variationsPath = Path.Combine(
            Path.GetTempPath(), "mediabutler-tests-variations-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("MEDIABUTLER_VARIATIONS_PATH", variationsPath);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        Environment.SetEnvironmentVariable("MEDIABUTLER_VARIATIONS_PATH", null);
        try { if (File.Exists(variationsPath)) File.Delete(variationsPath); } catch { /* best-effort */ }
    }
}
