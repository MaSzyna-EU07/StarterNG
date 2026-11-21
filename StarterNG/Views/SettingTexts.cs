using System;
using System.Globalization;
using StarterNG.Domain.Settings;

namespace StarterNG.Views;

/// <summary>The captions of the settings' rows and the words for their values.</summary>
internal static class SettingTexts
{
    public static string[] QualityWords => new[]
    {
        App.Loc["QualityVeryLow"], App.Loc["QualityLow"], App.Loc["QualityNormal"],
        App.Loc["QualityHigh"], App.Loc["QualityVeryHigh"]
    };

    /// <summary>The words of a slider that goes from 1 to 4, which has no middle.</summary>
    public static string Fidelity(int level)
    {
        var q = QualityWords;
        string[] four = { q[0], q[1], q[3], q[4] };
        return four[Math.Clamp(level, 1, 4) - 1];
    }

    public static string Anisotropy(double value) => "x" + value.ToString("0.#", CultureInfo.CurrentCulture);

    public static string AntiAliasing(AntiAliasingMode mode) => mode switch
    {
        AntiAliasingMode.Msaa2 => "MSAA x2",
        AntiAliasingMode.Msaa4 => "MSAA x4",
        AntiAliasingMode.Msaa8 => "MSAA x8",
        _ => App.Loc["MsaaNone"]
    };

    public static string TwoDecimals(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>A rate below one a second reads better as the time between updates.</summary>
    public static string ReflectionsInterval(double perSecond)
    {
        if (perSecond >= 1)
            return string.Format(App.Loc["TimesPerSecond"], perSecond.ToString("0.##", CultureInfo.CurrentCulture));
        double seconds = 1 / perSecond;
        return seconds >= 60
            ? string.Format(App.Loc["EveryMinutes"], (seconds / 60).ToString("0.#", CultureInfo.CurrentCulture))
            : string.Format(App.Loc["EverySeconds"], seconds.ToString("0.#", CultureInfo.CurrentCulture));
    }
}
