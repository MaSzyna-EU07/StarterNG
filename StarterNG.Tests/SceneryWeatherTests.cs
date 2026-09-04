using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryWeatherTests
{
    private static SceneryParser Parser(DateTime now) => new(new FixedClock(now), new StubRandom());

    [Fact]
    public void Authored_weather_is_read_from_the_config_block()
    {
        const string scn = """
            config
            movelight 172
            scenario.weather.temperature 21.5
            scenario.time.override 6:05
            endconfig
            atmo 0 0 0 50 900 0 0 0 0.7 endatmo
            time 8:30 0 0 endtime
            """;

        var weather = Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", scn).Weather;

        Assert.True(weather.IsAuthored);
        Assert.Equal(172, weather.Day);
        Assert.Equal(21.5, weather.Temperature);
        Assert.Equal("06:05", weather.ScenarioTimeOverride);
        Assert.Equal("08:30", weather.Time);
        Assert.Equal(50, weather.FogStart);
        Assert.Equal(0.7, weather.Overcast);
    }

    [Fact]
    public void A_scenery_without_a_time_starts_at_the_players_clock()
    {
        var weather = Parser(new DateTime(2026, 5, 4, 17, 42, 0)).Parse("scenery/x.scn", "//$n Test").Weather;

        Assert.Equal("17:42", weather.ScenarioTimeOverride);
        Assert.False(weather.IsAuthored);
    }

    [Fact]
    public void Freshly_parsed_weather_counts_as_unchanged()
    {
        var weather = Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", "movelight 100\n").Weather;

        Assert.False(weather.Changed);
    }

    [Fact]
    public void Editing_then_restoring_returns_to_the_authored_values()
    {
        var weather = Parser(new DateTime(2026, 1, 1)).Parse("scenery/x.scn", "movelight 100\n").Weather;

        weather.Day = 5;
        weather.Temperature = -12;
        Assert.True(weather.Changed);

        weather.Restore();

        Assert.False(weather.Changed);
        Assert.Equal(100, weather.Day);
        Assert.True(weather.Dirty);
    }

    [Fact]
    public void A_fog_range_is_clamped_into_the_supported_span()
    {
        var weather = new SceneryWeather();

        weather.SetFogRange(start: 1, end: 999999, random: new StubRandom());

        Assert.Equal(SceneryWeather.FogMin, weather.FogStart);
        Assert.InRange(weather.FogEnd, SceneryWeather.FogMin, SceneryWeather.FogMax);
    }

    [Fact]
    public void Random_overcast_draws_from_the_presets_rather_than_the_stored_value()
    {
        var weather = new SceneryWeather { Overcast = 0.42, OvercastRandom = true };

        Assert.Equal(-1.5, weather.EffectiveOvercast(new StubRandom()));
        Assert.Equal(0.42, new SceneryWeather { Overcast = 0.42 }.EffectiveOvercast(new StubRandom()));
    }
}
