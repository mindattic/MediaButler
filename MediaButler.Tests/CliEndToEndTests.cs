using System.Diagnostics;
using MediaButler.Tests.Fixtures;
using NUnit.Framework;

namespace MediaButler.Tests;

/// <summary>
/// Black-box tests that spawn the built <c>mediabutler.exe</c> against a
/// synthesized pathological library. Exercises the same surface a cron job
/// or interactive user would hit — Spectre.Console arg parsing, exit codes,
/// stdout/stderr capture, audit log on disk.
///
/// <para>These are deliberately small in count compared to the in-process
/// integration tests. Spawning a process per test is slow; the goal here is
/// to lock down the CLI contract (exit codes, --version, --dry-run, --source
/// override), not to re-cover every classification case.</para>
///
/// <para>The MediaButler CLI reads its settings from %APPDATA% by default,
/// which would pick up the developer's real configuration. Each test points
/// the CLI at the fixture's source via the <c>--source</c> override but also
/// needs the TV/Movies destinations under the fixture root, which the
/// existing CLI doesn't accept as flags. So most CLI tests run against the
/// SCAN and VERSION subcommands (which don't mutate anything) and the
/// in-process tests cover the mutating stages instead.</para>
/// </summary>
[TestFixture]
public class CliEndToEndTests
{
    private string exePath = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        exePath = FindBuiltExe();
        Assert.That(File.Exists(exePath), Is.True,
            $"mediabutler.exe not found at {exePath} — build the solution first");
    }

    [Test]
    public void Version_subcommand_prints_version_and_exits_zero()
    {
        var r = RunCli("version");
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitCode, Is.EqualTo(0), $"stderr: {r.StdErr}");
            Assert.That(r.StdOut, Does.Contain("MediaButler").Or.Contain("mediabutler"),
                "version output should mention the product");
        });
    }

    [Test]
    public void Bare_double_dash_version_resolves_to_version_subcommand()
    {
        var r = RunCli("--version");
        Assert.That(r.ExitCode, Is.EqualTo(0), $"stderr: {r.StdErr}");
        Assert.That(r.StdOut, Is.Not.Empty);
    }

    [Test]
    public void Short_dash_v_resolves_to_version_subcommand()
    {
        var r = RunCli("-v");
        Assert.That(r.ExitCode, Is.EqualTo(0), $"stderr: {r.StdErr}");
        Assert.That(r.StdOut, Is.Not.Empty);
    }

    [Test]
    public void Dash_v_in_any_argv_position_still_resolves_to_version()
    {
        // Fix #10 in the recent review: --version was only recognised when
        // it was the sole argument. This locks down the generalised behavior.
        var r = RunCli("-v", "scan");
        Assert.That(r.ExitCode, Is.EqualTo(0), $"stderr: {r.StdErr}");
        Assert.That(r.StdOut, Is.Not.Empty);
    }

    [Test]
    public void Scan_against_pathological_fixture_emits_every_input_folder()
    {
        using var lib = new PathologicalLibrary();
        var cases = lib.Populate();

        var r = RunCli("scan", "--source", lib.SourcePath);

        Assert.Multiple(() =>
        {
            // scan is read-only — exit code should be 0 even when there are
            // Extras / Empty / MultiSeasonParent items in the listing.
            Assert.That(r.ExitCode, Is.EqualTo(0), $"stderr: {r.StdErr}\nstdout: {r.StdOut}");

            // Every fixture input folder name should appear in scan output.
            foreach (var c in cases)
            {
                Assert.That(r.StdOut, Does.Contain(c.Input),
                    $"scan output missing fixture folder: {c.Input}");
            }
        });
    }

    [Test]
    public void Unknown_subcommand_returns_nonzero()
    {
        var r = RunCli("this-is-not-a-real-subcommand");
        Assert.That(r.ExitCode, Is.Not.EqualTo(0),
            "an unknown subcommand should fail, not silently succeed");
    }

    /// <summary>Locate the freshly-built CLI binary, walking up from the test bin dir.</summary>
    private static string FindBuiltExe()
    {
        // Walk up from the test assembly to the solution root and pick up
        // the CLI build output. Both projects target net10.0-windows.
        var assembly = typeof(CliEndToEndTests).Assembly.Location;
        var dir      = Path.GetDirectoryName(assembly)!;
        // …\MediaButler.Tests\bin\Debug\net10.0-windows  →  …\MediaButler\bin\Debug\net10.0-windows\mediabutler.exe
        var solutionRoot = new DirectoryInfo(dir).Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(solutionRoot, "MediaButler", "bin", "Debug", "net10.0-windows", "mediabutler.exe");
    }

    private CliResult RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            Assert.Fail("CLI invocation timed out after 60s: " + string.Join(' ', args));
        }
        return new CliResult(proc.ExitCode, stdout, stderr);
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
