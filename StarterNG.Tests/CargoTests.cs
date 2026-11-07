using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class CargoTests
{
    private readonly Consist _consist;
    private readonly VehicleCatalog _catalog;
    private readonly VehicleInfo _info;

    public CargoTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "en57", "textures.txt"), "!=b\nen57-a.mat=en57,EN57")
            .WithFile(TestInstallation.At("dynamic", "pkp", "en57", "en57.fiz"),
                      "Load: LoadAccepted=Passengers,Pantstate MaxLoad=50")
            .WithFile(TestInstallation.At("dynamic", "pkp", "tram", "textures.txt"), "!=b\ntram-a.mat=tram,TRAM")
            .WithFile(TestInstallation.At("dynamic", "pkp", "tram", "tram.fiz"),
                      "Load: LoadAccepted=pantstate,Passengers MaxLoad=52")
            .WithFile(TestInstallation.At("dynamic", "pkp", "s1", "textures.txt"), "!=b\ns1-a.mat=s1,S1")
            .WithFile(TestInstallation.At("dynamic", "pkp", "s1", "s1.fiz"), "Load: LoadAccepted=Coal,Sand"));
        installation.Vehicles.Load(installation.Library.Vehicles);

        _catalog = installation.Library.Vehicles;
        _info = new VehicleInfo(_catalog, installation.Physics);
        _consist = new Consist(_catalog, _info);
    }

    private (Dynamic Car, List<ConsistItem> Consist) Wagon(string skin, string? type = null, int count = 0)
    {
        var car = _consist.MakeDynamic(_catalog.TextureForSkin(skin)!, null);
        car.LoadType = type;
        car.LoadCount = count;
        return (car, new List<ConsistItem> { new() { Cars = new List<Dynamic> { car } } });
    }

    private Cargo Picking(int index) => new(_info, new Pick(index));

    private sealed class Pick : Random
    {
        private readonly int _index;

        public Pick(int index) => _index = index;

        public override int Next(int maxValue) => _index;

        public override int Next(int minValue, int maxValue) => maxValue - 1;
    }

    [Fact]
    public void A_random_type_takes_no_more_than_it_holds()
    {
        var (car, consist) = Wagon("en57-a", "Passengers", 50);

        Picking(1).RandomTypes(consist);

        Assert.Equal("Pantstate", car.LoadType);
        Assert.Equal(Dynamic.PantStateMax, car.LoadCount);
        Assert.Contains(" 3 Pantstate ", car.ToTrainsetNode());
    }

    [Fact]
    public void A_random_type_leaves_a_count_it_can_hold()
    {
        var (car, consist) = Wagon("en57-a", "Pantstate", 2);

        Picking(0).RandomTypes(consist);

        Assert.Equal("Passengers", car.LoadType);
        Assert.Equal(2, car.LoadCount);
    }

    [Fact]
    public void A_random_type_with_no_limit_declared_keeps_the_count()
    {
        // MaxLoad unset: nothing to hold it to.
        var (car, consist) = Wagon("s1-a", "Coal", 40);

        Picking(1).RandomTypes(consist);

        Assert.Equal("Sand", car.LoadType);
        Assert.Equal(40, car.LoadCount);
    }

    [Fact]
    public void Filling_an_empty_car_goes_by_the_type_it_is_given()
    {
        // Its first accepted type is pantographs: the 52 of the .fiz is not theirs.
        var (car, _) = Wagon("tram-a");

        Picking(0).FillToMax(car);

        Assert.Equal("pantstate", car.LoadType);
        Assert.Equal(Dynamic.PantStateMax, car.LoadCount);
    }

    [Fact]
    public void A_random_amount_for_an_empty_car_goes_by_the_type_it_is_given()
    {
        var (car, consist) = Wagon("tram-a");

        Picking(0).RandomAmounts(consist);

        Assert.Equal("pantstate", car.LoadType);
        Assert.InRange(car.LoadCount, 1, Dynamic.PantStateMax);
    }

    [Fact]
    public void A_count_passed_on_keeps_where_the_next_car_declares_no_limit()
    {
        // MaxLoad unset: nothing to hold it to, as when taken from the car before.
        var (_, consist) = Wagon("s1-a", "Coal", 40);
        var (next, following) = Wagon("s1-a");
        consist.AddRange(following);

        Picking(0).CopyToFollowing(consist, consist[0]);

        Assert.Equal("Coal", next.LoadType);
        Assert.Equal(40, next.LoadCount);
    }

    [Fact]
    public void A_count_passed_on_is_held_to_what_the_next_car_declares()
    {
        var (_, consist) = Wagon("tram-a", "Passengers", 52);
        var (next, following) = Wagon("en57-a");
        consist.AddRange(following);

        Picking(0).CopyToFollowing(consist, consist[0]);

        Assert.Equal("Passengers", next.LoadType);
        Assert.Equal(50, next.LoadCount);
    }

    [Fact]
    public void A_car_with_nothing_declared_stays_untouched()
    {
        var (car, _) = Wagon("en57-a");
        car.MaxLoad = 0;

        Picking(0).FillToMax(car);

        Assert.Null(car.LoadType);
        Assert.Equal(0, car.LoadCount);
    }
}
