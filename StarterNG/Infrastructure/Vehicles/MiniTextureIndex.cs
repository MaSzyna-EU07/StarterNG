using System;
using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Vehicles;

public sealed class MiniTextureIndex : IMiniTextureIndex
{
    private const string FallbackName = "other";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly object _gate = new();

    private Dictionary<string, string>? _index;

    public MiniTextureIndex(IFileSystem files, IGamePaths paths)
    {
        _files = files;
        _paths = paths;
    }

    public void Preload() => Index();

    public bool Has(string? miniName) =>
        !string.IsNullOrEmpty(miniName) && Index().ContainsKey(miniName.ToLowerInvariant());

    public string? PathFor(string? miniName)
    {
        if (!string.IsNullOrEmpty(miniName) &&
            Index().TryGetValue(miniName.ToLowerInvariant(), out string? path))
            return path;

        return FallbackPath;
    }

    public string? FallbackPath =>
        Index().TryGetValue(FallbackName, out string? path) ? path : null;

    private Dictionary<string, string> Index()
    {
        lock (_gate)
        {
            if (_index is not null)
                return _index;

            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in _files.GetFiles(_paths.MiniTextures, "*.bmp"))
                index[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = file;

            _index = index;
            return index;
        }
    }
}
