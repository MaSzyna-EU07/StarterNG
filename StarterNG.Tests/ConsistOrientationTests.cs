using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class ConsistOrientationTests
{
    private readonly VehicleCatalog _catalog;
    private readonly TestInstallation _installation;

    public ConsistOrientationTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "et40", "textures.txt"),
                      "!=e\n^2\net40-10-a.mat=et40a,ET40-A\net40-10-b.mat=et40b,ET40-B\n" +
                      "et40-16-a.mat=et40a,ET40-A\net40-16-b.mat=et40b,ET40-B"));
        installation.Vehicles.Load(installation.Library.Vehicles);
        _catalog = installation.Library.Vehicles;
        _installation = installation;
    }

    [Fact]
    public void Randomly_turned_unit_comes_out_like_a_flipped_one()
    {
        var flipped = NewConsist(out var unit);
        flipped.Flip(unit);

        var randomized = NewConsist(out _);
        randomized.RandomizeOrientation(new AlwaysTurn());

        Assert.Equal(Describe(flipped), Describe(randomized));
    }

    [Fact]
    public void A_turned_unit_has_its_cars_reversed_and_each_one_turned()
    {
        var consist = NewConsist(out _);

        consist.RandomizeOrientation(new AlwaysTurn());

        // B leads, A trails, and both face backwards - not A-B reordered but facing forward.
        Assert.Equal(new[] { "et40-10-b -1", "et40-10-a -1" }, Describe(consist));
    }

    [Fact]
    public void A_unit_put_in_place_of_a_turned_one_faces_the_same_way()
    {
        var consist = NewConsist(out var unit);
        consist.Flip(unit);

        consist.Replace(0, Unit(consist, "et40-16-a"));

        Assert.Equal(new[] { "et40-16-b -1", "et40-16-a -1" }, Describe(consist));
    }

    private Consist NewConsist(out ConsistItem unit)
    {
        var consist = new Consist(_catalog, new VehicleInfo(_catalog, _installation.Physics));
        unit = Unit(consist, "et40-10-a");
        consist.Insert(0, unit);
        return consist;
    }

    private ConsistItem Unit(Consist consist, string lead) => new()
    {
        Cars = _catalog.ResolveSet(_catalog.TextureForSkin(lead)!)!
                       .Select(texture => consist.MakeDynamic(texture, null))
                       .ToList(),
        Grouped = true
    };

    // The export order, each car with the way it faces.
    private static string[] Describe(Consist consist) =>
        consist.Flatten().Select(car => $"{car.SkinFile} {(car.Offset < 0 ? -1 : 1)}").ToArray();

    private sealed class AlwaysTurn : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
