using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Presentation.Scenarios;

public sealed class SeasonDates
{
    private readonly IClock _clock;

    public SeasonDates(IClock clock)
    {
        _clock = clock;
    }

    public int Today => _clock.Now.DayOfYear;

    public DateTimeOffset DateOf(int dayOfYear)
    {
        int year = _clock.Now.Year;
        int lastDay = DateTime.IsLeapYear(year) ? 366 : 365;
        int day = Math.Clamp(dayOfYear <= 0 ? Today : dayOfYear, 1, lastDay);

        return new DateTimeOffset(new DateTime(year, 1, 1).AddDays(day - 1));
    }
}
