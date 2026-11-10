using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

public class DrivingSummaryTests
{
    private static Recording Drive(Func<BlackBoxFile, BlackBoxFile> more)
    {
        var file = new BlackBoxFile().Info();
        return BlackBoxReader.Read(more(file).Stream());
    }

    /// <summary>Ten samples a second from t, at a speed against an AI holding target.</summary>
    private static BlackBoxFile Stretch(BlackBoxFile file, double from, double seconds, float speed, float target,
                                        byte driving = 1, byte flags = 0x03)
    {
        for (int i = 0; i < seconds * 10; i++)
            file.Cab(from + i * 0.1, 1, 1000 + (from + i * 0.1) * speed / 3.6, speed, driving, flags, target);
        return file;
    }

    [Theory]
    [InlineData(40f, 40f, SpeedJudgement.Within)]
    [InlineData(40.4f, 40f, SpeedJudgement.Within)]
    [InlineData(44f, 40f, SpeedJudgement.Slightly)]
    [InlineData(46f, 40f, SpeedJudgement.Over)]
    [InlineData(-46f, 40f, SpeedJudgement.Over)]
    public void A_sample_is_judged_by_how_much_faster_than_the_AI_it_went(float speed, float target, SpeedJudgement expected)
    {
        var r = Drive(f => f.Cab(0, 1, 0, speed, target: target));

        Assert.Equal(expected, DrivingSummary.Judge(r.Cab[0]));
    }

    [Fact]
    public void Without_the_AI_worked_out_or_with_the_AI_driving_there_is_nothing_to_judge()
    {
        var r = Drive(f => f.Cab(0, 1, 0, 80, flags: 0x01, target: 40).Cab(0.1, 1, 0, 80, driving: 2, target: 40));

        Assert.Equal(SpeedJudgement.NoData, DrivingSummary.Judge(r.Cab[0]));
        Assert.Equal(SpeedJudgement.Autopilot, DrivingSummary.Judge(r.Cab[1]));
    }

    [Fact]
    public void The_summary_counts_the_time_over_the_AI_the_autopilot_and_the_signals_passed_at_danger()
    {
        var r = Drive(f =>
        {
            Stretch(f, 0, 10, 40, 40);
            Stretch(f, 10, 10, 50, 40);
            Stretch(f, 20, 10, 43, 40);
            Stretch(f, 30, 10, 60, 60, driving: 2);
            f.Passed(15, 1, 0, "s1", 0, 50, 0x02);
            f.Passed(25, 1, 1, "s2", 40, 43);
            // Another train, on a track of its own far away.
            f.Passed(25, 7, 2, "someone else's", 0, 20, 0x02, z: 9000);
            f.Command(5, 1, true, "mastercontrollerincrease");
            f.Command(5.1, 1, false, "mastercontrollerincrease");
            f.Command(6, 0, true, "timejump");
            return f.Driver(30, 1, 2);
        });

        var summary = r.Summarize();

        Assert.Equal(20, summary.OverAiSeconds, 0);
        Assert.Equal(10, summary.WellOverAiSeconds, 0);
        Assert.Equal(10, summary.MaxOverAiKmh, 3);
        Assert.InRange(summary.MaxOverAiAt, 10, 20);
        Assert.Equal(10, summary.AutopilotSeconds, 0);
        Assert.Equal(30, summary.DrivenSeconds, 0);
        Assert.Equal(60, summary.MaxSpeedKmh);
        Assert.Equal(2, summary.SignalsPassed);
        Assert.Equal("s1", Assert.Single(summary.PassedAtDanger).Event);
        Assert.Equal(1, summary.Commands);
        Assert.Equal(1, summary.AutopilotSwitches);
        // 10 s at 40, 50, 43 and 60 km/h.
        Assert.InRange(summary.DistanceKm, 0.530, 0.540);
    }

    [Fact]
    public void The_autopilot_is_told_as_spans_of_time()
    {
        var r = Drive(f =>
        {
            Stretch(f, 0, 5, 40, 40);
            Stretch(f, 5, 5, 40, 40, driving: 2);
            Stretch(f, 10, 5, 40, 40);
            return Stretch(f, 15, 2, 40, 40, driving: 2);
        });

        var spans = r.AutopilotSpans();

        Assert.Equal(2, spans.Count);
        Assert.Equal(5, spans[0].Start, 3);
        Assert.Equal(10, spans[0].End, 3);
        Assert.Equal(15, spans[1].Start, 3);
    }
}
