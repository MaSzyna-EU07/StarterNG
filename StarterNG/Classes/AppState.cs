using System;

namespace StarterNG.Classes;

/// <summary>
/// Shared selection state across views: the scenery and trainset currently in
/// focus. The depot edits this trainset in place, so changes made in the depot
/// are reflected on the scenery (both views share the same Scenery/Trainset
/// objects from <see cref="GameData"/>).
/// </summary>
public sealed class AppState
{
    public static AppState Instance { get; } = new();

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

    /// <summary>
    /// Node name of the vehicle the player starts in (<c>-v</c>), like Pascal
    /// <c>SelVehicle</c>. Updated when a consist car is clicked in the depot or a
    /// trainset is picked in Scenarios.
    /// </summary>
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

    /// <summary>Notify listeners after in-place edits that affect Start enablement.</summary>
    public void NotifyChanged() => Changed?.Invoke();
}
