using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

public class CabDoorsTests
{
    private static CabSample Sample(sbyte cab, CabDevices devices) =>
        default(CabSample) with { CabEnd = cab, Devices = devices | CabDevices.Radio };

    [Fact]
    public void From_the_rear_cab_the_vehicles_right_doors_are_the_drivers_left()
    {
        var seen = Sample(-1, CabDevices.DoorsRight | CabDevices.DoorPermitRight).AsTheCabSees;

        Assert.True(seen.Has(CabDevices.DoorsLeft));
        Assert.True(seen.Has(CabDevices.DoorPermitLeft));
        Assert.False(seen.Has(CabDevices.DoorsRight));
        Assert.False(seen.Has(CabDevices.DoorPermitRight));
        Assert.True(seen.Has(CabDevices.Radio));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void From_the_front_cab_or_out_of_one_the_sides_are_the_vehicles(sbyte cab)
    {
        var sample = Sample(cab, CabDevices.DoorsLeft | CabDevices.DoorPermitRight);

        Assert.Equal(sample, sample.AsTheCabSees);
    }
}
