using System;
using System.Collections.Generic;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Application.Abstractions;

/// <summary>How far a scenery sweep has got, for the splash screen.</summary>
public readonly record struct SceneryLoadProgress(int Loaded, int Total, string FileName);

/// <summary>
/// Reads the scenarios of an installation. The only route from a .scn on disk to
/// a <see cref="Scenery"/>; nothing else constructs one from a path.
/// </summary>
public interface ISceneryRepository
{
    /// <summary>
    /// Every scenario in the installation, in no particular order. Files that
    /// fail to parse are logged and skipped rather than failing the load.
    /// </summary>
    IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null);

    /// <summary>Reloads a single scenario; null when it is gone or unreadable.</summary>
    Scenery? Load(string path);
}
