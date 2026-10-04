using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

// The simulator puts end signals only on a last vehicle whose coupler is 0.
public class ConsistTailTests
{
    private readonly Consist _consist;
    private readonly VehicleCatalog _catalog;

    public ConsistTailTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("dynamic", "pkp", "b111", "textures.txt"),
                      "!=b\nb111-01.mat=b111,B111\nb111-02.mat=b111,B111"));
        installation.Vehicles.Load(installation.Library.Vehicles);
        _catalog = installation.Library.Vehicles;
        _consist = new Consist(_catalog, new VehicleInfo(_catalog, installation.Physics));
    }

    [Fact]
    public void A_new_last_car_is_left_uncoupled_at_the_back()
    {
        var first = Append("b111-01");
        var last = Append("b111-02");

        _consist.AutoConnectAll();

        Assert.NotEqual(0, first.Cars[0].Coupling.Flags);
        Assert.Equal(0, last.Cars[0].Coupling.Flags);
    }

    [Fact]
    public void Taking_off_the_last_car_frees_the_one_now_at_the_back()
    {
        var first = Append("b111-01");
        var last = Append("b111-02");
        _consist.AutoConnectAll();

        _consist.Remove(last);

        Assert.Equal(0, first.Cars[0].Coupling.Flags);
    }

    [Fact]
    public void Freeing_the_back_keeps_the_brake_setting()
    {
        var car = Append("b111-01");
        car.Cars[0].Coupling = Coupling.Parse("3.BP");

        _consist.AutoConnectAll();

        Assert.Equal("0.BP", car.Cars[0].Coupling.ToString());
    }

    private ConsistItem Append(string skin)
    {
        var item = new ConsistItem
        {
            Cars = new List<Dynamic> { _consist.MakeDynamic(_catalog.TextureForSkin(skin)!, null) }
        };
        _consist.Insert(_consist.Count, item);
        return item;
    }
}
