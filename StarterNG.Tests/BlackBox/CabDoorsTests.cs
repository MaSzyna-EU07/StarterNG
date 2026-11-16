using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

// The events are coloured with the views' brushes.
[Collection(AvaloniaControls.Name)]
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

    [Fact]
    public void Going_to_the_other_cab_tells_nothing_of_the_doors_and_still_tells_the_switches()
    {
        var f = new BlackBoxFile().Info().Vehicle(1, "EN57-1001").User(0, 1, true);
        // the left doors permitted from the front cab; in the rear one the permit is still on, and the radio turned off
        f.Cab(0, 1, 1000, 0, cabEnd: 1, devices: 0x0007 | (uint)(CabDevices.DoorPermitLeft | CabDevices.Radio));
        f.Cab(1, 1, 1000, 0, cabEnd: -1, devices: 0x0007 | (uint)CabDevices.DoorPermitLeft);

        var events = EventList.Collect(BlackBoxReader.Read(f.End(1).Stream()), new DefaultBlackBoxStrings("en"))
                              .Select(e => e.Text).ToList();

        Assert.Contains("Radio: off", events);
        Assert.DoesNotContain(events, e => e.Contains("doors permitted"));
    }
}
