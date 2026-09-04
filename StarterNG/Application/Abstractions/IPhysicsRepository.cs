using StarterNG.Domain.Vehicles;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// Looks up the .fiz physics of a vehicle type by model name.
/// </summary>
public interface IPhysicsRepository
{
    /// <summary>Indexes the installation's .fiz files up front, off the UI thread.</summary>
    void Preload();

    /// <summary>How many vehicle types were indexed; zero means a broken installation.</summary>
    int IndexedCount { get; }

    /// <summary>
    /// Physics for a model name (with or without extension), or null when the
    /// installation has no .fiz for it.
    /// </summary>
    VehiclePhysics? For(string? dataFolder, string? model);
}
