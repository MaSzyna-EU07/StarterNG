using System;
using System.Collections.Generic;
using StarterNG.Classes;

namespace StarterNG.Domain.Vehicles;

public sealed class LoadWeightsTable
{
    public const int DefaultWeight = 1000;

    private readonly Dictionary<string, int> _weights;

    public LoadWeightsTable(IReadOnlyDictionary<string, int>? weights = null)
    {
        _weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Dynamic.PantState] = 0
        };

        if (weights is null)
            return;

        foreach (var (name, weight) in weights)
            _weights[name] = weight;
    }

    public int WeightOf(string? name) =>
        !string.IsNullOrEmpty(name) && _weights.TryGetValue(name, out int weight) ? weight : DefaultWeight;
}
