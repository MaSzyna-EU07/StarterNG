using System.Globalization;
using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class HaslerTapeTests
{
    private const string Header =
        "Time[s] Velocity[m/s] Acceleration[m/ss] Coupler.Dist[m] Coupler.Force[N] TractionForce[kN] FrictionForce[kN] " +
        "BrakeForce[kN] BrakePress[MPa] PipePress[MPa] MotorCurrent[A] MCP SCP BCP LBP Direction Command CVal1 CVal2 " +
        "Security Wheelslip EngineTemp[Deg] OilTemp[Deg] WaterTemp[Deg] WaterAuxTemp[Deg]\r\n";

    private static string Line(double t, double v, double brake, double current, string command = "none",
                               int security = 0) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{t:0.0000} {v:0.0000} 0.0000 0.0000 0.0000 0.0000 0.0000 0.0000 {brake:0.0000} 0.5000 {current:0.0000} ") +
        $"3 0 0.0000 0.0000 1 {command} 0.0000 0.0000 {security} 0 0.0000 0.0000 0.0000 0.0000\r\n";

    [Fact]
    public void A_physics_log_reads_as_speed_distance_and_the_two_hasler_marks()
    {
        var tape = HaslerTape.Parse(Header
                                    + Line(10, 0, 0.45, 0)
                                    + Line(20, 36, 0, 400)
                                    + Line(30, 72, 0, 0, security: 1));

        Assert.Equal(3, tape.Samples.Count);
        Assert.Equal(72, tape.MaxSpeedKmh, 6);
        Assert.Equal(20, tape.Duration, 6);
        Assert.Equal(50 + 150, tape.DistanceM, 6); // trapezoids: 0->36 km/h and 36->72 km/h over 10 s each
        Assert.True(tape.Samples[0].Braking);
        Assert.True(tape.Samples[1].Current);
        Assert.True(tape.Samples[2].SecurityAlarm);
    }

    [Fact]
    public void Standing_and_running_time_split_the_recording_like_the_old_analyser()
    {
        var tape = HaslerTape.Parse(Header + Line(0, 0, 0.45, 0) + Line(60, 0, 0.45, 0) + Line(120, 36, 0, 400)
                                    + Line(180, 36, 0, 0));

        Assert.Equal(60, tape.StandingTime, 6);
        Assert.Equal(120, tape.RunningTime, 6);
        Assert.Equal(900 / 180.0 * 3.6, tape.AverageWithStopsKmh, 6); // 300 m ramping up to 36 km/h, 600 m at it
        Assert.Equal(900 / 120.0 * 3.6, tape.AverageSpeedKmh, 6);
    }

    [Fact]
    public void A_line_from_the_simulator_reads_in_kmh_and_bar_whatever_its_header_says()
    {
        var tape = HaslerTape.Parse(Header +
            "107.5512 38.6099 -0.0201 -0.0002 917.2355 -0.0000 436.4420 0.0000 0.0021 4.9981 0.0000 0 0 0.0000 " +
            "0.0000 1 none 0.0000 0.0000 0 0 50.0000 45.0000 50.0000 40.0000\r\n");

        var s = Assert.Single(tape.Samples);
        Assert.Equal(38.6099, s.SpeedKmh, 6);
        Assert.Equal(4.9981, s.PipePressBar, 6);
        Assert.False(s.Braking);
    }

    [Fact]
    public void A_line_still_being_written_or_a_command_with_spaces_does_not_break_the_tape()
    {
        var tape = HaslerTape.Parse(Header + Line(0, 18, 0, 0, command: "Set Velocity") + Line(1, 18, 0, 0) + "2.0500 5.0");

        Assert.Equal(2, tape.Samples.Count);
        Assert.Equal(5, tape.DistanceM, 6);
    }

    [Fact]
    public void The_sample_under_a_point_of_the_tape_is_found_by_time_and_by_distance()
    {
        var tape = HaslerTape.Parse(Header + Line(0, 36, 0, 0) + Line(1, 36, 0, 0) + Line(2, 36, 0, 0));

        Assert.Equal(1, tape.IndexAt(1.5, byDistance: false));
        Assert.Equal(2, tape.IndexAt(25, byDistance: true));
        Assert.Equal(0, tape.IndexAt(-3, byDistance: false));
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 90)]
    [InlineData(90.5, 100)]
    [InlineData(120, 120)]
    [InlineData(151, 180)]
    [InlineData(180, 180)]
    [InlineData(181, 200)]
    [InlineData(205, 220)]
    public void The_scale_tops_out_at_the_speedometers_range_then_at_the_next_twenty(double max, double top) =>
        Assert.Equal(top, TapeFigures.TopSpeed(max));

    [Theory]
    [InlineData(1, 10)]
    [InlineData(0.8, 10)]
    [InlineData(0.79, 20)]
    [InlineData(0.4, 20)]
    [InlineData(0.16, 50)]
    [InlineData(0.08, 100)]
    [InlineData(0.04, 200)]
    [InlineData(0.039, 500)]
    public void Speed_lines_take_the_first_step_at_least_8_pixels_apart(double pxPerKmh, int step) =>
        Assert.Equal(step, TapeFigures.SpeedStep(pxPerKmh));

    [Theory]
    [InlineData(60, 1)]
    [InlineData(59, 2)]
    [InlineData(30, 2)]
    [InlineData(12, 5)]
    [InlineData(6, 10)]
    [InlineData(4, 15)]
    [InlineData(2, 30)]
    [InlineData(1, 60)]
    [InlineData(0.99, 120)]
    public void Clock_lines_take_the_first_step_at_least_60_pixels_apart(double pxPerMinute, double step) =>
        Assert.Equal(step, TapeFigures.MinuteStep(pxPerMinute));

    [Theory]
    [InlineData(600, 0.1)]
    [InlineData(500, 0.1)]
    [InlineData(499, 0.2)]
    [InlineData(100, 0.5)]
    [InlineData(50, 1)]
    [InlineData(25, 2)]
    [InlineData(10, 5)]
    [InlineData(5, 10)]
    [InlineData(2.5, 20)]
    [InlineData(2, 50)]
    public void Distance_marks_take_the_first_step_at_least_50_pixels_apart(double pxPerKm, double step) =>
        Assert.Equal(step, TapeFigures.KmStep(pxPerKm));

    [Theory]
    [InlineData(-5, "0:00")]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    [InlineData(90000, "25:00:00")]
    public void A_length_of_time_prints_hours_only_when_it_has_them(double seconds, string text) =>
        Assert.Equal(text, TapeFigures.Clock(seconds));

    [Theory]
    [InlineData("7:52", 7, 52, 0)]
    [InlineData("07:52", 7, 52, 0)]
    [InlineData(" 23:05 ", 23, 5, 0)]
    [InlineData("7:52:30", 7, 52, 30)]
    [InlineData("24:00", 0, 0, 0)]
    [InlineData("noon", 0, 0, 0)]
    [InlineData("", 0, 0, 0)]
    [InlineData(null, 0, 0, 0)]
    public void A_clock_typed_in_is_a_time_of_the_day_or_nothing(string? text, int hours, int minutes, int seconds) =>
        Assert.Equal(new TimeSpan(hours, minutes, seconds), TapeFigures.ParseClock(text));

    [Fact]
    public void Tapes_are_listed_from_physicslog_newest_first()
    {
        var installation = new TestInstallation(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("physicslog", "EU07-424.dat"), Header + Line(0, 1, 0, 0)));
        var tapes = installation.Tapes;

        var list = tapes.List();

        Assert.Single(list);
        Assert.Equal("EU07-424", list[0].Vehicle);
        Assert.Single(tapes.Load(list[0])!.Samples);
    }
}
