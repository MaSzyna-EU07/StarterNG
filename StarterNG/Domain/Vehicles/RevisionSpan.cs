using System;
using System.Globalization;

namespace StarterNG.Domain.Vehicles;

/// <summary>
/// A revision date from a livery's credit line, as far as it is known: 14.01.2005,
/// xx.06.2008, xx.xx.2011. A span, so a month or a year is not taken for its first day.
/// </summary>
public readonly record struct RevisionSpan(DateTime From, DateTime To)
{
    /// <summary>Also reads what is typed to filter by: 2016, 2016-12, 2016-12-06, 12.2016.</summary>
    public static RevisionSpan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string trimmed = text.Trim();
        string[] parts = trimmed.Split('.', '-');
        bool yearFirst = trimmed.Contains('-');
        return parts.Length switch
        {
            1 => Of(parts[0], null, null),
            2 => yearFirst ? Of(parts[0], parts[1], null) : Of(parts[1], parts[0], null),
            3 => yearFirst ? Of(parts[0], parts[1], parts[2]) : Of(parts[2], parts[1], parts[0]),
            _ => null
        };
    }

    private static RevisionSpan? Of(string year, string? month, string? day)
    {
        if (year.Length != 4 || !Number(year, out int y) || y is < 1800 or > 2200)
            return null;
        if (!Number(month, out int m))
            return new RevisionSpan(new DateTime(y, 1, 1), new DateTime(y, 12, 31));
        if (m is < 1 or > 12)
            return null;

        int days = DateTime.DaysInMonth(y, m);
        if (!Number(day, out int d))
            return new RevisionSpan(new DateTime(y, m, 1), new DateTime(y, m, days));
        if (d < 1 || d > days)
            return null;

        return new RevisionSpan(new DateTime(y, m, d), new DateTime(y, m, d));
    }

    private static bool Number(string? text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
