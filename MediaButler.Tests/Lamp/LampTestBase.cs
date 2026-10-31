using System.Text.Json;
using System.Text.Json.Nodes;
using MediaButler.Settings;
using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;

namespace MediaButler.Tests.Lamp;

/// <summary>
/// Shared setup for the LAMP migration tests (MB-T27): each test gets its own roaming root (never the
/// real %APPDATA%, M.VALT-LAW-6) and a sandbox media library (source, TV, Movies, a catalog file), so a
/// legacy settings.json can be written exactly where the pre-LAMP MediaButler kept it.
/// </summary>
public abstract class LampTestBase
{
    /// <summary>The options the pre-LAMP <c>JsonSettingsStore</c> (Vault 6) wrote settings.json with.</summary>
    protected static readonly JsonSerializerOptions LegacyOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected const string Prefix = "MindAttic:Settings:MediaButler";

    private string? previousRoaming;

    private protected TempDir Tmp { get; private set; } = null!;
    protected string Roaming { get; private set; } = null!;
    protected string AppFolder => Path.Combine(Roaming, "MediaButler");
    protected string SettingsFile => Path.Combine(AppFolder, "settings.json");
    protected string ManifestFile => Path.Combine(AppFolder, SettingsManifest.FileName);
    protected string Source { get; private set; } = null!;
    protected string Tv { get; private set; } = null!;
    protected string Movies { get; private set; } = null!;
    protected string Catalog { get; private set; } = null!;

    [SetUp]
    public void LampSetUp()
    {
        Tmp = new TempDir();
        Roaming = Tmp.MakeDir("Roaming");
        Source = Tmp.MakeDir("Torrents");
        Tv = Tmp.MakeDir("TV");
        Movies = Tmp.MakeDir("Movies");
        Catalog = Path.Combine(Tmp.Path, "variations.json");
        previousRoaming = Environment.GetEnvironmentVariable(VaultPaths.RoamingRootEnvVar);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Roaming);
    }

    [TearDown]
    public void LampTearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, previousRoaming);
        Tmp.Dispose();
    }

    /// <summary>
    /// Today's settings.json as pre-LAMP MediaButler wrote it, pointed at the sandbox library, with
    /// <paramref name="jsonName"/> set to <paramref name="legacyValue"/>.
    /// </summary>
    protected void WriteLegacySettings(string jsonName, object? legacyValue)
    {
        var legacy = JsonSerializer.SerializeToNode(new MediaButlerSettings
        {
            SourcePath = Source,
            TvDestination = Tv,
            MoviesDestination = Movies,
            VariationCatalogPath = Catalog,
            FileBotPath = Path.Combine(Tmp.Path, "no-filebot", "filebot.exe"),
        }, LegacyOptions)!.AsObject();
        Assert.That(legacy.ContainsKey(jsonName), Is.True, $"'{jsonName}' is not a key MediaButler has ever written");
        legacy[jsonName] = JsonSerializer.SerializeToNode(legacyValue, LegacyOptions);
        Directory.CreateDirectory(AppFolder);
        File.WriteAllText(SettingsFile, legacy.ToJsonString(LegacyOptions));
    }

    /// <summary>
    /// The end-to-end import check every per-setting test starts with:
    /// <list type="number">
    ///   <item>today's settings.json (with <paramref name="legacyValue"/> under <paramref name="jsonName"/>)
    ///         loads through the app's own <see cref="SettingsService"/> (<c>LoadOrSeed</c>) into the right property, unchanged on disk;</item>
    ///   <item>the manifest names it <c>MindAttic:Settings:MediaButler:{jsonName}</c>, labeled, grouped and helped;</item>
    ///   <item>Vault's merged IConfiguration tree exposes the same value under that name;</item>
    ///   <item>it round-trips through <see cref="JsonSettingsStore{T}"/> Save/Load.</item>
    /// </list>
    /// Returns the loaded settings for the test's consumer check.
    /// </summary>
    protected MediaButlerSettings ImportAndRoundTrip(string jsonName, object? legacyValue)
    {
        WriteLegacySettings(jsonName, legacyValue);
        var before = File.ReadAllText(SettingsFile);
        var expected = JsonSerializer.SerializeToElement(legacyValue, LegacyOptions);

        // 1. Imports into the right property.
        var loaded = new SettingsService().Load();
        var prop = typeof(MediaButlerSettings).GetProperties()
            .Single(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name) == jsonName);
        var actual = JsonSerializer.SerializeToElement(prop.GetValue(loaded), prop.PropertyType, LegacyOptions);
        Assert.That(actual.GetRawText(), Is.EqualTo(expected.GetRawText()),
            $"{prop.Name} did not import today's value from '{jsonName}'");
        Assert.That(File.ReadAllText(SettingsFile), Is.EqualTo(before), "loading must not rewrite the user's settings.json");

        // 2. Named, labeled, grouped and helped in the manifest written beside it.
        var manifest = SettingsManifestFile.TryRead(ManifestFile);
        Assert.That(manifest, Is.Not.Null, "LoadOrSeed should have written settings.manifest.json");
        var field = manifest!.Fields.SingleOrDefault(f => f.Name == $"{Prefix}:{jsonName}");
        Assert.That(field, Is.Not.Null, $"manifest has no field {Prefix}:{jsonName}");
        Assert.Multiple(() =>
        {
            Assert.That(field!.Property, Is.EqualTo(jsonName));
            Assert.That(field.Label, Is.Not.Empty);
            Assert.That(field.Group, Is.Not.EqualTo(SettingsManifest.DefaultGroup), "every setting sits in a named group");
            Assert.That(field.Help, Is.Not.Null.And.Not.Empty);
        });

        // 3. Vault's IConfiguration tree has the value under the manifest name.
        var config = new ConfigurationBuilder()
            .AddMindAtticVaultConfiguration(o => { o.RoamingRoot = Roaming; o.IncludeKeys = false; })
            .Build();
        var expectedEntries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Flatten(expectedEntries, $"{Prefix}:{jsonName}", expected);
        var actualEntries = config.GetSection($"{Prefix}:{jsonName}").AsEnumerable()
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (expected.ValueKind is not (JsonValueKind.Null or JsonValueKind.Array or JsonValueKind.Object))
            actualEntries[$"{Prefix}:{jsonName}"] = config[$"{Prefix}:{jsonName}"];
        Assert.That(actualEntries.Where(kv => kv.Value is not null), Is.EquivalentTo(expectedEntries.Where(kv => kv.Value is not null)),
            $"Vault configuration disagrees for {Prefix}:{jsonName}");

        // 4. Round-trips through the store.
        var copy = new JsonSettingsStore<MediaButlerSettings>(Tmp.MakeDir("roundtrip-" + jsonName));
        copy.Save(loaded);
        var reloaded = copy.Load();
        Assert.That(JsonSerializer.SerializeToElement(prop.GetValue(reloaded), prop.PropertyType, LegacyOptions).GetRawText(),
            Is.EqualTo(expected.GetRawText()), $"{prop.Name} did not survive Save/Load");
        using (var doc = JsonDocument.Parse(File.ReadAllText(copy.FilePath)))
            Assert.That(doc.RootElement.TryGetProperty(jsonName, out _), Is.True, $"saved file has no '{jsonName}' key");

        return loaded;
    }

    private static void Flatten(IDictionary<string, string?> sink, string path, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Flatten(sink, $"{path}:{p.Name}", p.Value);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in e.EnumerateArray()) Flatten(sink, $"{path}:{i++}", item);
                break;
            case JsonValueKind.String: sink[path] = e.GetString(); break;
            case JsonValueKind.True: sink[path] = "true"; break;
            case JsonValueKind.False: sink[path] = "false"; break;
            case JsonValueKind.Null: sink[path] = null; break;
            default: sink[path] = e.GetRawText(); break;
        }
    }

    protected static string MakeFile(string folder, string name, int bytes = 16)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>
    /// A stand-in filebot.exe: a .cmd that logs its arguments and the trust-all JVM options it was
    /// started with, so a test can see exactly which FileBot calls a stage made.
    /// </summary>
    protected sealed class FakeFileBot
    {
        public FakeFileBot(string folder)
        {
            Directory.CreateDirectory(folder);
            ExePath = Path.Combine(folder, "filebot.cmd");
            LogPath = Path.Combine(folder, "calls.log");
            File.WriteAllText(ExePath,
                "@echo off\r\n" +
                ">>\"%~dp0calls.log\" echo ARGS %*\r\n" +
                ">>\"%~dp0calls.log\" echo OPTS [%_JAVA_OPTIONS%]\r\n" +
                "exit /b 0\r\n");
        }

        public string ExePath { get; }
        public string LogPath { get; }
        public string Log => File.Exists(LogPath) ? File.ReadAllText(LogPath) : "";
    }
}
