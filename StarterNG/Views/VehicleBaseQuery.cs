using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Views;

public sealed record VehicleClassRow(string Name, int Count);

public sealed class VehicleBaseRow
{
    public VehicleTexture Texture { get; }
    public string Skin { get; }
    public string Class { get; }
    public string Operator { get; }
    public string Revision { get; }
    public string Author { get; }
    public string Station { get; }
    public string Photo { get; }
    public string Model { get; }
    public string? Category { get; }
    public RevisionSpan? Date { get; }

    /// <summary>Sorts as text in the order of the dates; empty for a date not known.</summary>
    public string DateKey => Date?.From.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";

    public VehicleBaseRow(VehicleTexture texture)
    {
        Texture = texture;
        Skin = Consist.Base(texture.Skinfile);
        Class = VehicleInfo.ClassOf(texture);
        Operator = Known(texture.Meta?.Operator);
        Revision = Known(texture.Meta?.RevisionDate);
        Author = Known(texture.Meta?.TextureAuthor);
        Station = Known(texture.Meta?.Depot);
        Photo = Known(texture.Meta?.PhotoAuthor);
        Model = texture.Directory + texture.Model;
        Category = VehicleInfo.CategoryOf(texture);
        Date = RevisionSpan.Parse(Revision);
    }

    // The credit lines write a question mark for what their author did not know.
    private static string Known(string? value) => value is null or "?" ? "" : value;
}

/// <summary>What is typed and picked to look through the vehicle base with.</summary>
public sealed record VehicleBaseFilters
{
    public string? Name { get; init; }
    public string? Class { get; init; }
    public string? Operator { get; init; }
    public string? Author { get; init; }
    public string? Model { get; init; }
    public string? Station { get; init; }
    public string? Photo { get; init; }
    public string? RevisionFrom { get; init; }
    public string? RevisionTo { get; init; }

    /// <summary>The class picked from the list, which has to match whole.</summary>
    public string? PickedClass { get; init; }
}

public static class VehicleBaseQuery
{
    private static readonly StringComparer Order = StringComparer.CurrentCultureIgnoreCase;

    public static List<VehicleClassRow> ClassCounts(IEnumerable<VehicleBaseRow> rows) => rows
        .Where(r => r.Class.Length > 0)
        .GroupBy(r => r.Class, StringComparer.OrdinalIgnoreCase)
        .Select(g => new VehicleClassRow(g.Key, g.Count()))
        .OrderBy(c => c.Name, Order)
        .ToList();

    public static List<VehicleBaseRow> Select(IEnumerable<VehicleBaseRow> rows, VehicleBaseFilters filters,
                                              Func<VehicleBaseRow, string> sortKey, bool descending)
    {
        rows = Filter(rows, filters.Name, r => r.Skin);
        rows = Filter(rows, filters.Class, r => r.Class);
        rows = Filter(rows, filters.Operator, r => r.Operator);
        rows = Filter(rows, filters.Author, r => r.Author);
        rows = Filter(rows, filters.Model, r => r.Model);
        rows = Filter(rows, filters.Station, r => r.Station);
        rows = Filter(rows, filters.Photo, r => r.Photo);

        if (filters.PickedClass is { } picked)
            rows = rows.Where(r => string.Equals(r.Class, picked, StringComparison.OrdinalIgnoreCase));

        if (RevisionSpan.Parse(filters.RevisionFrom) is { } from)
            rows = rows.Where(r => r.Date is { } date && date.To >= from.From);
        if (RevisionSpan.Parse(filters.RevisionTo) is { } to)
            rows = rows.Where(r => r.Date is { } date && date.From <= to.To);

        // Blanks stay at the bottom whichever way a column is sorted.
        var blanksLast = rows.OrderBy(r => sortKey(r).Length == 0);
        var sorted = descending ? blanksLast.ThenByDescending(sortKey, Order) : blanksLast.ThenBy(sortKey, Order);
        return sorted.ThenBy(r => r.Skin, Order).ToList();
    }

    private static IEnumerable<VehicleBaseRow> Filter(
        IEnumerable<VehicleBaseRow> rows, string? typed, Func<VehicleBaseRow, string> field)
    {
        string needle = typed?.Trim() ?? "";
        return needle.Length == 0
            ? rows
            : rows.Where(r => field(r).Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
