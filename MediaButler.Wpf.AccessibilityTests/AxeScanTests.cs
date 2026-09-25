using System.Diagnostics;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NUnit.Framework;

namespace MediaButler.Wpf.AccessibilityTests;

/// <summary>
/// Real-browser WCAG 2.2 AA scan. Launches the real
/// <c>MediaButler.Wpf.AccessibilityTests.Harness</c> executable as a
/// subprocess (a tiny standalone Blazor Server host wrapping the actual
/// <c>MediaButler.Wpf.UI.App</c> component tree — same production markup the
/// WPF shell hosts), drives it with headless Chromium via Playwright (same
/// dependency <c>MediaButler.Landing.Tests</c> already uses), and asserts
/// zero axe-core violations tagged wcag2a/wcag2aa/wcag22aa.
///
/// <para><b>Before running for the first time:</b> Playwright needs its browser
/// binaries installed. After the project builds, run:</para>
/// <code>
/// pwsh MediaButler.Wpf.AccessibilityTests/bin/Debug/net10.0-windows/playwright.ps1 install chromium
/// </code>
/// <para>If the binaries — or the harness executable — aren't available,
/// OneTimeSetUp ignores the whole fixture.</para>
/// </summary>
[TestFixture]
public class AxeScanTests
{
    private static readonly AxeRunOptions Wcag22AaOptions = new()
    {
        RunOnly = new RunOnlyOptions
        {
            Type = "tag",
            Values = ["wcag2a", "wcag2aa", "wcag22aa"],
        },
    };

    private static readonly Regex ListeningOnPattern = new(@"Now listening on:\s*(http://\S+)", RegexOptions.Compiled);

    private Process? harness;
    private string baseUrl = "";
    private IPlaywright? playwright;
    private IBrowser? browser;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var harnessExePath = LocateHarnessExecutable();
        if (!File.Exists(harnessExePath))
        {
            Assert.Ignore(
                $"MediaButler.Wpf.AccessibilityTests.Harness executable not found at {harnessExePath}. " +
                "Build the MediaButler.Wpf.AccessibilityTests.Harness project before running this fixture.");
            return;
        }

        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                "Playwright browser binaries are not installed. Run:\n" +
                "  pwsh MediaButler.Wpf.AccessibilityTests/bin/Debug/net10.0-windows/playwright.ps1 install chromium");
            return;
        }

        baseUrl = await StartHarnessAsync(harnessExePath);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
        try { if (harness is { HasExited: false }) harness.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        harness?.Dispose();
    }

    [Test]
    public async Task Run_tab_has_zero_wcag22_aa_violations()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForSelectorAsync("#tab-run");

        var results = await page.RunAxe(Wcag22AaOptions);
        AssertNoViolations(results);
    }

    [Test]
    public async Task Settings_tab_has_zero_wcag22_aa_violations()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.ClickAsync("#tab-settings");
        await page.WaitForSelectorAsync("#panel-settings:not([style*='display:none'])");

        var results = await page.RunAxe(Wcag22AaOptions);
        AssertNoViolations(results);
    }

    private static void AssertNoViolations(AxeResult results)
    {
        var violations = results.Violations;
        if (violations is null || violations.Count() == 0) return;
        var details = string.Join("\n", violations.Select(v =>
            $"- [{v.Impact}] {v.Id}: {v.Help} ({v.Nodes.Count()} node(s)) {v.HelpUrl}\n" +
            string.Join("\n", v.Nodes.Select(n =>
                "    " + string.Join(",", n.Target) + " -> " + n.Html + "\n      " +
                string.Join(" | ", n.All.Concat(n.Any).Concat(n.None).Select(c => c.Message))))));
        Assert.Fail($"axe-core found {violations.Count()} violation(s):\n{details}");
    }

    /// <summary>Starts the harness .exe with a dynamic loopback port and returns the URL Kestrel bound to.</summary>
    private async Task<string> StartHarnessAsync(string exePath)
    {
        var psi = new ProcessStartInfo(exePath, "--urls http://127.0.0.1:0")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        harness = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the accessibility harness.");

        var tcs = new TaskCompletionSource<string>();
        harness.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var match = ListeningOnPattern.Match(e.Data);
            if (match.Success) tcs.TrySetResult(match.Groups[1].Value);
        };
        harness.BeginOutputReadLine();
        harness.BeginErrorReadLine();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != tcs.Task)
            throw new TimeoutException("Accessibility harness did not report a listening URL within 30s.");
        return tcs.Task.Result;
    }

    private static string LocateHarnessExecutable()
    {
        var assembly = typeof(AxeScanTests).Assembly.Location;
        var dir = Path.GetDirectoryName(assembly)!;
        // …\MediaButler.Wpf.AccessibilityTests\bin\Debug\net10.0-windows → solution root
        var solutionRoot = new DirectoryInfo(dir).Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(solutionRoot, "MediaButler.Wpf.AccessibilityTests.Harness", "bin", "Debug",
            "net10.0-windows", "MediaButler.Wpf.AccessibilityTests.Harness.exe");
    }
}
