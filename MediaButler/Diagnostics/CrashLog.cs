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
/// Rolled monthly under the same <c>%LOCALAPPDATA%\MindAttic\MediaButler</c> directory
/// <see cref="Pipeline.AuditLog"/> already uses, so both files live side by side.
/// </para>
/// </summary>
public static class CrashLog
{
    private const string AppFolder = "MediaButler";
    private static readonly MessageTemplateParser TemplateParser = new();
    private static readonly object Lock = new();
    private static MindAtticSqliteSink? sink;

    /// <summary>Test-only seam: redirects the rolled-file directory away from the real
    /// %LOCALAPPDATA%\MindAttic\MediaButler, and forces a fresh sink on next use. Set back to
    /// null when done.</summary>
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
        var directory = directoryOverride ?? VaultPaths.LocalApp(AppFolder);
        VaultPaths.Ensure(directory);
        LogFileRoller.PruneOldFiles(directory, retainedFileCount: 12);
        return new MindAtticSqliteSink(LogFileRoller.CurrentPath(directory, DateTime.UtcNow), application: "MediaButler");
    }
}
