using StarterNG.Domain.Vehicles;

namespace StarterNG.Application.Abstractions;

public interface IVehicleRepository
{
    int Load(VehicleCatalog catalog);
}
