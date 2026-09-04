using StarterNG.Domain.Vehicles;

namespace StarterNG.Application.Abstractions;

public interface IPhysicsRepository
{
    void Preload();

    int IndexedCount { get; }

    VehiclePhysics? For(string? dataFolder, string? model);
}
