using System;
using System.Collections.Generic;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Application.Abstractions;

public readonly record struct SceneryLoadProgress(int Loaded, int Total, string FileName);

public interface ISceneryRepository
{
    IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null);

    Scenery? Load(string path);
}
