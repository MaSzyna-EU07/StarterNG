namespace StarterNG.Application.Abstractions;

/// <summary>
/// Port over randomness. Scenery weather deliberately varies between runs (fog
/// distance, overcast presets, texture randomiser); routing it through a port
/// keeps those rules assertable instead of flaky.
/// </summary>
public interface IRandomSource
{
    /// <summary>Inclusive lower bound, exclusive upper bound.</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Uniform value in [0, 1).</summary>
    double NextDouble();
}
