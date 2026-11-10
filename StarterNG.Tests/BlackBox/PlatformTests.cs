using System.IO;
using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

/// <summary>The second value of a stop point as the driver reads it: all of it the metres, the last digit also the side.</summary>
public class PlatformTests
{
    [Theory]
    [InlineData(201f, 201, 1)]
    [InlineData(242f, 242, 2)]
    [InlineData(553f, 553, 3)]
    [InlineData(150f, 150, 0)]
    [InlineData(-151f, 151, 1)]
    public void The_whole_value_is_metres_and_the_last_digit_the_side(float value, double length, int side)
    {
        Assert.Equal(length, Platforms.Length(value));
        Assert.Equal(side, Platforms.Side(value));
    }

    private static StationEvent Stop(float distance, float length, float platform) =>
        new(0, 1, StationKind.Stopped, 1, "Swoszowice", new Position(0, 0, 0), distance, 63.2f, length, platform, 600, 601);

    [Fact]
    public void A_train_whose_tail_reaches_past_the_platform_of_201_is_off_it()
    {
        // Two EN57 at Swoszowice: the head 74.7 m before the W4, the tail 204.5 m, the platform 201 m.
        var stop = Stop(74.7f, 129.8f, 201);
        Assert.False(stop.OnPlatform);
        Assert.Equal(3.5, stop.OffPlatform, 1);
        Assert.Equal(201, stop.PlatformLength);
    }

    [Fact]
    public void A_train_within_the_platform_is_along_it()
    {
        var stop = Stop(63.2f, 129.8f, 201);
        Assert.True(stop.OnPlatform);
        Assert.Equal(0, stop.OffPlatform);
    }
}

/// <summary>The way a stop point faces and the W32, as the simulator writes them.</summary>
public class StopPointRecordTests
{
    [Fact]
    public void A_stop_point_tells_the_way_of_the_trains_it_is_for()
    {
        var recording = BlackBoxReader.Read(new MemoryStream(new BlackBoxFile().Info().StopPoint(0, "Swoszowice", 100, 201, 201, 1.25f).End(10).Bytes));
        var point = Assert.Single(recording.StopPoints);
        Assert.True(point.HasHeading);
        Assert.Equal(1.25f, point.Heading);
    }

    [Fact]
    public void A_stop_point_of_an_older_recording_has_no_way()
    {
        var recording = BlackBoxReader.Read(new MemoryStream(new BlackBoxFile().Info().StopPoint(0, "Swoszowice", 100, 201, 201).End(10).Bytes));
        Assert.False(Assert.Single(recording.StopPoints).HasHeading);
    }

    [Fact]
    public void A_stop_point_on_tracks_of_both_ways_has_no_way()
    {
        var recording = BlackBoxReader.Read(new MemoryStream(new BlackBoxFile().Info().StopPoint(0, "Swoszowice", 100, 201, 201, float.NaN).End(10).Bytes));
        Assert.False(Assert.Single(recording.StopPoints).HasHeading);
    }

    [Fact]
    public void A_W32_is_for_the_trains_shorter_than_its_length_and_that_is_their_room()
    {
        var recording = BlackBoxReader.Read(new MemoryStream(new BlackBoxFile().Info().StopPoint(0, "Tumlin_po", 100, -1, -121).End(10).Bytes));
        var point = Assert.Single(recording.StopPoints);
        Assert.True(point.ForShortTrains);
        Assert.False(point.HasPlatform);
        Assert.Equal(121, point.PlatformLength);
        var stop = new StationEvent(0, 1, StationKind.Stopped, 1, "Tumlin_po", new Position(0, 0, 0), 60, 50, 64.9f, -121, 600, 601);
        Assert.False(stop.OnPlatform);
        Assert.Equal(3.9, stop.OffPlatform, 1);
    }
}
