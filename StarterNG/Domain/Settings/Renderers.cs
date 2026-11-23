namespace StarterNG.Domain.Settings;

/// <summary>The renderers a gfxrenderer word picks, as application.cpp tells them apart.</summary>
public enum RendererKind { Modern, Legacy }

public static class Renderers
{
    /// <summary>"legacy", "simple", "simpleshader" and "off" draw with the legacy renderer, any other word with the full one.</summary>
    public static RendererKind KindOf(string renderer) => renderer.ToLowerInvariant() switch
    {
        "legacy" or "simple" or "simpleshader" or "off" => RendererKind.Legacy,
        _ => RendererKind.Modern
    };
}
