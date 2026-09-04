using StarterNG.Domain.Vehicles;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// Reads the installation's rolling stock. The only route from the dynamic/
/// folder to a <see cref="VehicleCatalog"/>.
/// </summary>
public interface IVehicleRepository
{
    /// <summary>
    /// Fills the catalogue from every textures.txt under dynamic/. Returns the
    /// number of liveries read, which the caller uses to spot an installation
    /// with no rolling stock at all.
    /// </summary>
    int Load(VehicleCatalog catalog);
}
