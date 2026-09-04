using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// Set to 1-100 to make that percentage of thumbnails behave as if the
    /// installation did not have their file - the vehicle then falls back to
    /// other.bmp exactly as one naming an unknown thumbnail would. For checking
    /// how a consist looks when some of its stock has no picture of its own.
    /// Off unless the variable is set.
    /// </summary>
    public const string MissRateVariable = "STARTER_DEBUG_MISSING_MINI";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly int _missRate;
    private readonly object _gate = new();

    private Dictionary<string, string>? _index;

    public MiniTextureIndex(IFileSystem files, IGamePaths paths, IEnvironment? environment = null)
    {
        _files = files;
        _paths = paths;
        _missRate = ReadMissRate(environment);
    }

    /// <summary>
    /// Percentage of thumbnails currently being hidden on purpose; 0 in a normal
    /// run.
    /// </summary>
    public int SimulatedMissRate => _missRate;

    public void Preload() => Index();

    public bool Has(string? miniName) =>
        !string.IsNullOrEmpty(miniName) &&
        !IsSimulatedMiss(miniName) &&
        Index().ContainsKey(miniName.ToLowerInvariant());

    public string? PathFor(string? miniName)
    {
        if (!IsSimulatedMiss(miniName) && !string.IsNullOrEmpty(miniName) &&
            Index().TryGetValue(miniName.ToLowerInvariant(), out string? path))
            return path;

        return FallbackPath;
    }

    public string? FallbackPath =>
        Index().TryGetValue(FallbackName, out string? path) ? path : null;

    /// <summary>
    /// Whether this name is one of the thumbnails the debug switch is hiding.
    /// Decided from the name itself rather than a random draw, so a vehicle keeps
    /// the same answer every time the strip is rebuilt and can actually be looked
    /// at.
    /// </summary>
    private bool IsSimulatedMiss(string? miniName) =>
        _missRate > 0 && !string.IsNullOrEmpty(miniName) && Bucket(miniName) < _missRate;

    /// <summary>FNV-1a over the lower-cased name, folded into 0-99.</summary>
    private static int Bucket(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (int)(hash % 100);
    }

    private static int ReadMissRate(IEnvironment? environment)
    {
        string? raw = environment?.GetVariable(MissRateVariable);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate)
            ? Math.Clamp(rate, 0, 100)
            : 0;
    }

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
