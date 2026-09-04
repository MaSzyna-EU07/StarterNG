namespace StarterNG.Application.Abstractions;

/// <summary>
/// The layout of a MaSzyna installation. Replaces the
/// <c>Directory.GetCurrentDirectory()</c> calls and hard-coded "scenery/",
/// "dynamic", "textures/mini/" literals that were previously spread across
/// parsers, views and the loader.
/// </summary>
public interface IGamePaths
{
    /// <summary>Installation root: the folder holding eu07.exe.</summary>
    string Root { get; }

    /// <summary>Scenery definitions (*.scn).</summary>
    string Scenery { get; }

    /// <summary>Rolling stock: dynamic/&lt;maker&gt;/&lt;vehicle&gt;/textures.txt.</summary>
    string Dynamic { get; }

    /// <summary>Vehicle thumbnails used by the depot and the consist strip.</summary>
    string MiniTextures { get; }

    /// <summary>Simulator data files (load_weights.txt and friends).</summary>
    string Data { get; }

    /// <summary>Starter-owned configuration: presets, profiles, key bindings.</summary>
    string StarterConfig { get; }

    /// <summary>The starter's own error log (starter/bledy.txt).</summary>
    string DiagnosticsLog { get; }

    /// <summary>Resolves a path relative to <see cref="Root"/>.</summary>
    string FromRoot(params string[] segments);
}
