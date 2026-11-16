using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

public class SecurityBrakingTests
{
    [Fact]
    public void The_protection_braking_with_no_power_is_not_the_drivers_fault()
    {
        var file = new BlackBoxFile().Info().Vehicle(1, "EP07-424").User(0, 1, true);
        // no power for the first seconds, then the cab signalling not acknowledged
        for (double t = 0; t <= 20; t += 0.5)
            file.Cab(t, 1, 100, 0, security: (byte)(t < 5 ? 0x10 : t is >= 10 and < 12 ? 0x1a : 0));
        var recording = BlackBoxReader.Read(file.Stream());

        var braking = Assert.Single(recording.SecurityBrakings());
        Assert.Equal(10, braking.Start, 6);
    }
}
