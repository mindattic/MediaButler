using Spectre.Console;

namespace MediaButler.Ui;

/// <summary>
/// Header / footer / prompt helpers shared by the main menu and the settings
/// editor. Matches <c>MindAttic.Console.Ui.Screen</c> so the two consoles
/// look identical.
/// </summary>
public static class Screen
{
    public static void Header(params string[] breadcrumbs)
    {
        try { AnsiConsole.Clear(); } catch (IOException) { /* stdout redirected */ }
        var trail = string.Join(" > ", new[] { "MediaButler" }.Concat(breadcrumbs));
        AnsiConsole.Write(new Rule($"[cyan1]{Markup.Escape(trail)}[/]")
        {
            Style = Theme.AccentStyle,
            Justification = Justify.Left,
        });
        AnsiConsole.WriteLine();
    }

    public static void Footer(string extra = "")
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule { Style = Theme.AccentStyle });
        var hints = "  [green]Up/Down[/][grey50] navigate  [/][green]Enter[/][grey50] select  [/][green]Esc[/][grey50] back[/]";
        if (!string.IsNullOrWhiteSpace(extra))
            hints = $"  {extra}  [grey50]·[/]  {hints.TrimStart()}";
        AnsiConsole.MarkupLine(hints);
        AnsiConsole.WriteLine();
    }

    public static void Notice(string markup)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  {markup}");
    }

    public static void PressAnyKey()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey50]Press any key to continue...[/]");
        try { Console.ReadKey(intercept: true); }
        catch (InvalidOperationException) { /* stdin redirected */ }
    }

    /// <summary>
    /// Free-form prompt for a string. Returns the trimmed input, or
    /// <paramref name="currentValue"/> if the user pressed Enter on an empty
    /// line. Useful for "edit this setting" flows where blank means "keep".
    /// </summary>
    public static string? Prompt(string label, string? currentValue = null)
    {
        AnsiConsole.WriteLine();
        var line = $"  [white]{Markup.Escape(label)}[/]";
        if (!string.IsNullOrWhiteSpace(currentValue))
            line += $"  [grey50]({Markup.Escape(currentValue)})[/]";
        AnsiConsole.MarkupLine(line);
        AnsiConsole.Markup("  [grey50]>[/] ");
        var input = Console.ReadLine();
        if (input is null) return null;
        input = input.Trim();
        return string.IsNullOrEmpty(input) ? currentValue : input;
    }
}
