using StarterNG.Domain.Settings;

namespace StarterNG.Tests;

public class StepsTests
{
    [Fact]
    public void A_value_among_the_steps_leaves_them_as_they_are()
    {
        Assert.Same(Steps.FrameRateLimits, Steps.With(Steps.FrameRateLimits, 144));
    }

    [Fact]
    public void A_value_between_the_steps_joins_them_in_order()
    {
        var steps = Steps.With(Steps.FrameRateLimits, 58);

        Assert.Equal(Steps.FrameRateLimits.Length + 1, steps.Count);
        Assert.Equal(new double[] { 50, 58, 60 }, steps.Skip(Steps.IndexOf(steps, 50)).Take(3));
    }

    [Fact]
    public void Values_past_either_end_join_at_that_end()
    {
        Assert.Equal(0.5, Steps.With(Steps.Anisotropy, 0.5)[0]);
        Assert.Equal(500, Steps.With(Steps.FrameRateLimits, 500)[^1]);
    }

    [Fact]
    public void The_frame_rate_limits_start_with_none()
    {
        Assert.Equal(new double[] { 0, 10, 20 }, Steps.With(Steps.FrameRateLimits, 10).Take(3));
    }

    [Fact]
    public void A_step_is_found_from_its_rounding_in_the_file()
    {
        // 1/300 is written as 0.003333.
        Assert.Equal(0, Steps.IndexOf(Steps.ReflectionUpdates, 0.003333));
        Assert.Equal(-1, Steps.IndexOf(Steps.ReflectionUpdates, 0.0033));
    }
}
