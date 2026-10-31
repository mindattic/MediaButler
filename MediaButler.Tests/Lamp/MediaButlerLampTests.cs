using System.Diagnostics;
using System.Net;
using System.Text.Json;
using MediaButler.Commands;
using MediaButler.Llm;
using MediaButler.Pipeline;
using MediaButler.Settings;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Launch;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;

namespace MediaButler.Tests.Lamp;

/// <summary>
/// LAMP (MB-T27) app-level checks: the manifest, first-run defaults, the --write-settings-manifest
/// switch, LLM key failover through the shared named lists, the one-time app-scoped key import, the
/// settings cog opening MindAttic Vault, and the old settings UI being gone.
/// </summary>
[TestFixture]
public class MediaButlerLampTests : LampTestBase
{
    // ---- Manifest -------------------------------------------------------------------------

    [Test]
    public void Manifest_builds_with_every_setting_labeled_grouped_and_helped()
    {
        var manifest = SettingsManifestBuilder.Build<MediaButlerSettings>();

        Assert.Multiple(() =>
        {
            Assert.That(manifest.AppId, Is.EqualTo("MediaButler"));
            Assert.That(manifest.ConfigurationPrefix, Is.EqualTo(Prefix));
            Assert.That(manifest.Fields.Select(f => f.Property),
                Is.EquivalentTo(typeof(MediaButlerSettings).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))),
                "every setting appears once, under its JSON name");
            foreach (var f in manifest.Fields)
            {
                Assert.That(f.Label, Is.Not.Empty, f.Name);
                Assert.That(f.Help, Is.Not.Null.And.Not.Empty, f.Name);
                Assert.That(f.Group, Is.Not.EqualTo(SettingsManifest.DefaultGroup), f.Name);
                Assert.That(f.Kind, Is.Not.EqualTo(SettingKind.Unknown), f.Name);
                Assert.That(f.Name, Is.EqualTo($"{Prefix}:{f.Property}"));
            }
        });
        Assert.That(manifest.Fields.Select(f => f.Group).Distinct(),
            Is.EquivalentTo(new[] { "Folders", "FileBot", "Subtitles", "Safety", "File types", "Name parsing" }));
    }

    [Test]
    public void Manifest_has_no_secret_fields_and_declares_the_claude_key()
    {
        var manifest = SettingsManifestBuilder.Build<MediaButlerSettings>();
        var secretish = new[] { "key", "password", "token", "secret" };

        Assert.That(manifest.Fields.Where(f => secretish.Any(w => f.Property.Contains(w, StringComparison.OrdinalIgnoreCase))), Is.Empty);
        Assert.That(manifest.Credentials.Select(c => c.ProviderId), Is.EquivalentTo(new[] { "claude" }));
    }

    // ---- Defaults ---------------------------------------------------------------------------

    [Test]
    public void LoadOrSeed_on_an_empty_folder_seeds_exactly_todays_defaults_and_the_manifest()
    {
        var loaded = new SettingsService().Load();

        Assert.That(File.Exists(SettingsFile), Is.True);
        Assert.That(File.Exists(ManifestFile), Is.True);
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        var r = doc.RootElement;
        // The defaults MediaButler used before LAMP (pre-MB-T27 MediaButlerSettings initializers).
        Assert.Multiple(() =>
        {
            Assert.That(r.GetProperty("sourcePath").GetString(), Is.EqualTo(@"M:\Torrents"));
            Assert.That(r.GetProperty("extraSources").GetArrayLength(), Is.EqualTo(0));
            Assert.That(r.GetProperty("tvDestination").GetString(), Is.EqualTo(@"M:\TV"));
            Assert.That(r.GetProperty("moviesDestination").GetString(), Is.EqualTo(@"M:\Movies"));
            Assert.That(r.GetProperty("musicDestination").GetString(), Is.EqualTo(""));
            Assert.That(r.GetProperty("recursive").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("fileBotPath").GetString(), Is.EqualTo(@"C:\Program Files\FileBot\filebot.exe"));
            Assert.That(r.GetProperty("fileBotTrustAll").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("subtitleLanguage").GetString(), Is.EqualTo("en"));
            Assert.That(r.GetProperty("enableSubtitles").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("renameEpisodes").GetBoolean(), Is.True);
            Assert.That(r.GetProperty("renameMovies").GetBoolean(), Is.True);
            Assert.That(r.GetProperty("fetchArtwork").GetBoolean(), Is.True);
            Assert.That(r.GetProperty("dryRun").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("limit").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(r.GetProperty("noGuard").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("enableLlmFallback").GetBoolean(), Is.False);
            Assert.That(r.GetProperty("llmProvider").GetString(), Is.EqualTo("claude"));
            Assert.That(r.GetProperty("excludedFolders").EnumerateArray().Select(e => e.GetString()),
                Is.EqualTo(new[] { "temp", ".temp", "incomplete", "complete", "_unsorted" }));
            Assert.That(r.GetProperty("videoExtensions").GetArrayLength(), Is.EqualTo(25));
            Assert.That(r.GetProperty("emptyDeleteSafetyBytes").GetInt64(), Is.EqualTo(1L * 1024 * 1024));
            Assert.That(r.GetProperty("audioExtensions").GetArrayLength(), Is.EqualTo(12));
            Assert.That(r.GetProperty("duplicateMovieAction").GetString(), Is.EqualTo("KeepLargest"));
            Assert.That(r.GetProperty("duplicateEpisodeAction").GetString(), Is.EqualTo("KeepLargest"));
            Assert.That(r.GetProperty("sampleMaxBytes").GetInt64(), Is.EqualTo(300L * 1024 * 1024));
            Assert.That(r.GetProperty("subtitleExtensions").EnumerateArray().Select(e => e.GetString()),
                Is.EqualTo(new[] { ".srt", ".sub", ".idx", ".ass", ".ssa" }));
            Assert.That(r.GetProperty("variationCatalogPath").GetString(), Is.EqualTo(""));
            Assert.That(r.GetProperty("titleYearOverrides").GetArrayLength(), Is.EqualTo(8));
            Assert.That(r.GetProperty("tvdbIdOverrides").EnumerateObject(), Is.Empty);
            Assert.That(r.GetProperty("tmdbIdOverrides").EnumerateObject(), Is.Empty);
        });
        Assert.That(JsonSerializer.Serialize(loaded, LegacyOptions), Is.EqualTo(JsonSerializer.Serialize(new MediaButlerSettings(), LegacyOptions)));
    }

    [Test]
    public void Settings_live_in_the_existing_MediaButler_app_folder()
    {
        Assert.That(new SettingsService().FilePath, Is.EqualTo(SettingsFile));
        Assert.That(SettingsManifestCommand.AppIdOf<MediaButlerSettings>(), Is.EqualTo("MediaButler"));
    }

    [Test]
    public void Write_settings_manifest_switch_seeds_and_exits_without_running_the_app()
    {
        var exe = Path.Combine(new DirectoryInfo(Path.GetDirectoryName(typeof(MediaButlerLampTests).Assembly.Location)!)
            .Parent!.Parent!.Parent!.Parent!.FullName, "MediaButler", "bin", "Debug", "net10.0-windows", "mediabutler.exe");
        Assert.That(File.Exists(exe), Is.True, "build the solution first");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add(SettingsManifestCommand.Switch);
        psi.Environment["MINDATTIC_VAULT_ROAMING_ROOT"] = Roaming;
        psi.Environment["MINDATTIC_VAULT_LOCAL_ROOT"] = Tmp.MakeDir("Local");

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        Assert.That(proc.WaitForExit(TimeSpan.FromSeconds(60)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(proc.ExitCode, Is.EqualTo(0));
            Assert.That(stdout.Trim(), Is.EqualTo(ManifestFile));
            Assert.That(File.Exists(SettingsFile), Is.True);
            Assert.That(SettingsManifestFile.TryRead(ManifestFile)?.AppId, Is.EqualTo("MediaButler"));
        });
    }

    // ---- Keys -------------------------------------------------------------------------------

    private NamedKeyStore TempKeys() =>
        new(Roaming, new KeyAssignmentStore(Path.Combine(Roaming, KeyAssignmentStore.Bucket)));

    [Test]
    public void Llm_fallback_fails_over_to_the_second_key_when_the_first_is_rejected()
    {
        var keys = TempKeys();
        keys.Add("claude", "first", "sk-first");
        keys.Add("claude", "second", "sk-second");
        var used = new List<string>();
        var transport = LegionFallbackParser.BuildTransport(new KeySelector(new FileKeySource(keys)),
            (_, key, _, _, _, _, _) =>
            {
                used.Add(key);
                if (key == "sk-first") throw new HttpRequestException("401 Unauthorized", null, HttpStatusCode.Unauthorized);
                return Task.FromResult("""{"kind":"movie","title":"Heat","year":1995}""");
            });
        var parser = new LegionFallbackParser(new MediaButlerSettings { EnableLlmFallback = true }, transport);

        var guess = parser.ClassifyAsync("zzqx", new[] { "a.mkv" }).GetAwaiter().GetResult();

        Assert.That(used, Is.EqualTo(new[] { "sk-first", "sk-second" }));
        Assert.That(guess!.Title, Is.EqualTo("Heat"));
    }

    [Test]
    public void Llm_fallback_uses_MediaButlers_assigned_key_first()
    {
        var keys = TempKeys();
        keys.Add("claude", "shared", "sk-shared");
        keys.Add("claude", "mine", "sk-mine");
        keys.Assignments.Set("MediaButler", "claude", new[] { "mine", "shared" });
        var used = new List<string>();
        var transport = LegionFallbackParser.BuildTransport(new KeySelector(new FileKeySource(keys)),
            (_, key, _, _, _, _, _) => { used.Add(key); return Task.FromResult("{}"); });

        new LegionFallbackParser(new MediaButlerSettings { EnableLlmFallback = true }, transport)
            .ClassifyAsync("zzqx", Array.Empty<string>()).GetAwaiter().GetResult();

        Assert.That(used, Is.EqualTo(new[] { "sk-mine" }));
    }

    [Test]
    public void Llm_fallback_with_no_key_skips_the_name_instead_of_failing_the_run()
    {
        var transport = LegionFallbackParser.BuildTransport(new KeySelector(new FileKeySource(TempKeys())),
            (_, _, _, _, _, _, _) => throw new AssertionException("no key, no call"));

        var guess = new LegionFallbackParser(new MediaButlerSettings { EnableLlmFallback = true }, transport)
            .ClassifyAsync("zzqx", Array.Empty<string>()).GetAwaiter().GetResult();

        Assert.That(guess, Is.Null);
    }

    [Test]
    public void Legion_errors_wrapping_a_401_still_count_as_a_failed_key()
    {
        var wrapped = new InvalidOperationException("breaker", new HttpRequestException("401", null, HttpStatusCode.Unauthorized));

        Assert.That(LegionFallbackParser.ClassifyLegionFailure(wrapped)?.Kind, Is.EqualTo(KeyFailureKind.Auth));
        Assert.That(LegionFallbackParser.ClassifyLegionFailure(new InvalidOperationException("other")), Is.Null);
    }

    [Test]
    public void Startup_import_moves_the_old_app_scoped_key_into_the_shared_list_as_MediaButlers_active_key()
    {
#pragma warning disable CS0618 // the pre-LAMP per-app copy is exactly what this test recreates
        new AppScopedCredentialStore("mediabutler", new LlmCredentialStore(Path.Combine(Roaming, LlmCredentialStore.Bucket)))
            .SetKey("claude", "sk-mediabutler-own");
#pragma warning restore CS0618
        var keys = TempKeys();
        keys.Add("claude", "shared", "sk-shared");

        KeyImporter.ImportAppScopedFor("MediaButler", TempKeys());
        var second = KeyImporter.ImportAppScopedFor("MediaButler", TempKeys());

        var active = new KeySelector(new FileKeySource(TempKeys())).GetActiveKey("claude", "MediaButler");
        Assert.That(active?.Value, Is.EqualTo("sk-mediabutler-own"), "MediaButler keeps using its own key");
        Assert.That(TempKeys().GetKeys("claude").Select(k => k.Value), Does.Contain("sk-shared"));
        Assert.That(second.Added, Is.Empty, "running the import again changes nothing");
    }

    [Test]
    public void No_per_app_key_store_is_left_in_MediaButler()
    {
        var src = Path.Combine(new DirectoryInfo(Path.GetDirectoryName(typeof(MediaButlerLampTests).Assembly.Location)!)
            .Parent!.Parent!.Parent!.Parent!.FullName, "MediaButler");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("AppScopedCredentialStore"))
            .Select(Path.GetFileName);

        Assert.That(offenders, Is.Empty);
    }

    // ---- Cog / old UI -----------------------------------------------------------------------

    private sealed class FakeLauncher : IVaultDesktopLauncher
    {
        public bool Running { get; init; } = true;
        public List<VaultOpenRequest> Requests { get; } = new();
        public bool TrySend(VaultOpenRequest request) { Requests.Add(request); return Running; }
        public bool TryStart(VaultOpenRequest request) => false;
    }

    private static T WithLauncher<T>(FakeLauncher fake, Func<T> body)
    {
        var original = VaultSettings.Launcher;
        VaultSettings.Launcher = fake;
        try { return body(); }
        finally { VaultSettings.Launcher = original; }
    }

    [Test]
    public void Settings_cog_opens_Vault_at_the_MediaButler_tab()
    {
        var fake = new FakeLauncher();

        var result = WithLauncher(fake, () => SettingsCog.Open());

        Assert.That(result.Opened, Is.True);
        Assert.That(fake.Requests.Single().AppId, Is.EqualTo("MediaButler"));
        Assert.That(fake.Requests.Single().Setting, Is.Null);
    }

    [Test]
    public void Settings_cog_can_deep_link_to_one_setting()
    {
        var fake = new FakeLauncher();

        WithLauncher(fake, () => SettingsCog.Open($"{Prefix}:fileBotPath"));

        Assert.That(fake.Requests.Single().Setting, Is.EqualTo($"{Prefix}:fileBotPath"));
    }

    [Test]
    public void Settings_cog_explains_when_Vault_is_not_installed()
    {
        var result = WithLauncher(new FakeLauncher { Running = false }, () => SettingsCog.Open());

        Assert.That(result.Opened, Is.False);
        Assert.That(result.Message, Does.Contain("isn't installed").And.Contain(SettingsFile));
    }

    [Test]
    public void Console_menu_Settings_row_opens_Vault()
    {
        var fake = new FakeLauncher();
        var runner = new PipelineRunner(new SettingsService());
        var items = MainMenuCommand.BuildItems(runner.LoadEffective(), new BaseSettings(), runner, () => { });
        var row = items.Single(i => i.Name == "Settings");

        WithLauncher(fake, () => { ((Action)row.Tag!)(); return 0; });

        Assert.That(fake.Requests.Single().AppId, Is.EqualTo("MediaButler"));
    }

    [Test]
    public void Old_settings_editor_is_gone()
    {
        var core = typeof(MediaButlerSettings).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(core.GetType("MediaButler.Settings.SettingsEditor"), Is.Null, "console settings editor still exists");
            Assert.That(typeof(SettingsService).GetMethod("Save"), Is.Null, "SettingsService still offers a general Save");
            Assert.That(typeof(SettingsService).GetMethod("Update"), Is.Null, "SettingsService still offers a general Update");
        });
        var ui = Path.Combine(new DirectoryInfo(Path.GetDirectoryName(typeof(MediaButlerLampTests).Assembly.Location)!)
            .Parent!.Parent!.Parent!.Parent!.FullName, "MediaButler.Wpf.UI", "Pages");
        Assert.That(File.Exists(Path.Combine(ui, "Settings.razor")), Is.False, "WPF Settings page still exists");
        Assert.That(File.Exists(Path.Combine(ui, "Settings.razor.cs")), Is.False);
    }
}
