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
                VaultPaths.Ensure(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Audit logging must never break a mutation. Failures are silent.
        }
    }
}
