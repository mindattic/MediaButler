using System.Text.Json;
using MediaButler.Media;
using MediaButler.Settings;
using MindAttic.Vault.Paths;

namespace MediaButler.Pipeline;

/// <summary>
/// Append-only NDJSON audit log of every mutation MediaButler performs.
/// Lives at <c>%LOCALAPPDATA%\MindAttic\MediaButler\audit-YYYY-MM-DD.ndjson</c>
/// and exists for one reason: when something goes sideways at 2 a.m. and
/// you can't remember whether MediaButler moved a folder or you did, this
/// file is the record. Each line is a single JSON object:
///
/// <code>
/// {"ts":"2026-05-22T03:14:15Z","op":"move","kind":"TvSeason","from":"...","to":"...","dryRun":false}
/// </code>
///
/// Failures to write the log are swallowed — the audit log is observation,
/// not the system of record, and we don't want logging IO to break a move.
/// </summary>
public static class AuditLog
{
    private const string AppFolder = "MediaButler";
    private static readonly object Lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    // Cached append writer so a large run doesn't open/close the day file once
    // per mutation. Re-created when the path changes (date rollover at midnight).
    // AutoFlush keeps the log durable for crash forensics — its whole purpose.
    private static StreamWriter? writer;
    private static string? writerPath;

    /// <summary>
    /// Process-lifetime count of audit-write failures. Read by
    /// <c>PipelineRunner.PrintReport</c> so a final warning surfaces when the
    /// "system of record" is silently failing (disk full, ACL revoked, locked
    /// file). Use <see cref="ResetFailureCount"/> in tests.
    /// </summary>
    public static int FailureCount => failureCount;
    private static int failureCount;

    /// <summary>The most recent failure's exception message, for the final warning.</summary>
    public static string? LastFailureMessage { get; private set; }

    /// <summary>Reset the per-process failure counter (intended for tests).</summary>
    public static void ResetFailureCount()
    {
        lock (Lock)
        {
            failureCount = 0;
            LastFailureMessage = null;
            writer?.Dispose();
            writer = null;
            writerPath = null;
        }
    }

    /// <summary>The current day's log path under <c>%LOCALAPPDATA%</c>.</summary>
    public static string FilePath() => Path.Combine(
        VaultPaths.LocalApp(AppFolder),
        $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson");

    /// <summary>
    /// Append one mutation record. <paramref name="op"/> is a short verb
    /// ("rename", "move", "delete-empty", "hoist", "copy-cross-volume").
    /// </summary>
    public static void Record(
        MediaButlerSettings settings,
        bool dryRun,
        string op,
        string from,
        string? to,
        MediaKind kind)
    {
        try
        {
            var entry = new
            {
                ts     = DateTime.UtcNow.ToString("o"),
                op,
                kind   = kind.ToString(),
                from,
                to,
                dryRun,
            };
            var line = JsonSerializer.Serialize(entry, JsonOpts);

            var path = FilePath();
            lock (Lock)
            {
                if (writer is null || !string.Equals(writerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    writer?.Dispose();
                    VaultPaths.Ensure(Path.GetDirectoryName(path)!);
                    writer = new StreamWriter(path, append: true) { AutoFlush = true };
                    writerPath = path;
                }
                writer.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            // Audit logging must never break a mutation, so we still swallow
            // the exception. But we track it so the pipeline summary can warn
            // the user — silent log loss defeats the whole point of the log.
            lock (Lock)
            {
                failureCount++;
                LastFailureMessage = ex.Message;
            }
        }
    }
}
