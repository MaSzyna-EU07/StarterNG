using System;
using StarterNG.Domain.Sceneries;

using StarterNG.Classes;

namespace StarterNG.Application;

public sealed class AppState
{
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
