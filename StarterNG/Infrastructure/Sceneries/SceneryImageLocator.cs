using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class SceneryImageLocator
{
    private readonly IFileSystem _files;
    private readonly Dictionary<string, string?> _cache = new();

    public SceneryImageLocator(IFileSystem files)
    {
        _files = files;
    }

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
