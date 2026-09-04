namespace StarterNG.Application.Abstractions;

/// <summary>
/// The thumbnails in textures/mini, looked up by name. Backed by one directory
/// scan; a name with no bitmap falls back to the generic "other" thumbnail.
/// </summary>
public interface IMiniTextureIndex
{
    /// <summary>Builds the index up front, off the UI thread.</summary>
    void Preload();

    bool Has(string? miniName);

    /// <summary>Bitmap for the name, the fallback thumbnail, or null when neither exists.</summary>
    string? PathFor(string? miniName);

    /// <summary>The generic thumbnail shown for unknown stock.</summary>
    string? FallbackPath { get; }
}
