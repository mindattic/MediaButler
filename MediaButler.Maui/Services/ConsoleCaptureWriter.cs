using System.Text;

namespace MediaButler.Maui.Services;

/// <summary>
/// TextWriter that captures every <see cref="Console.Write"/> / <see cref="Console.WriteLine"/>
/// call and forwards the produced text to a sink delegate. Used to redirect
/// the existing CLI pipeline's <c>Console</c> output into the MAUI output log
/// without modifying the MediaButler library.
///
/// <para>
/// Lines are flushed to the sink as soon as a newline is written. Partial
/// writes are buffered so a single line built up via multiple
/// <see cref="Console.Write"/> calls (e.g. <c>"  " + name + " -> " + dest</c>)
/// arrives at the UI as one row.
/// </para>
/// </summary>
public sealed class ConsoleCaptureWriter : TextWriter
{
    private readonly Action<string> sink;
    private readonly StringBuilder buffer = new();

    public ConsoleCaptureWriter(Action<string> sink) => this.sink = sink;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (buffer)
        {
            if (value == '\n')
            {
                var line = buffer.ToString();
                buffer.Clear();
                if (line.EndsWith('\r')) line = line[..^1];
                sink(line);
            }
            else
            {
                buffer.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        foreach (var ch in value) Write(ch);
    }

    /// <summary>Flush any buffered partial line so the user sees it before completion.</summary>
    public override void Flush()
    {
        lock (buffer)
        {
            if (buffer.Length == 0) return;
            var line = buffer.ToString();
            buffer.Clear();
            sink(line);
        }
    }
}
