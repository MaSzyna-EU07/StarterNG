using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Sceneries;

public static class SceneryWeatherWriter
{
    private static readonly Regex ManagedConfigLine = new(
        @"^\s*(movelight|scenario\.weather\.temperature|scenario\.time\.override)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Rewrite(string text, SceneryWeather weather, IRandomSource random)
    {
        try
        {
            string carried = CarriedConfigLines(text);

            string stripped = text;
            stripped = Regex.Replace(stripped, @"(?is)\bconfig\b.*?\bendconfig\b", " ");
            stripped = Regex.Replace(stripped, @"(?is)\btime\b\s+\d[^\r\n]*?\bendtime\b", " ");
            stripped = Regex.Replace(stripped, @"(?is)\batmo\b.*?\bendatmo\b", " ");
            stripped = Regex.Replace(stripped, @"(?im)^[ \t]*movelight\s+\S+", " ");
            stripped = Regex.Replace(stripped, @"(?i)scenario\.weather\.temperature\s+\S+", " ");
            stripped = Regex.Replace(stripped, @"(?i)scenario\.time\.override\s+\S+", " ");

            var inv = CultureInfo.InvariantCulture;
            string config =
                "config\r\n" +
                $"movelight {weather.Day}\r\n" +
                $"scenario.weather.temperature {weather.Temperature.ToString(inv)}\r\n" +
                $"scenario.time.override {DelphiHourMinute(weather.ScenarioTimeOverride)}\r\n" +
                carried +
                "endconfig\r\n" +
                $"atmo 0 0 0 {weather.FogEnd} {weather.FogEnd} 0 0 0 " +
                $"{weather.EffectiveOvercast(random).ToString(inv)} endatmo\r\n" +
                $"time {DelphiHourMinute(weather.Time)} 0 0 endtime\r\n";

            return config + stripped;
        }
        catch
        {
            return text;
        }
    }

    private static string DelphiHourMinute(string hhmm)
    {
        int colon = hhmm.IndexOf(':');
        if (colon <= 0)
            return hhmm;

        return hhmm[..colon].TrimStart('0') is { Length: > 0 } hour
            ? hour + hhmm[colon..]
            : "0" + hhmm[colon..];
    }

    private static string CarriedConfigLines(string text)
    {
        var block = Regex.Match(text, @"(?is)\bconfig\b(.*?)\bendconfig\b");
        if (!block.Success)
            return "";

        var sb = new StringBuilder();
        foreach (string raw in block.Groups[1].Value.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || ManagedConfigLine.IsMatch(line))
                continue;
            sb.Append(line).Append("\r\n");
        }
        return sb.ToString();
    }
}
