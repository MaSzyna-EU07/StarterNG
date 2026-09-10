using StarterNG.Classes;

namespace StarterNG.Tests;

public class TrainsetReadyTests
{
    private static Trainset Parse(string velocity) =>
        new($"trainset pociag tor_1 10 {velocity}\n" +
            "node -1 -1 ep07-001 dynamic PKP/EP07 ep07 ep07.mmd 0 headdriver 0\n" +
            "endtrainset");

    [Fact]
    public void A_consist_authored_standing_still_is_not_ready()
    {
        Assert.False(Parse("0").ReadyToGo);
    }

    [Fact]
    public void Marking_a_standing_consist_ready_gives_it_the_prepared_creep()
    {
        var trainset = Parse("0");

        trainset.ReadyToGo = true;

        Assert.True(trainset.ReadyToGo);
        Assert.Equal(0.1f, trainset.Velocity, 3);
    }

    [Fact]
    public void A_consist_authored_on_the_move_is_ready_and_keeps_its_speed()
    {
        var trainset = Parse("40");

        Assert.True(trainset.ReadyToGo);

        trainset.ReadyToGo = false;
        Assert.Equal(0f, trainset.Velocity);

        trainset.ReadyToGo = true;
        Assert.Equal(40f, trainset.Velocity);
    }

    [Fact]
    public void A_consist_running_the_other_way_keeps_its_direction()
    {
        // The sign of the speed is what DynObj.cpp turns into the running
        // direction, so restoring readiness must not flip it.
        var trainset = Parse("-40");

        trainset.ReadyToGo = false;
        trainset.ReadyToGo = true;

        Assert.Equal(-40f, trainset.Velocity);
        Assert.True(trainset.ReadyToGo);
    }
}
