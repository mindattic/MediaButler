using Spectre.Console;

namespace MediaButler.Ui;

/// <summary>
/// Verbosity floor for pipeline status output. Quiet runs suppress Normal/Dim
/// lines and only print Err and Active (warnings); the final
/// <see cref="Status.Summary"/> always prints. Verbose has no suppression.
/// </summary>
public enum Verbosity { Quiet, Normal, Verbose }

/// <summary>
/// Pipeline-stage status output. Replaces the previous
/// <c>ConsoleMenu.Status / WriteColor / Summary</c> trio with a small surface
/// that writes raw ANSI escapes via <see cref="Console.Write(string?)"/> —
/// not <see cref="AnsiConsole"/>'s widget renderer, which word-wraps to the
/// terminal width and would split absolute file paths across lines.
/// </summary>
public static class Status
{
    // Backed by AsyncLocal so test fixtures and any future concurrent runs
    // (Maui shell wrapping CLI logic) don't clobber each other's verbosity.
    // AsyncLocal<T> for a value type defaults to default(T) on the first read,
    // which would mean Quiet (0) — wrap with nullable so unset reads as Normal.
    private static readonly AsyncLocal<Verbosity?> verbosity = new();

    public static Verbosity Verbosity
    {
        get => verbosity.Value ?? Verbosity.Normal;
        set => verbosity.Value = value;
    }

    /// <summary>Indented one-liner. Suppressed in Quiet mode for non-Err/Active colors.</summary>
    public static void Print(string text, Color color)
    {
        if (Suppressed(color)) return;
        WriteRaw("  " + text, color, newline: true);
    }

    /// <summary>
    /// Per-item leading header ("  {name}") with no newline, so the stage can
    /// stack inline result fragments on the same row. Suppressed in Quiet mode —
    /// without this the stages emit orphan folder-name lines whose (Dim/Ok)
    /// result text is itself suppressed, leaving bare names with no outcome.
    /// </summary>
    public static void Item(string text)
    {
        if (Verbosity == Verbosity.Quiet) return;
        Console.Write("  " + text);
    }

    /// <summary>Trailing fragment on the current line, with a newline appended.</summary>
    public static void Line(string text, Color color)
    {
        if (Suppressed(color)) return;
        WriteRaw(text, color, newline: true);
    }

    /// <summary>
    /// Trailing fragment on the current line, no newline. Used when the caller
    /// emits multiple colored fragments before a final newline (e.g. FileBot
    /// stage stacks <c>[rename ok][artwork ok]</c> on one row).
    /// </summary>
    public static void Inline(string text, Color color)
    {
        if (Suppressed(color)) return;
        WriteRaw(text, color, newline: false);
    }

    /// <summary>
    /// Emit a bare newline that respects verbosity. Pipeline stages that print
    /// a leading item header with <see cref="Console.Write"/> and then stack
    /// inline fragments use this to terminate the line — in Quiet mode the
    /// fragments were suppressed and the newline must be too, otherwise the
    /// output is one blank line per item.
    /// </summary>
    public static void NewLine()
    {
        if (Verbosity == Verbosity.Quiet) return;
        Console.WriteLine();
    }

    /// <summary>
    /// Final-report line that prints at every verbosity, including Quiet. Use
    /// for end-of-pipeline totals so cron-driven runs still land the bottom
    /// line + anything that went wrong.
    /// </summary>
    public static void Summary(string text, Color color)
    {
        WriteRaw("  " + text, color, newline: true);
    }

    private static bool Suppressed(Color color) =>
        Verbosity == Verbosity.Quiet && color != Theme.Err && color != Theme.Active;

    /// <summary>
    /// Write colored text using raw 256-color ANSI escapes. Bypasses Spectre's
    /// widget rendering — which would word-wrap to the console width and
    /// chop long file paths across two lines. When color isn't supported
    /// (output redirected, NO_COLOR set), falls back to plain text.
    /// </summary>
    private static void WriteRaw(string text, Color color, bool newline)
    {
        var system = AnsiConsole.Console.Profile.Capabilities.ColorSystem;
        // NO_COLOR (https://no-color.org): any non-empty value disables color.
        // Spectre's profile usually honors it, but this path emits raw ANSI
        // directly, so check explicitly rather than assume the profile did.
        var noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        // Only the TrueColor profile can render the 24-bit escapes we emit below.
        // On a Legacy/Standard/EightBit console those sequences print as literal
        // garbage (e.g. "[38;2;...m") since the terminal can't interpret them —
        // fall back to plain text rather than corrupt the output.
        if (noColor || system != ColorSystem.TrueColor || Console.IsOutputRedirected)
        {
            if (newline) Console.WriteLine(text); else Console.Write(text);
            return;
        }
        // TrueColor (24-bit) ANSI — supported by every modern terminal
        // (Windows Terminal, iTerm2, gnome-terminal). Spectre's Color struct
        // exposes R/G/B but not a palette index in 0.49, so go RGB direct.
        var open = $"\x1b[38;2;{color.R};{color.G};{color.B}m";
        const string close = "\x1b[0m";
        if (newline) Console.WriteLine(open + text + close);
        else Console.Write(open + text + close);
    }
}
