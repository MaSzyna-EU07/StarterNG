using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

/// <summary>
/// Finds the preview image a scenery names, which authors place in any of
/// several conventional spots relative to the .scn.
/// </summary>
/// <remarks>
/// Was a lazy property on the aggregate; moved out because it is a lookup on
/// disk. Results are cached per scenery path, which is what the lazy field
/// bought us before.
/// </remarks>
public sealed class SceneryImageLocator
{
    private readonly IFileSystem _files;
    private readonly Dictionary<string, string?> _cache = new();

    public SceneryImageLocator(IFileSystem files)
    {
        _files = files;
    }

    /// <summary>The image file for this scenery, or null when there is none.</summary>
    public string? Resolve(Scenery scenery)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(scenery.Path, out string? cached))
                return cached;

            string? resolved = Search(scenery);
            _cache[scenery.Path] = resolved;
            return resolved;
        }
    }

    private string? Search(Scenery scenery)
    {
        if (string.IsNullOrWhiteSpace(scenery.ImageName))
            return null;

        string name = scenery.ImageName.Replace('\\', '/').Trim();
        string sceneryDir = Path.GetDirectoryName(scenery.Path) ?? ".";
        string root = Path.GetDirectoryName(sceneryDir) ?? ".";

        string[] candidates =
        {
            name,
            Path.Combine(root, name),
            Path.Combine(sceneryDir, name),
            Path.Combine(sceneryDir, "images", name),
            Path.Combine(root, "scenery", "images", name)
        };

        foreach (string candidate in candidates)
            if (_files.FileExists(candidate))
                return candidate;

        return null;
    }
}
