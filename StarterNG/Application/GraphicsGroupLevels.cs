using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Domain.Settings;

namespace StarterNG.Application;

/// <summary>Levels of detail part by part: what a level holds for one <see cref="GraphicsGroup"/>, the rest as it is.</summary>
public sealed class GraphicsGroupLevels
{
    private readonly SettingsStore _store;

    public GraphicsGroupLevels(SettingsStore store) => _store = store;

    /// <summary>
    /// The level each group is at in <paramref name="settings"/>, null where it is at none. At
    /// Ultra the level under it fits too, as it leaves alone what only Ultra sets: the highest counts.
    /// </summary>
    public IReadOnlyDictionary<GraphicsGroup, GraphicsPreset?> LevelsOf(SimulatorSettings settings)
    {
        var now = _store.Render(settings);
        var rendered = GraphicsPreset.All.Select(level => (Level: level, Keys: RenderAt(level, settings))).ToList();
        return GraphicsGroup.All.ToDictionary(group => group, group =>
            (GraphicsPreset?)rendered.LastOrDefault(level => Same(group, level.Keys, now)).Level);
    }

    /// <summary>
    /// A copy of <paramref name="settings"/> with the group at <paramref name="level"/>. What only
    /// an end of the scale sets goes back to the simulator's own on leaving that end.
    /// </summary>
    public SimulatorSettings Apply(GraphicsGroup group, GraphicsPreset level, SimulatorSettings settings)
    {
        var current = LevelsOf(settings)[group];
        var source = settings.Copy();
        if (level.Extras is null && current?.Extras is not null)
            GraphicsExtras.None.ApplyTo(source);
        if (level.Top is null && current?.Top is not null)
            GraphicsTop.None.ApplyTo(source);
        level.ApplyTo(source);
        return _store.WithKeysOf(settings, source, group.Keys);
    }

    private IReadOnlyDictionary<string, string> RenderAt(GraphicsPreset level, SimulatorSettings settings)
    {
        var copy = settings.Copy();
        level.ApplyTo(copy);
        return _store.Render(copy);
    }

    private static bool Same(GraphicsGroup group, IReadOnlyDictionary<string, string> one, IReadOnlyDictionary<string, string> other) =>
        group.Keys.All(key => string.Equals(ValueIn(one, key), ValueIn(other, key), StringComparison.Ordinal));

    // A key the file does not set is at the simulator's own value.
    private static string? ValueIn(IReadOnlyDictionary<string, string> keys, string key) =>
        keys.TryGetValue(key, out string? value) ? value : SimulatorKeys.All.FirstOrDefault(known => known.Key == key)?.Default;
}
