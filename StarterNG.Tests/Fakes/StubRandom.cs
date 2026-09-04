using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>
/// Randomness pinned to the low end of every range, so weather that varies in
/// production is deterministic in tests.
/// </summary>
public sealed class StubRandom : IRandomSource
{
    private readonly int _fixedValue;

    public StubRandom(int fixedValue = 0)
    {
        _fixedValue = fixedValue;
    }

    public int Next(int minInclusive, int maxExclusive) =>
        Math.Clamp(minInclusive + _fixedValue, minInclusive, Math.Max(minInclusive, maxExclusive - 1));

    public double NextDouble() => 0.0;
}
