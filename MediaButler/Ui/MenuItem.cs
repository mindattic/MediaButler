namespace MediaButler.Ui;

/// <summary>One row in a <see cref="Menu"/>.</summary>
public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public object? Tag { get; init; }

    /// <summary>Disabled rows render dim and are skipped on Enter.</summary>
    public bool Disabled { get; init; }
}
