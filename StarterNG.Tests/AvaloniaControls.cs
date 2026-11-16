namespace StarterNG.Tests;

/// <summary>
/// Tests that build Avalonia controls run one after another: a control belongs to the
/// thread that made the first one, and run side by side they now and then failed.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AvaloniaControls
{
    public const string Name = "Avalonia controls";
}
