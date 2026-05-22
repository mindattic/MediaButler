namespace MediaButler.Menu;

/// <summary>
/// One row in a <see cref="ConsoleMenu"/>. <see cref="OnSelect"/> returns true
/// to stay in the menu loop after the action runs, false to exit back up one level.
/// </summary>
public sealed class MenuItem
{
    public required string Label { get; init; }
    public string? Description { get; init; }

    /// <summary>Action invoked on Enter. Default = stay in menu.</summary>
    public Func<bool>? OnSelect { get; init; }

    /// <summary>Disabled rows render in dim and can't be selected.</summary>
    public bool Disabled { get; init; }
}
