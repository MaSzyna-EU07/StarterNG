using System;
using StarterNG.Domain.Sceneries;

using StarterNG.Classes;

namespace StarterNG.Application;

/// <summary>
/// What the user currently has selected: which scenario, which consist in it and
/// which vehicle they will start in.
/// </summary>
/// <remarks>
/// Session state rather than domain state, which is why it lives in the
/// application layer. Built by the composition root instead of being an ambient
/// singleton; screens observe <see cref="Changed"/> to stay in step.
/// </remarks>
public sealed class AppState
{
    /// <summary>Raised whenever any of the selections changes.</summary>
    public event Action? Changed;

    private Scenery? _currentScenery;
    private Trainset? _currentTrainset;
    private string? _startingVehicleName;

    public Scenery? CurrentScenery
    {
        get => _currentScenery;
        set
        {
            if (ReferenceEquals(_currentScenery, value)) return;
            _currentScenery = value;
            Changed?.Invoke();
        }
    }

    public Trainset? CurrentTrainset
    {
        get => _currentTrainset;
        set
        {
            if (ReferenceEquals(_currentTrainset, value)) return;
            _currentTrainset = value;
            Changed?.Invoke();
        }
    }

    public string? StartingVehicleName
    {
        get => _startingVehicleName;
        set
        {
            if (_startingVehicleName == value) return;
            _startingVehicleName = value;
            Changed?.Invoke();
        }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
