using Spectre.Console;

namespace MediaButler.Ui;

/// <summary>
/// Arrow-key selection prompt. Up/Down (or K/J) to move, Home/End to jump,
/// Enter to select, Esc to go back. Disabled rows render dim and are skipped
/// during navigation. Mirrors <c>MindAttic.Console.Ui.Menu</c>.
/// </summary>
public static class Menu
{
    /// <summary>
    /// Selection prompt with real keyboard handling. Returns the selected item,
    /// or null if the user pressed Esc (or the "&lt; Back" sentinel).
    /// </summary>
    public static MenuItem? Prompt(string title, IReadOnlyList<MenuItem> items, bool allowBack = true)
    {
        var rows = new List<MenuItem>(items);
        if (allowBack)
        {
            rows.Add(new MenuItem
            {
                Name = "< Back",
                Description = "return to previous menu",
                Tag = MenuSentinel.Back,
            });
        }

        if (rows.Count == 0) return null;

        var nameWidth = rows.Max(i => i.Name.Length);
        var index = FirstSelectable(rows, 0, +1);
        if (index < 0) return null;
        var startTop = Console.CursorTop;

        var priorCursorVisible = true;
        try { priorCursorVisible = Console.CursorVisible; } catch { /* not supported / redirected */ }

        try
        {
            // Both the getter above and the setter can throw IOException
            // ("The handle is invalid") when stdout has been redirected — tolerate
            // it so the menu still renders rather than crashing the whole app.
            try { Console.CursorVisible = false; } catch (IOException) { /* redirected */ }
            Render(title, rows, nameWidth, index);

            while (true)
            {
                ConsoleKeyInfo keyInfo;
                try
                {
                    keyInfo = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    // stdin is redirected (piped run, CI, test harness) — no
                    // interactive user to drive the menu. Unwind cleanly
                    // instead of crashing.
                    return null;
                }
                var navigated = false;

                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K:
                        index = Move(rows, index, -1);
                        navigated = true;
                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J:
                        index = Move(rows, index, +1);
                        navigated = true;
                        break;
                    case ConsoleKey.Home:
                    {
                        var first = FirstSelectable(rows, 0, +1);
                        if (first >= 0) { index = first; navigated = true; }
                        break;
                    }
                    case ConsoleKey.End:
                    {
                        var last = FirstSelectable(rows, rows.Count - 1, -1);
                        if (last >= 0) { index = last; navigated = true; }
                        break;
                    }
                    case ConsoleKey.Enter:
                    {
                        var chosen = rows[index];
                        if (chosen.Disabled) break;
                        if (ReferenceEquals(chosen.Tag, MenuSentinel.Back)) return null;
                        return chosen;
                    }
                    case ConsoleKey.Escape:
                        if (!allowBack) break;
                        return null;
                }

                if (!navigated) continue;

                Console.SetCursorPosition(0, startTop);
                Console.Write("\x1b[J");
                Render(title, rows, nameWidth, index);
            }
        }
        finally
        {
            try { Console.CursorVisible = priorCursorVisible; } catch (IOException) { /* see above */ }
            catch (PlatformNotSupportedException) { /* non-Windows */ }
        }
    }

    private static int Move(IReadOnlyList<MenuItem> rows, int from, int delta)
    {
        var i = from;
        for (var n = 0; n < rows.Count; n++)
        {
            i = (i + delta + rows.Count) % rows.Count;
            if (!rows[i].Disabled) return i;
        }
        return from;
    }

    private static int FirstSelectable(IReadOnlyList<MenuItem> rows, int from, int delta)
    {
        var i = from;
        while (i >= 0 && i < rows.Count)
        {
            if (!rows[i].Disabled) return i;
            i += delta;
        }
        // No selectable row in this direction. Callers handle -1: the initial
        // call returns null (nothing to pick), and Home/End keep the current
        // index rather than parking the cursor on a disabled row.
        return -1;
    }

    private static void Render(string title, IReadOnlyList<MenuItem> rows, int nameWidth, int highlighted)
    {
        AnsiConsole.MarkupLine(title);
        AnsiConsole.WriteLine();
        for (var i = 0; i < rows.Count; i++)
            RenderItem(rows[i], nameWidth, i == highlighted);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [green]Up/Down[/][grey50] navigate  [/][green]Enter[/][grey50] select  [/][green]Esc[/][grey50] back[/]");
    }

    private static void RenderItem(MenuItem item, int nameWidth, bool highlighted)
    {
        // Name + Description come from user-controlled settings (paths, names,
        // commit messages). Escape before interpolating into markup so a stray
        // '[' doesn't crash the prompt with InvalidOperationException. Pad on
        // the raw string so column alignment matches the visible width.
        var name = Markup.Escape(item.Name.PadRight(nameWidth));
        var desc = string.IsNullOrWhiteSpace(item.Description)
            ? ""
            : $"  [grey50]{Markup.Escape(item.Description)}[/]";
        if (item.Disabled)
            AnsiConsole.MarkupLine($"  [grey50]{name}[/]{desc}");
        else if (highlighted)
            AnsiConsole.MarkupLine($"[yellow]> {name}[/]{desc}");
        else
            AnsiConsole.MarkupLine($"  {name}{desc}");
    }
}

/// <summary>Marker tags for menu sentinels (Back, etc.).</summary>
public static class MenuSentinel
{
    public static readonly object Back = new();
}
