using StarterNG.Classes;
using StarterNG.Domain.Vehicles;
using StarterNG.Application;

namespace StarterNG.Domain;

public sealed class VehicleInfo
{
    private readonly VehicleCatalog _db;

    public VehicleInfo(VehicleCatalog db) => _db = db;

    public static string ClassOf(VehicleTexture t) => t.ResolvedClass;

    public static string? CategoryOf(VehicleTexture t) => t.ResolvedCategory;

    public static bool IsPoweredCategory(string? c) =>
        c is "e" or "s" or "p" or "z" or "a";

    public VehicleTexture? TextureFor(Dynamic car) => _db.TextureForSkin(car.SkinFile);

    public string? CategoryOf(Dynamic car) =>
        TextureFor(car) is { } t ? CategoryOf(t) : null;

    public VehiclePhysics? PhysicsFor(VehicleTexture texture) =>
        AppServices.Current.Physics.For(texture.Directory, texture.Model)
        ?? AppServices.Current.Physics.For(texture.Directory, texture.Skinfile);

    public VehiclePhysics? PhysicsFor(Dynamic car)
    {
        string? dbModel = TextureFor(car)?.Model;
        return AppServices.Current.Physics.For(car.DataFolder, dbModel)
            ?? AppServices.Current.Physics.For(car.DataFolder, car.MmdFile)
            ?? AppServices.Current.Physics.For(car.DataFolder, car.SkinFile);
    }
}
