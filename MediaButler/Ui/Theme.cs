using Spectre.Console;

namespace MediaButler.Ui;

/// <summary>
/// Color palette: one source of truth for accent/dim/key colors so menus and
/// pipeline status lines stay visually consistent. Mirrors
/// <c>MindAttic.Console.Ui.Theme</c> so the two consoles look identical.
/// </summary>
public static class Theme
{
    public static readonly Color Header = Color.Cyan1;
    public static readonly Color Active = Color.Yellow;
    public static readonly Color Accent = Color.DarkCyan;
    public static readonly Color Desc   = Color.Grey;
    public static readonly Color Key    = Color.Green;
    public static readonly Color Dim    = Color.Grey50;
    public static readonly Color Normal = Color.White;
    public static readonly Color Err    = Color.Red;
    public static readonly Color Ok     = Color.Green;

    public static Style HeaderStyle => new(foreground: Header);
    public static Style ActiveStyle => new(foreground: Active);
    public static Style AccentStyle => new(foreground: Accent);
    public static Style DescStyle   => new(foreground: Desc);
    public static Style KeyStyle    => new(foreground: Key);
    public static Style DimStyle    => new(foreground: Dim);
    public static Style ErrorStyle  => new(foreground: Err);
    public static Style OkStyle     => new(foreground: Ok);
    public static Style NormalStyle => new(foreground: Normal);
}
