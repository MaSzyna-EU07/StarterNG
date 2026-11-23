using System;
using System.Collections.Generic;
using StarterNG.Classes;

namespace StarterNG.Domain;

/// <summary>A stretch of a consist made of vehicles alike, shown as one picture with a count.</summary>
public sealed record ConsistRun(Dynamic First, int Count);

public static class ConsistRuns
{
    /// <summary>
    /// The consist's vehicles in order, those next to each other that look alike taken together.
    /// </summary>
    /// <param name="keyOf">
    /// What makes two vehicles alike, given the vehicle and its place in the consist.
    /// No key keeps the vehicle on its own.
    /// </param>
    public static List<ConsistRun> Of(IReadOnlyList<Dynamic> vehicles, Func<Dynamic, int, string?> keyOf)
    {
        var runs = new List<ConsistRun>();
        string? prevKey = null;

        for (int i = 0; i < vehicles.Count; i++)
        {
            string? key = keyOf(vehicles[i], i);
            if (key is not null && prevKey is not null && runs.Count > 0 &&
                string.Equals(key, prevKey, StringComparison.OrdinalIgnoreCase))
            {
                var last = runs[^1];
                runs[^1] = last with { Count = last.Count + 1 };
            }
            else
            {
                runs.Add(new ConsistRun(vehicles[i], 1));
            }
            prevKey = key;
        }

        return runs;
    }
}
