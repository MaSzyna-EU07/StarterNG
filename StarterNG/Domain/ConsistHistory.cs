using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using StarterNG.Classes;

namespace StarterNG.Domain;

/// <summary>
/// Undo and redo for the consist editor, one history per trainset. It is fed the
/// vehicles as written back after every redraw; a state equal to the last one -
/// a redraw for a new selection - changes nothing, so feeding it is cheap to do
/// everywhere instead of remembering which of the many edits went through it.
/// </summary>
public sealed class ConsistHistory
{
    private const int Depth = 100;

    public sealed record State(IReadOnlyList<Dynamic> Vehicles, string Signature, int Selected);

    private sealed class Track
    {
        public State? Last;
        public readonly List<State> Undo = new();
        public readonly Stack<State> Redo = new();
    }

    private readonly ConditionalWeakTable<Trainset, Track> _tracks = new();

    /// <summary>
    /// Notes what the trainset holds now. With <paramref name="rebase"/> it only
    /// becomes the state undo returns to - for the redraw after an undo itself.
    /// </summary>
    public void Record(Trainset trainset, int selected, bool rebase = false)
    {
        var track = _tracks.GetOrCreateValue(trainset);
        string signature = Dynamic.ToTrainsetNodes(trainset.Vehicles);

        if (track.Last is { } last && last.Signature == signature)
        {
            track.Last = last with { Selected = selected };
            return;
        }

        var now = new State(trainset.Vehicles.Select(v => v.Clone()).ToList(), signature, selected);
        if (track.Last is { } previous && !rebase)
        {
            // Undone, the selection goes back to where the change was made.
            track.Undo.Add(previous with { Selected = selected });
            if (track.Undo.Count > Depth)
                track.Undo.RemoveAt(0);
            track.Redo.Clear();
        }
        track.Last = now;
    }

    public bool CanUndo(Trainset trainset) =>
        _tracks.TryGetValue(trainset, out var track) && track.Undo.Count > 0;

    public bool CanRedo(Trainset trainset) =>
        _tracks.TryGetValue(trainset, out var track) && track.Redo.Count > 0;

    /// <summary>The state before the last change, as fresh copies to put into the trainset.</summary>
    public State? Undo(Trainset trainset, int selected)
    {
        // An edit that never asked for a redraw is still in the trainset; noting it
        // first makes it the change that gets undone instead of being lost.
        Record(trainset, selected);
        var track = _tracks.GetOrCreateValue(trainset);
        if (track.Undo.Count == 0 || track.Last is not { } last)
            return null;

        track.Redo.Push(last);
        track.Last = track.Undo[^1];
        track.Undo.RemoveAt(track.Undo.Count - 1);
        return Copy(track.Last);
    }

    public State? Redo(Trainset trainset, int selected)
    {
        Record(trainset, selected);
        var track = _tracks.GetOrCreateValue(trainset);
        if (track.Redo.Count == 0 || track.Last is not { } last)
            return null;

        track.Undo.Add(last);
        track.Last = track.Redo.Pop();
        return Copy(track.Last);
    }

    private static State Copy(State state) =>
        state with { Vehicles = state.Vehicles.Select(v => v.Clone()).ToList() };
}
