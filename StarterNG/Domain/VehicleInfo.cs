using StarterNG.Classes;

namespace StarterNG.Domain;

public sealed class VehicleInfo
{
    private readonly VehicleDatabase _db;

    public VehicleInfo(VehicleDatabase db) => _db = db;

    public static string ClassOf(VehicleTexture t) => t.ResolvedClass;

    public static string? CategoryOf(VehicleTexture t) => t.ResolvedCategory;

    public static bool IsPoweredCategory(string? c) =>
        c is "e" or "s" or "p" or "z" or "a";

    public VehicleTexture? TextureFor(Dynamic car) => _db.TextureForSkin(car.SkinFile);

    public string? CategoryOf(Dynamic car) =>
        TextureFor(car) is { } t ? CategoryOf(t) : null;

    public Physics? PhysicsFor(Dynamic car)
    {
        string? dbModel = TextureFor(car)?.Model;
        return Physics.For(car.DataFolder, dbModel)
            ?? Physics.For(car.DataFolder, car.MmdFile)
            ?? Physics.For(car.DataFolder, car.SkinFile);
    }
}
