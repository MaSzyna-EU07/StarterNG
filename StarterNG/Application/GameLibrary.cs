using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Application;

public enum LoadPhase
{
    Vehicles,
    Sceneries,
    Done
}

public readonly record struct LoadStatus(double Fraction, LoadPhase Phase, string? Detail);

public sealed class GameLibrary
{
    private readonly IVehicleRepository _vehicles;
    private readonly ISceneryRepository _sceneries;
    private readonly IMiniTextureIndex _minis;
    private readonly IPhysicsRepository _physics;
    private readonly IDiagnosticsLog _log;

    public GameLibrary(IVehicleRepository vehicles, ISceneryRepository sceneries, IMiniTextureIndex minis,
                       IPhysicsRepository physics, IDiagnosticsLog log)
    {
        _vehicles = vehicles;
        _sceneries = sceneries;
        _minis = minis;
        _physics = physics;
        _log = log;
        Vehicles = new VehicleCatalog(minis);
    }

    public VehicleCatalog Vehicles { get; }

    public List<Scenery> Sceneries { get; } = new();

    public bool Loaded { get; private set; }

    public void Load(IProgress<LoadStatus>? progress = null)
    {
        if (Loaded)
            return;

        progress?.Report(new LoadStatus(0, LoadPhase.Vehicles, "textures.txt"));
        if (_vehicles.Load(Vehicles) == 0)
            _log.Log("dynamic: brak plikow textures.txt");

        Parallel.Invoke(_minis.Preload, _physics.Preload);

        ReloadSceneries(progress is null
            ? null
            : new Progress<SceneryLoadProgress>(step => progress.Report(
                new LoadStatus((double)step.Loaded / step.Total, LoadPhase.Sceneries, step.FileName))));

        progress?.Report(new LoadStatus(1.0, LoadPhase.Done, null));
        Loaded = true;
    }

    public void ReloadSceneries(IProgress<SceneryLoadProgress>? progress = null)
    {
        var reloaded = _sceneries.LoadAll(progress);
        Sceneries.Clear();
        Sceneries.AddRange(reloaded);
    }
}
