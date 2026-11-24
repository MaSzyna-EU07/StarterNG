using System;
using System.Collections.Generic;
using Avalonia;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The marks of a map thinned out on the screen: one to a square of a few pixels, the first
/// to ask for a square keeps it. Asked in the order of how much a mark tells, a crowd seen from
/// afar shows its most telling marks, each clear of the next, and close up, where the marks
/// stand apart anyway, every one is drawn.
/// </summary>
internal sealed class MarkGrid
{
    private readonly double _cell;
    private readonly HashSet<(long, long)> _taken = new();

    /// <param name="cell">The side of a square in pixels: about the size of the mark.</param>
    public MarkGrid(double cell) => _cell = Math.Max(cell, 1);

    /// <summary>Whether the mark at this point of the screen is drawn: its square was free.</summary>
    public bool Take(Point p) =>
        _taken.Add(((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell)));

    /// <summary>
    /// The items whose marks are drawn: those in view, the most telling first - a lower rank
    /// tells more - each in a square no other took before it.
    /// </summary>
    public static List<(T Item, Point At)> Thin<T>(IEnumerable<(T Item, Point At, int Rank)> marks, double cell)
    {
        var ranked = new List<(T Item, Point At, int Rank)>(marks);
        // Stable: of marks alike, the one first in the recording keeps its square, frame after frame.
        var order = new int[ranked.Count];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => ranked[a].Rank != ranked[b].Rank ? ranked[a].Rank.CompareTo(ranked[b].Rank) : a.CompareTo(b));

        var grid = new MarkGrid(cell);
        var kept = new List<(T, Point)>();
        foreach (int i in order)
            if (grid.Take(ranked[i].At))
                kept.Add((ranked[i].Item, ranked[i].At));
        // Drawn the least telling first: where marks of next squares touch, the telling one lies on top.
        kept.Reverse();
        return kept;
    }
}
