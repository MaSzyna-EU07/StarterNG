using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class SceneryImageLocator
{
    private readonly IFileSystem _files;
    private readonly CaseInsensitivePaths _names;
    private readonly Dictionary<string, string?> _cache = new();

    public SceneryImageLocator(IFileSystem files)
    {
        _files = files;
        _names = new CaseInsensitivePaths(files);
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
        if (Path.IsPathFullyQualified(name))
            return _names.Find("", name);
        if (_files.FileExists(name))
            return name;

        string sceneryDir = Path.GetDirectoryName(scenery.Path) ?? ".";
        string root = Path.GetDirectoryName(sceneryDir) ?? ".";

        (string Folder, string Name)[] candidates =
        {
            (root, name),
            (sceneryDir, name),
            (sceneryDir, "images/" + name),
            (root, "scenery/images/" + name)
        };

        return candidates.Select(candidate => _names.Find(candidate.Folder, candidate.Name))
                         .FirstOrDefault(found => found is not null);
    }
}
