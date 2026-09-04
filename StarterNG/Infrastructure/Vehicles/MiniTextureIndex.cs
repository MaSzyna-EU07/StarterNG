using System;
using System.Collections.Generic;
using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// <see cref="IMiniTextureIndex"/> over the installation's textures/mini folder.
/// </summary>
/// <remarks>
/// Replaces the static thumbnail dictionary that used to live on the vehicle
/// database: an installation-scoped lookup with its own lifetime, which is what
/// lets a test point the depot at thumbnails that only exist in memory.
/// </remarks>
public sealed class MiniTextureIndex : IMiniTextureIndex
{
    /// <summary>Thumbnail shown when a vehicle names one we do not have.</summary>
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
