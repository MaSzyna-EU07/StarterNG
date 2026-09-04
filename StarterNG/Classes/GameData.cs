using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Classes;

public enum LoadPhase
{
    Vehicles,
    Sceneries,
    Done
}

public readonly record struct LoadStatus(double Fraction, LoadPhase Phase, string? Detail);

public sealed class GameData
{
    public static GameData Instance { get; } = new();

    public VehicleDatabase Vehicles { get; } = new();
    public List<Scenery> Sceneries { get; } = new();

    public bool Loaded { get; private set; }

    public void Load(IProgress<LoadStatus>? progress = null,
                     string miniDir = "textures/mini/",
                     string dynamicRoot = "dynamic")
    {
        if (Loaded)
            return;

        progress?.Report(new LoadStatus(0, LoadPhase.Vehicles, "textures.txt"));
        int vehicles = Vehicles.LoadFromTexturesTxt(dynamicRoot);
        if (vehicles == 0)
            Infrastructure.Diagnostics.Log($"{dynamicRoot}: brak plikow textures.txt");

        Parallel.Invoke(
            () => VehicleDatabase.PreloadMiniIndex(miniDir),
            () => Physics.PreloadIndex());

        var sceneryProgress = progress is null
            ? null
            : new Progress<SceneryLoadProgress>(step => progress.Report(
                new LoadStatus((double)step.Loaded / step.Total, LoadPhase.Sceneries, step.FileName)));
        ReloadSceneries(sceneryProgress);

        progress?.Report(new LoadStatus(1.0, LoadPhase.Done, null));
        Loaded = true;
    }

    public void ReloadSceneries(IProgress<SceneryLoadProgress>? progress = null)
    {
        Sceneries.Clear();
        Sceneries.AddRange(AppServices.Current.Sceneries.LoadAll(progress));
    }
}
