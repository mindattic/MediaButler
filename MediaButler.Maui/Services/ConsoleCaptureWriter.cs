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
        string? completed = null;
        lock (buffer)
        {
            if (value == '\n')
            {
                completed = buffer.ToString();
                buffer.Clear();
                if (completed.EndsWith('\r')) completed = completed[..^1];
            }
            else
            {
                buffer.Append(value);
            }
        }
        // Fire the sink outside the lock so a slow/blocking sink can't stall
        // other producers waiting on the buffer.
        if (completed is not null) sink(completed);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        // Process the whole string under a single lock, collecting any completed
        // lines, then dispatch to the sink outside the lock.
        List<string>? completed = null;
        lock (buffer)
        {
            foreach (var ch in value)
            {
                if (ch == '\n')
                {
                    var line = buffer.ToString();
                    buffer.Clear();
                    if (line.EndsWith('\r')) line = line[..^1];
                    (completed ??= new List<string>()).Add(line);
                }
                else
                {
                    buffer.Append(ch);
                }
            }
        }
        if (completed is not null)
            foreach (var line in completed) sink(line);
    }

    /// <summary>Flush any buffered partial line so the user sees it before completion.</summary>
    public override void Flush()
    {
        string? line = null;
        lock (buffer)
        {
            if (buffer.Length > 0)
            {
                line = buffer.ToString();
                buffer.Clear();
            }
        }
        if (line is not null) sink(line);
    }
}
