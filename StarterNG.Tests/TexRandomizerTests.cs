using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class TexRandomizerTests
{
    private readonly Consist _consist;
    private readonly VehicleCatalog _catalog;

    public TexRandomizerTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"),
                      "!=e\nep07-001.mat=ep07,EP07\nep07-002.mat=ep07,EP07")
            .WithFile(TestInstallation.At("dynamic", "pkp", "en57", "textures.txt"),
                      "!=z\n^2\nen57-a.mat=en57a,EN57\nen57-b.mat=en57b,EN57\nen57-c.mat=en57a,EN57\nen57-d.mat=en57b,EN57")
            .WithFile(TestInstallation.At("dynamic", "pkp", "b111", "textures.txt"),
                      "!=b\nb111-01.mat=b111,B111\nb111-02.mat=b111,B111"));
        installation.Vehicles.Load(installation.Library.Vehicles);

        _catalog = installation.Library.Vehicles;
        _consist = new Consist(_catalog, new VehicleInfo(_catalog, installation.Physics));
    }

    private Dynamic Car(string skin) => _consist.MakeDynamic(_catalog.TextureForSkin(skin)!, null);

    private ConsistItem Put(params Dynamic[] cars)
    {
        var item = new ConsistItem
        {
            Cars = cars.ToList(),
            Grouped = cars.Length > 1,
            Driver = Consist.UnitDriver(cars)
        };
        _consist.Insert(_consist.Count, item);
        return item;
    }

    /// <summary>The randomizer with the next pick in line always the other livery of two.</summary>
    private int Randomize() => new TexRandomizer(_catalog, new SecondPick()).Apply(_consist, t => _consist.MakeDynamic(t, null));

    private sealed class SecondPick : Random
    {
        public override int Next(int maxValue) => 1;
    }

    [Theory]
    [InlineData(eDriverType.Headdriver)]
    [InlineData(eDriverType.Reardriver)]
    [InlineData(eDriverType.Passenger)]
    public void The_crew_stays_on_the_vehicle_that_takes_over(eDriverType crew)
    {
        var loco = Car("ep07-001");
        loco.DriverType = crew;
        Put(loco);

        Assert.Equal(1, Randomize());

        var car = Assert.Single(_consist[0].Cars);
        Assert.Equal("ep07-002", car.SkinFile);
        Assert.Equal(crew, car.DriverType);
        Assert.Equal(crew, _consist[0].Driver);
    }

    [Fact]
    public void A_driver_kept_is_what_the_export_writes()
    {
        var loco = Car("ep07-001");
        loco.DriverType = eDriverType.Headdriver;
        Put(loco);

        Randomize();

        Assert.Contains(" headdriver ", _consist[0].Cars[0].ToTrainsetNode());
    }

    [Fact]
    public void An_empty_locomotive_stays_empty()
    {
        // Consist.Replace would staff it; a driver of its own is not this one's to give.
        Put(Car("ep07-001"));

        Randomize();

        Assert.Equal(eDriverType.Nobody, _consist[0].Cars[0].DriverType);
        Assert.Equal(eDriverType.Nobody, _consist[0].Driver);
    }

    [Fact]
    public void A_vehicle_keeps_facing_the_way_it_did()
    {
        var loco = Car("ep07-001");
        loco.Offset = -1f;
        var item = Put(loco);
        item.Flipped = true;

        Randomize();

        Assert.True(_consist[0].Flipped);
        Assert.True(Consist.IsTurned(_consist[0].Cars[0]));
    }

    [Fact]
    public void A_vehicle_keeps_its_load()
    {
        var wagon = Car("b111-01");
        wagon.LoadType = "Passengers";
        wagon.LoadCount = 40;
        wagon.MaxLoad = 60;
        Put(wagon);

        Assert.Equal(1, Randomize());

        var car = _consist[0].Cars[0];
        Assert.Equal("b111-02", car.SkinFile);
        Assert.Equal("Passengers", car.LoadType);
        Assert.Equal(40, car.LoadCount);
        Assert.Equal(60, car.MaxLoad);
    }

    [Fact]
    public void A_unit_of_the_same_length_keeps_all_of_it_car_by_car()
    {
        var first = Car("en57-a");
        var second = Car("en57-b");
        second.DriverType = eDriverType.Reardriver;
        first.LoadType = "Passengers";
        first.LoadCount = 10;
        second.LoadType = "Passengers";
        second.LoadCount = 20;
        first.Offset = -1f;
        second.Offset = -1f;
        var item = Put(first, second);
        item.Flipped = true;

        Assert.Equal(1, Randomize());

        var unit = _consist[0];
        Assert.Equal(new[] { "en57-c", "en57-d" }, unit.Cars.Select(c => c.SkinFile));
        Assert.True(unit.Grouped);
        Assert.True(unit.Flipped);
        Assert.Equal(new[] { eDriverType.Nobody, eDriverType.Reardriver }, unit.Cars.Select(c => c.DriverType));
        Assert.Equal(eDriverType.Reardriver, unit.Driver);
        Assert.Equal(new[] { 10, 20 }, unit.Cars.Select(c => c.LoadCount));
        Assert.All(unit.Cars, c => Assert.True(Consist.IsTurned(c)));
    }

    [Theory]
    [InlineData(eDriverType.Headdriver, 0)]
    [InlineData(eDriverType.Reardriver, 1)]
    public void A_crew_moving_into_a_longer_unit_sits_in_the_car_with_its_cab(eDriverType crew, int car)
    {
        var loco = Car("en57-c");
        loco.DriverType = crew;
        Put(loco);

        Assert.Equal(1, new TexRandomizer(_catalog, new FirstPick()).Apply(_consist, t => _consist.MakeDynamic(t, null)));

        var unit = _consist[0];
        Assert.Equal(2, unit.Cars.Count);
        Assert.Equal(crew, unit.Cars[car].DriverType);
        Assert.Equal(eDriverType.Nobody, unit.Cars[1 - car].DriverType);
        Assert.Equal(crew, unit.Driver);
    }

    private sealed class FirstPick : Random
    {
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public void A_selected_vehicle_stays_selected_as_the_one_that_took_over()
    {
        var item = Put(Car("ep07-001"));
        _consist.Selected = item;

        Randomize();

        Assert.Same(_consist[0], _consist.Selected);
    }

    [Fact]
    public void A_vehicle_not_selected_does_not_take_the_selection()
    {
        // ep07-001's turn picks ep07-002; ep07-002's turn picks itself and passes.
        Put(Car("ep07-001"));
        var selected = Put(Car("ep07-002"));
        _consist.Selected = selected;

        Assert.Equal(1, Randomize());

        Assert.Same(selected, _consist.Selected);
    }
}
