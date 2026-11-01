using System;
using System.Collections.Generic;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Application.Abstractions;

public readonly record struct SceneryLoadProgress(int Loaded, int Total, string FileName);

public interface ISceneryRepository
{
    IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null);

    Scenery? Load(string path);

    /// <summary>The scenery files there are now; null while their folder is away.</summary>
    IReadOnlyList<string>? ListFiles();
}
