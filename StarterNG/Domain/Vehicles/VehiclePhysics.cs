using System;

namespace StarterNG.Domain.Vehicles;

public sealed class VehiclePhysics
{
    public double Mass { get; set; }

    public double VMax { get; set; }

    public double Length { get; set; }

    public string LoadAccepted { get; set; } = "";

    public string LoadQ { get; set; } = "";

    public int MaxLoad { get; set; }

    public int AllowedFlagA { get; set; } = 3;

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
