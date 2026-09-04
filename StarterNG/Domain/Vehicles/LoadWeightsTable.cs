using System;
using System.Collections.Generic;
using StarterNG.Classes;

namespace StarterNG.Domain.Vehicles;

/// <summary>
/// How heavy a tonne of each cargo type is, as declared by
/// data/load_weights.txt, used to turn a load into a mass.
/// </summary>
/// <remarks>
/// Was a static class that also read the file. The table is now a value the
/// repository builds; an unknown cargo weighs <see cref="DefaultWeight"/>, which
/// is what the simulator assumes.
/// </remarks>
public sealed class LoadWeightsTable
{
    /// <summary>Weight assumed for a cargo the file does not mention.</summary>
    public const int DefaultWeight = 1000;

    private readonly Dictionary<string, int> _weights;

    public LoadWeightsTable(IReadOnlyDictionary<string, int>? weights = null)
    {
        _weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // The pantograph state is carried as a pseudo load and weighs nothing.
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
