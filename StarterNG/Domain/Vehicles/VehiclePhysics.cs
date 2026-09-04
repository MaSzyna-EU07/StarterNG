using System;

namespace StarterNG.Domain.Vehicles;

/// <summary>
/// The physical properties of a vehicle type, as declared by its .fiz file:
/// what it weighs, how fast it goes, what it can carry and what it couples to.
/// </summary>
/// <remarks>
/// Was <c>Classes.Physics</c>, which also owned the .fiz index, the parser and a
/// static cache. Those are now <c>Infrastructure.Vehicles.FizPhysicsRepository</c>;
/// this is the value the rest of the application reasons about.
/// </remarks>
public sealed class VehiclePhysics
{
    /// <summary>Tare mass in kilograms.</summary>
    public double Mass { get; set; }

    /// <summary>Design speed in km/h.</summary>
    public double VMax { get; set; }

    /// <summary>Length over buffers in metres.</summary>
    public double Length { get; set; }

    public string LoadAccepted { get; set; } = "";

    public string LoadQ { get; set; } = "";

    public int MaxLoad { get; set; }

    /// <summary>Coupling capabilities of the front end.</summary>
    public int AllowedFlagA { get; set; } = 3;

    /// <summary>Coupling capabilities of the rear end.</summary>
    public int AllowedFlagB { get; set; } = 3;

    public string ControlTypeA { get; set; } = "";

    public string ControlTypeB { get; set; } = "";

    public string EngineType { get; set; } = "";

    /// <summary>
    /// True for the engine types the simulator actually runs its diesel heat model for.
    /// Mover.cpp calls dizel_Update - and through it dizel_Heat - only for DieselEngine
    /// and DieselElectric; LoadFIZ_EngineDecode maps the legacy "DumbDE" onto
    /// DieselElectric. On anything else the coolant temperature struct is written and
    /// never read.
    /// </summary>
    public bool IsDieselEngine =>
        EngineType.Equals("DieselEngine", StringComparison.OrdinalIgnoreCase)
        || EngineType.Equals("DieselElectric", StringComparison.OrdinalIgnoreCase)
        || EngineType.Equals("DumbDE", StringComparison.OrdinalIgnoreCase);
}
