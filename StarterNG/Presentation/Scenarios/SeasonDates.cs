using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Presentation.Scenarios;

/// <summary>
/// Translates between the scenery's day-of-year (movelight) and the date the
/// weather tab shows.
/// </summary>
/// <remarks>
/// Day 0 is the scenery saying "whatever today is", which the tab presents as
/// the current date; the season presets are ordinary days of the year. Leap
/// years are honoured so 366 stays selectable in one.
/// </remarks>
public sealed class SeasonDates
{
    private readonly IClock _clock;

    public SeasonDates(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>The day-of-year a scenery means by 0: today.</summary>
    public int Today => _clock.Now.DayOfYear;

    /// <summary>
    /// The date to show for a stored day-of-year, in the current year. Day 0 and
    /// out-of-range values fall back to today, clamped to the year's length.
    /// </summary>
    public DateTimeOffset DateOf(int dayOfYear)
    {
        int year = _clock.Now.Year;
        int lastDay = DateTime.IsLeapYear(year) ? 366 : 365;
        int day = Math.Clamp(dayOfYear <= 0 ? Today : dayOfYear, 1, lastDay);

        return new DateTimeOffset(new DateTime(year, 1, 1).AddDays(day - 1));
    }
}
