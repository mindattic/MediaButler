using MediaButler.Diagnostics;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Schema;
using NUnit.Framework;

namespace MediaButler.Tests.Diagnostics;

/// <summary>
/// Proves CrashLog.Fatal (wired into both front doors' unhandled-exception paths — Program.cs's
/// outer catch and App.xaml.cs's DispatcherUnhandledException/AppDomain handlers) actually
/// reaches a readable MindAttic.Log row, not just that it compiles.
/// </summary>
public class CrashLogTests
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "mediabutler-crashlog-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        CrashLog.DirectoryOverrideForTests = directory;
    }

    [TearDown]
    public void TearDown()
    {
        CrashLog.DirectoryOverrideForTests = null; // disposes the test sink
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    [Test]
    public void Fatal_Writes_A_Readable_Row_With_The_Exception()
    {
        CrashLog.Fatal("scan failed", new InvalidOperationException("no FileBot on PATH"));

        // Flush happens on a 2s timer inside MindAtticSqliteSink (or on Dispose); force it via
        // the same path TearDown uses so this test doesn't need a real sleep.
        CrashLog.DirectoryOverrideForTests = null;

        var dbPath = Path.Combine(directory, $"MindAttic.Log.{DateTime.UtcNow:yyyy-MM}.db");
        Assert.That(File.Exists(dbPath), Is.True, "Expected a rolled MindAttic.Log file to be created.");

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
