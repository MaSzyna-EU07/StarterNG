using System;
using System.Collections.Generic;
using StarterNG.Classes;

namespace StarterNG.Application;

/// <summary>
/// The warehouse consist the strip edits while the warehouse tab is open, under
/// its name there, and the entry as last written - every change saves itself.
/// </summary>
public sealed class WarehouseEdit
{
    /// <summary>One trainset per warehouse consist, so its undo history outlives a switch.</summary>
    private readonly Dictionary<string, Trainset> _trainsets = new(StringComparer.OrdinalIgnoreCase);

    private string? _saved;

    public Trainset? Trainset { get; private set; }

    public string? Name { get; private set; }

    public Trainset Open(TrainsetPreset preset)
    {
        // Written over from elsewhere - "add to warehouse as" the same name - the
        // entry is read afresh; the history of what it was no longer applies.
        if (!_trainsets.TryGetValue(preset.Name, out var trainset) ||
            ConsistText.Serialize(trainset) != preset.Entry)
            _trainsets[preset.Name] = trainset = new Trainset(preset.Entry);

        Trainset = trainset;
        Name = preset.Name;
        _saved = preset.Entry;
        return trainset;
    }

    public void Close()
    {
        Trainset = null;
        Name = null;
    }

    public void Renamed(string old, string fresh)
    {
        if (_trainsets.Remove(old, out var trainset))
            _trainsets[fresh] = trainset;
        if (string.Equals(Name, old, StringComparison.OrdinalIgnoreCase))
            Name = fresh;
    }

    public void Save()
    {
        if (Trainset is null || Name is null)
            return;

        string entry = ConsistText.Serialize(Trainset);
        if (entry == _saved)
            return;

        PresetStore.Save(Name, entry);
        _saved = entry;
    }
}
