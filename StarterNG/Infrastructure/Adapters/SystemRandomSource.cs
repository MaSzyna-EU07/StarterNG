using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary><see cref="IRandomSource"/> backed by <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);

    public double NextDouble() => Random.Shared.NextDouble();
}
