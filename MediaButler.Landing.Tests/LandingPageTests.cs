using Microsoft.Playwright;
using NUnit.Framework;

namespace MediaButler.Landing.Tests;

/// <summary>
/// Headless-browser tests against the MediaButler landing page
/// (<c>index.htm</c>, deployed to <c>mindattic.com/mediabutler.htm</c>).
/// Closest equivalent in .NET-land to a Cypress suite — Playwright drives a
/// real Chromium against the local file, asserts visible content, checks
/// links resolve, and watches the console for runtime errors.
///
/// <para><b>Before running for the first time:</b> Playwright needs its
/// browser binaries installed. After the project builds, run:</para>
/// <code>
/// pwsh MediaButler.Landing.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
/// </code>
/// <para>If the binaries aren't installed, the OneTimeSetUp hook ignores
/// (does not fail) every test with a clear message.</para>
/// </summary>
[TestFixture]
[Category("Landing")]
public class LandingPageTests
{
    private IPlaywright? playwright;
    private IBrowser? browser;
    private string landingUrl = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Locate the index.htm at the repo root so the test runs against the
        // exact file MindAttic.Deploy will upload, not a stale copy.
        var here = Path.GetDirectoryName(typeof(LandingPageTests).Assembly.Location)!;
        var repoRoot = new DirectoryInfo(here).Parent!.Parent!.Parent!.Parent!.FullName;
        var indexPath = Path.Combine(repoRoot, "index.htm");
        if (!File.Exists(indexPath))
        {
            Assert.Ignore($"index.htm not found at {indexPath} — cannot test landing page.");
        }
        landingUrl = "file:///" + indexPath.Replace('\\', '/');

        try
        {
            playwright = await Playwright.CreateAsync();
            browser    = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                "Playwright browser binaries are not installed. Run:\n" +
                "  pwsh MediaButler.Landing.Tests/bin/Debug/net10.0/playwright.ps1 install chromium");
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
    }

    [Test]
    public async Task Page_loads_with_correct_title_and_main_heading()
    {
        await using var ctx = await browser!.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(landingUrl);

        Assert.Multiple(async () =>
        {
            Assert.That(await page.TitleAsync(), Does.Contain("MediaButler"));
            await Assertions.Expect(page.Locator("h1#mediabutler").First).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".tagline").First).ToContainTextAsync("FileBot");
        });
    }

    [Test]
    public async Task Hero_CTA_buttons_target_expected_links()
    {
        await using var ctx = await browser!.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(landingUrl);

        var openBtn = page.Locator(".btn.btn-primary").First;
        var githubBtn = page.Locator(".btn.btn-secondary").First;

        Assert.Multiple(async () =>
        {
            await Assertions.Expect(openBtn).ToHaveAttributeAsync("href", "https://mindattic.com/mediabutler/");
            await Assertions.Expect(githubBtn).ToHaveAttributeAsync("href", "https://github.com/mindattic/MediaButler");
            await Assertions.Expect(openBtn).ToHaveAttributeAsync("rel", "noopener noreferrer");
            await Assertions.Expect(githubBtn).ToHaveAttributeAsync("rel", "noopener noreferrer");
        });
    }

    [Test]
    public async Task Rendered_readme_includes_the_canonical_use_case_examples()
    {
        // The README's "what it does" section lists the actual pathological
        // folder shapes MediaButler is designed to handle. These references
        // are the user's first signal that "yes, this tool fixes my mess."
        // If they disappear during a doc rewrite, the landing page loses its
        // core pitch.
        await using var ctx = await browser!.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(landingUrl);

        var readme = page.Locator("article.readme");
        Assert.Multiple(async () =>
        {
            await Assertions.Expect(readme).ToContainTextAsync("Better.Call.Saul");
            await Assertions.Expect(readme).ToContainTextAsync("FileBot");
            await Assertions.Expect(readme).ToContainTextAsync("Plex");
            await Assertions.Expect(readme).ToContainTextAsync("Season");
        });
    }

    [Test]
    public async Task Page_has_no_runtime_console_errors()
    {
        await using var ctx = await browser!.NewContextAsync();
        var page = await ctx.NewPageAsync();

        var consoleErrors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error") consoleErrors.Add(msg.Text);
        };

        var pageErrors = new List<string>();
        page.PageError += (_, msg) => pageErrors.Add(msg);

        await page.GotoAsync(landingUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Multiple(() =>
        {
            Assert.That(consoleErrors, Is.Empty,
                "landing page emitted console.error: " + string.Join(" | ", consoleErrors));
            Assert.That(pageErrors, Is.Empty,
                "landing page threw uncaught exceptions: " + string.Join(" | ", pageErrors));
        });
    }

    [Test]
    public async Task All_internal_anchor_targets_resolve_to_real_ids_on_the_page()
    {
        // Catches broken table-of-contents links — typical after a README
        // rewrite when section headings change but the anchors in prose
        // didn't keep up.
        await using var ctx = await browser!.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(landingUrl);

        var anchorTargets = await page.Locator("a[href^='#']").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('href'))");

        var dangling = new List<string>();
        foreach (var href in anchorTargets)
        {
            if (string.IsNullOrEmpty(href) || href == "#") continue;
            var id = href.TrimStart('#');
            // Use the attribute selector so we don't have to escape CSS
            // identifier characters by hand — markdown slugs occasionally
            // contain dots or other non-trivial chars.
            var found = await page.Locator($"[id='{id.Replace("'", "\\'")}']").CountAsync();
            if (found == 0) dangling.Add(href);
        }

        Assert.That(dangling, Is.Empty,
            "internal links point to ids that don't exist: " + string.Join(", ", dangling));
    }
}
