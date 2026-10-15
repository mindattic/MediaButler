using MediaButler.Diagnostics;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Schema;
using MindAttic.Vault.Paths;
using NUnit.Framework;

namespace MediaButler.Tests.Diagnostics;

/// <summary>
/// Proves CrashLog.Fatal (wired into both front doors' unhandled-exception paths — Program.cs's
/// outer catch and App.xaml.cs's DispatcherUnhandledException/AppDomain handlers) actually
/// reaches a readable row in the standalone MindAttic.Log.db, not just that it compiles.
/// Redirects Vault's roaming root via its env-var override (VLT-LAW-6) so this never touches the
/// real %APPDATA%.
/// </summary>
public class CrashLogTests
{
    private string directory = null!;
    private string roamingRoot = null!;
    private string? originalRoamingRoot;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "mediabutler-crashlog-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        CrashLog.DirectoryOverrideForTests = directory;

        roamingRoot = Path.Combine(Path.GetTempPath(), "mediabutler-crashlog-vault-tests-" + Guid.NewGuid());
        originalRoamingRoot = Environment.GetEnvironmentVariable(VaultPaths.RoamingRootEnvVar);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, roamingRoot);
    }

    [TearDown]
    public void TearDown()
    {
        CrashLog.DirectoryOverrideForTests = null; // disposes the test sink
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, originalRoamingRoot);
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        try { Directory.Delete(roamingRoot, recursive: true); } catch (IOException) { }
    }

    [Test]
    public void Fatal_Writes_A_Readable_Row_With_The_Exception()
    {
        CrashLog.Fatal("scan failed", new InvalidOperationException("no FileBot on PATH"));

        // Flush happens on a 2s timer inside MindAtticSqliteSink (or on Dispose); force it via
        // the same path TearDown uses so this test doesn't need a real sleep.
        CrashLog.DirectoryOverrideForTests = null;

        var dbPath = VaultPaths.LoggingDbPath("MediaButler");
        Assert.That(File.Exists(dbPath), Is.True, "Expected the standalone MindAttic.Log.db to be created.");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Application, Level, Message, Exception FROM {LogSchema.TableName};";
        using var reader = command.ExecuteReader();

        Assert.That(reader.Read(), Is.True, "Expected the fatal entry to have reached the table.");
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("MediaButler"));
            Assert.That(reader.GetInt32(1), Is.EqualTo((int)MindAttic.Log.Models.LogSeverity.Fatal));
            Assert.That(reader.GetString(2), Does.Contain("scan failed"));
            Assert.That(reader.GetString(3), Does.Contain("no FileBot on PATH"));
        });
    }
}
