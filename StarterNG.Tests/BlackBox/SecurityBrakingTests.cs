using System.Globalization;
using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

[Collection(AvaloniaControls.Name)]
public class SecurityBrakingTests
{
    // as the simulator writes them: one acknowledgement for both, so the cab signalling's buzzer is the alerter's too
    private const byte CabSignal = 0x02, CabSignalBuzzing = 0x0e, CabSignalBraking = 0x1e, NoPower = 0x10;

    private static Recording Drive(System.Func<double, byte> security)
    {
        var file = new BlackBoxFile().Info().Vehicle(1, "EP07-424").User(0, 1, true);
        for (double t = 0; t <= 20; t += 0.5)
            file.Cab(t, 1, 100, 0, security: security(t));
        return BlackBoxReader.Read(file.Stream());
    }

    private static List<string> Events(Recording recording)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return EventList.Collect(recording, new DefaultBlackBoxStrings("pl")).Select(e => e.Text).ToList();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void The_protection_braking_with_no_power_is_not_the_drivers_fault()
    {
        // no power for the first seconds, then the cab signalling not acknowledged
        var recording = Drive(t => t < 5 ? NoPower : t is >= 10 and < 12 ? (byte)0x1a : (byte)0);

        var braking = Assert.Single(recording.SecurityBrakings());
        Assert.Equal(10, braking.Start, 6);
    }

    [Fact]
    public void A_buzzer_acknowledged_in_time_is_told_apart_from_braking()
    {
        var recording = Drive(t => t is >= 2 and < 4 ? CabSignal : t is >= 4 and < 5.5 ? CabSignalBuzzing : (byte)0);

        var events = Events(recording);

        Assert.Contains("Brzęczyk SHP, potwierdzony po 1.5 s", events);
        Assert.DoesNotContain(events, e => e.StartsWith("HAMOWANIE", System.StringComparison.Ordinal));
        Assert.Empty(recording.SecurityBrakings());
    }

    [Fact]
    public void A_buzzer_cut_by_the_power_is_not_acknowledged()
    {
        var recording = Drive(t => t is >= 2 and < 4 ? CabSignalBuzzing : t is >= 4 and < 8 ? NoPower : (byte)0);

        var events = Events(recording);

        Assert.Contains("Brzęczyk SHP", events);
        Assert.DoesNotContain(events, e => e.Contains("potwierdzony", System.StringComparison.Ordinal));
    }

    [Fact]
    public void A_buzzer_the_other_joins_is_named_by_both()
    {
        // the alerter's buzzer, then a magnet passed before it's acknowledged
        var recording = Drive(t => t is >= 2 and < 4 ? (byte)0x05 : t is >= 4 and < 5 ? (byte)0x0f : (byte)0);

        Assert.Contains("Brzęczyk CA/SHP, potwierdzony po 3.0 s", Events(recording));
    }

    [Fact]
    public void A_buzzer_not_acknowledged_is_followed_by_the_braking()
    {
        // the cab signalling at the start: the buzzer at once, braking when not acknowledged
        var recording = Drive(t => t < 3 ? CabSignalBuzzing : t < 6 ? CabSignalBraking : (byte)0);

        var events = Events(recording);

        Assert.Equal(["Brzęczyk SHP, bez potwierdzenia", "HAMOWANIE PRZEZ SHP"],
                     events.Where(e => e.Contains("SHP", System.StringComparison.Ordinal)));
        Assert.DoesNotContain(events, e => e.Contains("CA", System.StringComparison.Ordinal));
    }
}
