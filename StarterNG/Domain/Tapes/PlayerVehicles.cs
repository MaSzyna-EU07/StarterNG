using System;
using System.Collections.Generic;
using System.Linq;

namespace StarterNG.Domain.Tapes;

/// <summary>The vehicles the player's commands went to during a run, the one used last at the end.</summary>
public sealed class PlayerVehicles
{
    private const string CommandMark = " received command: [";

    /// <summary>No log of the run to go by, so a change of vehicle cannot be seen.</summary>
    public static readonly PlayerVehicles Unknown = new(Array.Empty<string>(), known: false);

    /// <summary>A log with no command in it yet.</summary>
    public static readonly PlayerVehicles None = new(Array.Empty<string>(), known: true);

    private readonly IReadOnlyList<string> _vehicles;

    private PlayerVehicles(IReadOnlyList<string> vehicles, bool known)
    {
        _vehicles = vehicles;
        Known = known;
    }

    public bool Known { get; }

    public IReadOnlyList<string> Names => _vehicles;

    public string? Current => _vehicles.Count > 0 ? _vehicles[^1] : null;

    public static PlayerVehicles Of(IEnumerable<string> names) => new(names.ToList(), known: true);

    public bool Used(string vehicle) => _vehicles.Contains(vehicle, StringComparer.OrdinalIgnoreCase);

    /// <summary>What a further part of the simulator's log, in whole lines, adds to this.</summary>
    public PlayerVehicles After(string log)
    {
        var vehicles = _vehicles.ToList();
        foreach (string line in log.Split('\n'))
        {
            int mark = line.IndexOf(CommandMark, StringComparison.Ordinal);
            if (mark < 0)
                continue;

            int stamp = line.IndexOf(']');
            string vehicle = line[(stamp < mark ? stamp + 1 : 0)..mark].Trim();
            if (vehicle.Length == 0 || string.Equals(vehicles.LastOrDefault(), vehicle, StringComparison.OrdinalIgnoreCase))
                continue;

            vehicles.RemoveAll(v => string.Equals(v, vehicle, StringComparison.OrdinalIgnoreCase));
            vehicles.Add(vehicle);
        }
        return new PlayerVehicles(vehicles, known: true);
    }
}
