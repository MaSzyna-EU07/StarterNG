using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class ConsistCrewTests
{
    private readonly Consist _consist;
    private readonly VehicleCatalog _catalog;

    public ConsistCrewTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "ep07", "textures.txt"),
                      "!=e\nep07-001.mat=ep07,EP07\nep07-002.mat=ep07,EP07")
            .WithFile(TestInstallation.At("dynamic", "pkp", "b111", "textures.txt"),
                      "!=b\nb111-01.mat=b111,B111"));
        installation.Vehicles.Load(installation.Library.Vehicles);

        _catalog = installation.Library.Vehicles;
        _consist = new Consist(_catalog, new VehicleInfo(_catalog, installation.Physics));
    }

    [Fact]
    public void A_locomotive_added_at_the_head_gets_a_driver_in_cab_a()
    {
        var loco = Add("ep07-001", 0);

        Assert.Equal(eDriverType.Headdriver, loco.Cars[0].DriverType);
    }

    [Fact]
    public void Replacing_the_locomotive_keeps_the_driver_on_the_new_one()
    {
        Add("ep07-001", 0);
        Add("b111-01", 1);

        var replacement = Replace(0, "ep07-002");

        Assert.Equal(eDriverType.Headdriver, replacement.Cars[0].DriverType);
        Assert.Equal(eDriverType.Headdriver, replacement.Driver);
    }

    [Fact]
    public void An_unstaffed_locomotive_replaced_at_the_head_is_staffed_in_cab_a()
    {
        var loco = Add("ep07-001", 0);
        loco.Cars[0].DriverType = eDriverType.Nobody;
        loco.Driver = eDriverType.Nobody;

        var replacement = Replace(0, "ep07-002");

        Assert.Equal(eDriverType.Headdriver, replacement.Cars[0].DriverType);
    }

    [Fact]
    public void A_driver_in_cab_b_stays_in_cab_b()
    {
        var loco = Add("ep07-001", 0);
        loco.Cars[0].DriverType = eDriverType.Reardriver;
        loco.Driver = eDriverType.Reardriver;

        var replacement = Replace(0, "ep07-002");

        Assert.Equal(eDriverType.Reardriver, replacement.Cars[0].DriverType);
    }

    [Fact]
    public void A_wagon_put_in_place_of_the_locomotive_gets_no_driver()
    {
        Add("ep07-001", 0);

        var wagon = Replace(0, "b111-01");

        Assert.Equal(eDriverType.Nobody, wagon.Cars[0].DriverType);
    }

    [Fact]
    public void A_second_locomotive_behind_a_staffed_one_stays_unstaffed()
    {
        Add("ep07-001", 0);

        var second = Add("ep07-002", 1);

        Assert.Equal(eDriverType.Nobody, second.Cars[0].DriverType);
    }

    [Fact]
    public void A_locomotive_moved_to_the_head_takes_the_driver_from_the_one_it_put_behind()
    {
        var first = Add("ep07-001", 0);
        var second = Add("ep07-002", 1);

        _consist.Move(1, 0);

        Assert.Equal(eDriverType.Headdriver, second.Cars[0].DriverType);
        Assert.Equal(eDriverType.Nobody, first.Cars[0].DriverType);
        Assert.Equal(eDriverType.Nobody, first.Driver);
    }

    [Fact]
    public void Stepping_a_locomotive_left_onto_the_head_hands_over_the_same_way()
    {
        var first = Add("ep07-001", 0);
        var second = Add("ep07-002", 1);

        _consist.MoveLeft(second);

        Assert.Equal(eDriverType.Headdriver, second.Cars[0].DriverType);
        Assert.Equal(eDriverType.Nobody, first.Cars[0].DriverType);
    }

    [Fact]
    public void A_wagon_moved_to_the_head_leaves_the_driver_where_he_is()
    {
        var loco = Add("ep07-001", 0);
        Add("b111-01", 1);

        _consist.Move(1, 0);

        Assert.Equal(eDriverType.Headdriver, loco.Cars[0].DriverType);
    }

    [Fact]
    public void A_unit_staffed_at_the_head_has_its_driver_in_cab_1_of_the_first_car()
    {
        var unit = Unit("ep07-001", "ep07-002");

        _consist.Staff(unit, 0);

        Assert.Equal(eDriverType.Headdriver, unit.Cars[0].DriverType);
        Assert.Equal(eDriverType.Nobody, unit.Cars[1].DriverType);
    }

    [Fact]
    public void A_driver_in_cab_2_moves_into_the_last_car_of_a_unit()
    {
        var loco = Add("ep07-001", 0);
        loco.Cars[0].DriverType = eDriverType.Reardriver;
        loco.Driver = eDriverType.Reardriver;
        var unit = Unit("ep07-001", "ep07-002");

        _consist.Replace(0, unit);

        Assert.Equal(eDriverType.Nobody, unit.Cars[0].DriverType);
        Assert.Equal(eDriverType.Reardriver, unit.Cars[1].DriverType);
        Assert.Equal(eDriverType.Reardriver, unit.Driver);
    }

    private ConsistItem Unit(params string[] skins) =>
        new()
        {
            Cars = skins.Select(skin => _consist.MakeDynamic(_catalog.TextureForSkin(skin)!, null)).ToList(),
            Grouped = true
        };

    private ConsistItem Add(string skin, int at)
    {
        var item = Item(skin);
        _consist.Staff(item, at);
        _consist.Insert(at, item);
        return item;
    }

    private ConsistItem Replace(int at, string skin)
    {
        var item = Item(skin);
        _consist.Replace(at, item);
        return item;
    }

    private ConsistItem Item(string skin) =>
        new() { Cars = new List<Dynamic> { _consist.MakeDynamic(_catalog.TextureForSkin(skin)!, null) } };
}
