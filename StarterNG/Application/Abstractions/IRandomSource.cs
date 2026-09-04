namespace StarterNG.Application.Abstractions;

public interface IRandomSource
{
    int Next(int minInclusive, int maxExclusive);

    double NextDouble();
}
