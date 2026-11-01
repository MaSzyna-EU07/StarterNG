using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Sceneries;

public static class SceneryWeatherWriter
{
    // Whole words, as the simulator splits them: "atmo/lasy.scm" is a path, no atmo.
    private const string WordStart = @"(?<![^\s;])";
    private const string WordEnd = @"(?![^\s;])";

    private static readonly Regex ConfigBlock = new(
        $@"(?is){WordStart}config{WordEnd}(.*?){WordStart}endconfig{WordEnd}");

    private static readonly Regex ManagedSetting = new(
        $@"(?i){WordStart}(movelight|scenario\.weather\.temperature|scenario\.time\.override)\s+\S+");

    /// <summary>What the written block takes the place of - looked for in the code alone, never in a comment.</summary>
    private static readonly Regex[] Replaced =
    {
        ConfigBlock,
        new($@"(?is){WordStart}time\s+\d[^\r\n]*?{WordStart}endtime{WordEnd}"),
        new($@"(?is){WordStart}atmo{WordEnd}.*?{WordStart}endatmo{WordEnd}"),
        new(@"(?im)^[ \t]*movelight\s+\S+"),
        new($@"(?i){WordStart}scenario\.weather\.temperature\s+\S+"),
        new($@"(?i){WordStart}scenario\.time\.override\s+\S+")
    };

    public static string Rewrite(string text, SceneryWeather weather, IRandomSource random)
    {
        try
        {
            string carried = CarriedConfigLines(text);

            string stripped = text;
            foreach (var pattern in Replaced)
                stripped = Cut(stripped, pattern);

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

    /// <summary>
    /// The text without what the pattern finds in its code. A comment inside what
    /// is taken out stays where it is, line ends and all, for a <c>//</c> to end
    /// where it did.
    /// </summary>
    private static string Cut(string text, Regex pattern)
    {
        string code = SceneryCode.Mask(text, out _);
        var sb = new StringBuilder(text.Length);
        int at = 0;

        foreach (Match found in pattern.Matches(code))
        {
            sb.Append(text, at, found.Index - at);
            string written = text.Substring(found.Index, found.Length);
            if (written == found.Value)
            {
                sb.Append(' ');
            }
            else
            {
                var comments = new StringBuilder(written);
                foreach (var word in Enumerable.Reverse(SceneryCode.Tokens(found.Value)))
                    comments.Remove(word.Index, word.Text.Length);
                sb.Append(comments);
            }
            at = found.Index + found.Length;
        }

        return sb.Append(text, at, text.Length - at).ToString();
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

    /// <summary>The settings of every config block but those the starter writes itself.</summary>
    private static string CarriedConfigLines(string text)
    {
        var sb = new StringBuilder();
        foreach (Match block in ConfigBlock.Matches(SceneryCode.Mask(text, out _)))
            foreach (string raw in ManagedSetting.Replace(block.Groups[1].Value, " ").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0)
                    sb.Append(line).Append("\r\n");
            }
        return sb.ToString();
    }
}
