using Microsoft.Data.Sqlite;
using MindAttic.Log.Migration;
using MindAttic.Log.Sinks;
using MindAttic.Vault.Paths;
using Serilog.Events;
using Serilog.Parsing;

namespace MediaButler.Diagnostics;

/// <summary>
/// Durable, queryable record of unhandled exceptions from every MediaButler front door (CLI,
/// WPF). Distinct from <see cref="Pipeline.AuditLog"/>, which is a business record of file
/// mutations (move/rename/etc.) — this is operational diagnostics, the shared MindAttic.Log
/// schema (see that repo's docs/MIGRATION.md), not a domain-specific audit trail. Before this,
/// an exception that reached Program.cs's outer catch printed to the console and vanished —
/// nothing survived past the window closing, which is exactly the gap this closes for
/// cron-driven/unattended runs.
/// <para>
/// Phase 2 (MindAttic.Log 3.0.0, see its docs/BIBLE.md §1.1): lives in its own standalone
/// <c>MindAttic.Log.db</c> under Vault's shared roaming root (<c>VaultPaths.LoggingDbPath</c>), not
/// the rolled files Phase 1 kept under <c>%LOCALAPPDATA%\MindAttic\MediaButler</c> alongside
/// <see cref="Pipeline.AuditLog"/> — superseded so clearing that local folder can no longer take the
/// crash history with it.
/// </para>
/// </summary>
public static class CrashLog
{
    private const string AppFolder = "MediaButler";
    private static readonly MessageTemplateParser TemplateParser = new();
    private static readonly object Lock = new();
    private static MindAtticSqliteSink? sink;

    /// <summary>Test-only seam: redirects where Phase 1's rolled files are read from for the
    /// one-time migration, and forces a fresh sink on next use. The destination itself redirects
    /// via Vault's own <c>MINDATTIC_VAULT_ROAMING_ROOT</c> env var (M.VALT-LAW-6), not this property.
    /// Set back to null when done.</summary>
    internal static string? DirectoryOverrideForTests
    {
        get => directoryOverride;
        set
        {
            lock (Lock)
            {
                directoryOverride = value;
                sink?.Dispose();
                sink = null;
            }
        }
    }
    private static string? directoryOverride;

    /// <summary>Records a fatal, unhandled exception. Never throws — a crash-logging failure
    /// must not mask or replace the original crash report.</summary>
    public static void Fatal(string context, Exception exception)
    {
        try
        {
            lock (Lock)
            {
                sink ??= CreateSink();
                var template = TemplateParser.Parse(context);
                var logEvent = new LogEvent(
                    DateTimeOffset.UtcNow, LogEventLevel.Fatal, exception, template, []);
                sink.Emit(logEvent);
            }
        }
        catch
        {
            // Same rule AuditLog follows: logging must never take down the thing reporting the
            // original failure.
        }
    }

    private static MindAtticSqliteSink CreateSink()
    {
        var dbPath = VaultPaths.LoggingDbPath(AppFolder);

        // Phase 2: carry forward any rows left in Phase 1's rolled MindAttic.Log.<yyyy-MM>.db
        // files (LogFileRoller is retired, so these are never written to again), then remove the
        // now-empty rolled files. Idempotent and a clean no-op on every later startup.
        var oldDirectory = directoryOverride ?? VaultPaths.LocalApp(AppFolder);
        if (Directory.Exists(oldDirectory))
        {
            foreach (var rolledFile in Directory.GetFiles(oldDirectory, "MindAttic.Log.*.db"))
            {
                MindAtticLogMigrator.MigrateFromSqliteAsync(rolledFile, dbPath, dropSource: true)
                    .GetAwaiter().GetResult();
                SqliteConnection.ClearAllPools();
                try { File.Delete(rolledFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        return new MindAtticSqliteSink(dbPath, application: "MediaButler");
    }
}
