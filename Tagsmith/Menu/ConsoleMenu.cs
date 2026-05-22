namespace Tagsmith.Menu;

/// <summary>
/// Arrow-key console menu. Modeled after the MindAttic.Console PowerShell
/// menus but written for <c>Console.ReadKey</c>. Up/Down navigates, Enter
/// selects, Esc returns control to the caller.
/// </summary>
public static class ConsoleMenu
{
    public static readonly ConsoleColor Header = ConsoleColor.Cyan;
    public static readonly ConsoleColor Active = ConsoleColor.Yellow;
    public static readonly ConsoleColor Accent = ConsoleColor.DarkCyan;
    public static readonly ConsoleColor Desc   = ConsoleColor.Gray;
    public static readonly ConsoleColor Key    = ConsoleColor.DarkGreen;
    public static readonly ConsoleColor Err    = ConsoleColor.Red;
    public static readonly ConsoleColor Dim    = ConsoleColor.DarkGray;
    public static readonly ConsoleColor Normal = ConsoleColor.White;
    public static readonly ConsoleColor Ok     = ConsoleColor.Green;

    private const string HR = "------------------------------------------------------------";

    /// <summary>Top-of-screen breadcrumb. Pass the chain leading to the current view.</summary>
    public static void WriteHeader(params string[] breadcrumbs)
    {
        Console.Clear();
        Console.WriteLine();
        var trail = string.Join(" > ", new[] { "Tagsmith" }.Concat(breadcrumbs));
        WriteColor("  " + trail, Header, newline: true);
        WriteColor("  " + HR, Accent, newline: true);
        Console.WriteLine();
    }

    /// <summary>Bottom-of-screen key legend.</summary>
    public static void WriteFooter()
    {
        Console.WriteLine();
        WriteColor("  " + HR, Accent, newline: true);
        Console.Write("  ");
        WriteColor("Up/Down", Key);   WriteColor(" navigate  ", Dim);
        WriteColor("Enter",   Key);   WriteColor(" select  ",   Dim);
        WriteColor("Esc",     Key);   WriteColor(" back",       Dim, newline: true);
        Console.WriteLine();
    }

    /// <summary>Press-any-key prompt. Used after non-menu actions complete.</summary>
    public static void WaitForKey()
    {
        Console.WriteLine();
        WriteColor("  Press any key to continue...", Dim, newline: true);
        Console.ReadKey(intercept: true);
    }

    /// <summary>
    /// Free-form prompt for a string. Returns null if the user pressed Esc, the
    /// trimmed input otherwise (which may be empty). Renders the current value
    /// dimmed beside the prompt for context.
    /// </summary>
    public static string? Prompt(string label, string? currentValue = null)
    {
        Console.WriteLine();
        Console.Write("  ");
        WriteColor(label, Normal);
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            Console.Write("  ");
            WriteColor("(" + currentValue + ")", Dim);
        }
        Console.WriteLine();
        Console.Write("  > ");
        // Console.ReadLine can't be aborted with Esc, but it's good enough for
        // this menu; users press Enter on an empty line to keep the current value.
        var line = Console.ReadLine();
        if (line is null) return null;
        line = line.Trim();
        return string.IsNullOrEmpty(line) ? currentValue : line;
    }

    /// <summary>Show a menu and dispatch <see cref="MenuItem.OnSelect"/> on Enter. Returns when the user hits Esc.</summary>
    public static void Show(string[] breadcrumbs, Func<IReadOnlyList<MenuItem>> getItems)
    {
        var idx = 0;
        while (true)
        {
            var items = getItems();
            if (items.Count == 0) return;
            if (idx >= items.Count) idx = items.Count - 1;
            if (idx < 0) idx = 0;

            WriteHeader(breadcrumbs);
            Render(items, idx);
            WriteFooter();

            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    idx = NextSelectable(items, idx, -1);
                    break;
                case ConsoleKey.DownArrow:
                    idx = NextSelectable(items, idx, +1);
                    break;
                case ConsoleKey.Escape:
                    return;
                case ConsoleKey.Enter:
                    var item = items[idx];
                    if (item.Disabled || item.OnSelect is null) break;
                    var keepGoing = item.OnSelect();
                    if (!keepGoing) return;
                    break;
            }
        }
    }

    private static int NextSelectable(IReadOnlyList<MenuItem> items, int from, int delta)
    {
        var i = from + delta;
        while (i >= 0 && i < items.Count && items[i].Disabled) i += delta;
        if (i < 0 || i >= items.Count) return from;
        return i;
    }

    private static void Render(IReadOnlyList<MenuItem> items, int selected)
    {
        var nameCol = items.Max(it => it.Label.Length) + 3;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var isSelected = i == selected;
            var nameColor = item.Disabled ? Dim : (isSelected ? Active : Normal);
            var descColor = item.Disabled ? Dim : (isSelected ? Desc   : Dim);

            WriteColor(isSelected ? "  > " : "    ", Accent);
            WriteColor(item.Label.PadRight(nameCol), nameColor);
            if (!string.IsNullOrEmpty(item.Description))
                WriteColor(item.Description, descColor, newline: true);
            else
                Console.WriteLine();
        }
    }

    public static void WriteColor(string text, ConsoleColor color, bool newline = false)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(text); else Console.Write(text);
        Console.ForegroundColor = prev;
    }

    /// <summary>Convenience for indented status lines during pipeline runs.</summary>
    public static void Status(string text, ConsoleColor color)
    {
        WriteColor("  " + text, color, newline: true);
    }
}
