using StarterNG.Classes;
using StarterNG.Domain.Vehicles;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain;

public sealed class VehicleInfo
{
    private readonly VehicleCatalog _db;
    private readonly IPhysicsRepository _physics;

    public VehicleInfo(VehicleCatalog db, IPhysicsRepository physics)
    {
        _db = db;
        _physics = physics;
    }

    public static string ClassOf(VehicleTexture t) => t.ResolvedClass;

    public static string? CategoryOf(VehicleTexture t) => t.ResolvedCategory;

    public static bool IsPoweredCategory(string? c) =>
        c is "e" or "s" or "p" or "z" or "a";

    public VehicleTexture? TextureFor(Dynamic car) => _db.TextureForSkin(car.SkinFile);

    public string? CategoryOf(Dynamic car) =>
        TextureFor(car) is { } t ? CategoryOf(t) : null;

    public VehiclePhysics? PhysicsFor(VehicleTexture texture) =>
        _physics.For(texture.Directory, texture.Model)
        ?? _physics.For(texture.Directory, texture.Skinfile);

    public VehiclePhysics? PhysicsFor(Dynamic car)
    {
        string? dbModel = TextureFor(car)?.Model;
        return _physics.For(car.DataFolder, dbModel)
            ?? _physics.For(car.DataFolder, car.MmdFile)
            ?? _physics.For(car.DataFolder, car.SkinFile);
    }
}
