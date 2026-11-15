using System;
using System.Collections.Generic;
using System.Linq;

namespace StarterNG.Views;

/// <summary>
/// What a change to the consist is said as, to someone who cannot see the strip: a
/// vehicle added from the list, removed with Delete, turned, moved, or a whole undo,
/// each of which changed only pictures before. Worked out from the consist as it was
/// and as it is, so every way of making the change - a button, a key, the menu, a
/// drag - is told the same way.
/// </summary>
public static class ConsistNarration
{
    /// <summary>One position of the consist as it is told: what stands there, and whether it is turned.</summary>
    public readonly record struct Unit(string Label, bool Turned);

    /// <summary>
    /// The sentence for the change from <paramref name="before"/> to <paramref name="after"/>;
    /// null when nothing a listener would notice changed, such as a coupling or the crew.
    /// </summary>
    /// <param name="vehicles">How many vehicles the consist has now, its units counted car by car.</param>
    /// <param name="loc">The starter's texts by key.</param>
    public static string? Describe(IReadOnlyList<Unit> before, IReadOnlyList<Unit> after, int vehicles,
                                   Func<string, string> loc)
    {
        if (after.Count == 0)
            return before.Count == 0 ? null : loc("ConsistSaidEmpty");

        int head = 0;
        while (head < before.Count && head < after.Count && before[head] == after[head])
            head++;
        int tail = 0;
        while (tail < before.Count - head && tail < after.Count - head
               && before[before.Count - 1 - tail] == after[after.Count - 1 - tail])
            tail++;

        var gone = before.Skip(head).Take(before.Count - head - tail).ToList();
        var come = after.Skip(head).Take(after.Count - head - tail).ToList();

        if (gone.Count == 0 && come.Count == 0)
            return null;

        if (gone.Count == 0)
            return string.Format(loc("ConsistSaidAdded"), Labels(come), head + 1, after.Count);

        if (come.Count == 0)
            return string.Format(loc("ConsistSaidRemoved"), Labels(gone), vehicles);

        if (gone.Count == come.Count)
        {
            // The same vehicles in the same places: some of them turned round.
            if (gone.Select(u => u.Label).SequenceEqual(come.Select(u => u.Label)))
            {
                var turned = come.Where((u, i) => u.Turned != gone[i].Turned).ToList();
                return turned.Count == 1
                    ? string.Format(loc(turned[0].Turned ? "ConsistSaidTurned" : "ConsistSaidUnturned"), turned[0].Label)
                    : string.Format(loc("ConsistSaidTurnedMany"), turned.Count);
            }

            // One vehicle taken out and put back elsewhere: the rest keep their order.
            if (MovedOne(gone, come) is { } moved)
                return string.Format(loc("ConsistSaidMoved"), moved.Unit.Label, head + moved.To + 1, after.Count);

            if (gone.OrderBy(u => u.Label, StringComparer.Ordinal).SequenceEqual(
                    come.OrderBy(u => u.Label, StringComparer.Ordinal)))
                return loc("ConsistSaidReordered");

            if (gone.Count == 1)
                return string.Format(loc("ConsistSaidReplaced"), gone[0].Label, come[0].Label);
        }

        return string.Format(loc("ConsistSaidChangedMany"), Math.Max(gone.Count, come.Count), vehicles);
    }

    /// <summary>The unit and the place it went to when one alone moved, or null.</summary>
    private static (Unit Unit, int To)? MovedOne(IReadOnlyList<Unit> gone, IReadOnlyList<Unit> come)
    {
        // The run that differs starts and ends with a change, so the mover is at one end of it.
        if (come.Count >= 2 && come.Take(come.Count - 1).SequenceEqual(gone.Skip(1)) && come[^1] == gone[0])
            return (come[^1], come.Count - 1);
        if (come.Count >= 2 && come.Skip(1).SequenceEqual(gone.Take(gone.Count - 1)) && come[0] == gone[^1])
            return (come[0], 0);
        return null;
    }

    private static string Labels(IEnumerable<Unit> units) => string.Join(", ", units.Select(u => u.Label));
}
