using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarterNG.Domain.Settings;

/// <summary>
/// weather-schema.txt, which the simulator's build writes next to the simulator: the physics.weather.* keys
/// of that build with their defaults, ranges and groups, out of the tables the simulator reads them with
/// (McZapkie/weatherschema.h in the simulator's repository describes the format). Each entry becomes a
/// <see cref="SimulatorKey"/>, so it gets a row of the settings like any other key, writes nothing until the
/// player moves it, and is clamped to the range of the build it will be started with.
/// <para>
/// The file is read as what it is, text from the disk of the player: one that is too long, lines too long,
/// an entry that makes no sense, a key outside physics.weather, a number that is not one, a range turned
/// upside down are left out, and the rest is used. A schema number this starter does not know leaves the
/// whole file out, and the keys are then not shown (a simulator without the file shows none either).
/// </para>
/// </summary>
public static class WeatherSchema
{
    public const string FileName = "weather-schema.txt";

    /// <summary>The prefix of the translation keys of the sections, the group follows: IniSectionWeather.wind.</summary>
    public const string SectionPrefix = "IniSectionWeather.";

    /// <summary>The only keys the file may name: nothing else of eu07.ini is written because of it.</summary>
    public const string KeyRoot = "physics.weather";

    private const int SupportedSchema = 1;
    private const int MaxTextLength = 256 * 1024;
    private const int MaxLineLength = 1024;
    private const int MaxEntries = 512;
    private const int MaxKeyLength = 64;
    private const int MaxGroupLength = 32;
    private const int MaxLabelLength = 120;
    private const int MaxDecimals = 6;

    /// <summary>The keys of a file's text, in its order; none for a text that is not a file of a schema this starter knows.</summary>
    public static IReadOnlyList<SimulatorKey> Parse(string? text)
    {
        var keys = new List<SimulatorKey>();
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            return keys;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool schemaKnown = false;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line.Length > MaxLineLength)
                continue;

            string[] fields = line.Split('\t');
            if (!schemaKnown)
            {
                // The first line that is not a comment says which file this is.
                if (fields[0] != "schema" || fields.Length < 2 ||
                    !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version) ||
                    version != SupportedSchema)
                    return keys;
                schemaKnown = true;
                continue;
            }

            if (keys.Count >= MaxEntries)
                break;

            var key = fields[0] switch
            {
                "bool" => ParseToggle(fields),
                "float" => ParseNumber(fields),
                _ => null
            };
            if (key is not null && seen.Add(key.Key))
                keys.Add(key);
        }

        return keys;
    }

    // bool <key> <0|1> <group> <label>
    private static SimulatorKey? ParseToggle(string[] fields)
    {
        if (fields.Length < 5 || !IsKey(fields[1]) || !IsGroup(fields[3]))
            return null;
        if (fields[2] is not ("0" or "1"))
            return null;

        return new SimulatorKey(fields[1], SimulatorKeyKind.Toggle, SimulatorKey.FormatToggle(fields[2] == "1"),
                                SimulatorKeys.Physics, SectionPrefix + fields[3],
                                FallbackLabel: Label(fields[4], fields[1]));
    }

    // float <key> <default> <minimum> <maximum> <decimals> <group> <label>
    private static SimulatorKey? ParseNumber(string[] fields)
    {
        if (fields.Length < 8 || !IsKey(fields[1]) || !IsGroup(fields[6]))
            return null;
        if (!TryNumber(fields[2], out double value) || !TryNumber(fields[3], out double minimum) ||
            !TryNumber(fields[4], out double maximum))
            return null;
        if (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int decimals) ||
            decimals > MaxDecimals)
            return null;
        if (!(minimum < maximum) || value < minimum || value > maximum)
            return null;

        return new SimulatorKey(fields[1], SimulatorKeyKind.Number, value.ToString(CultureInfo.InvariantCulture),
                                SimulatorKeys.Physics, SectionPrefix + fields[6], minimum, maximum,
                                1.0 / Math.Pow(10, decimals), FallbackLabel: Label(fields[7], fields[1]));
    }

    // A point and no thousands, whatever the language of the machine: the simulator writes them so and reads them so.
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>physics.weather, or physics.weather.something of letters, digits and dots: a word of eu07.ini and no more.</summary>
    private static bool IsKey(string key)
    {
        if (key.Length > MaxKeyLength || !key.StartsWith(KeyRoot, StringComparison.Ordinal))
            return false;
        if (key.Length == KeyRoot.Length)
            return true;
        if (key[KeyRoot.Length] != '.' || key.Length == KeyRoot.Length + 1)
            return false;

        foreach (char c in key.AsSpan(KeyRoot.Length + 1))
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '.'))
                return false;
        return key[^1] != '.' && !key.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>The name of a group: it ends up in the translation key of its section.</summary>
    private static bool IsGroup(string group)
    {
        if (group.Length == 0 || group.Length > MaxGroupLength)
            return false;
        foreach (char c in group)
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '_'))
                return false;
        return true;
    }

    private static string Label(string label, string key)
    {
        string trimmed = label.Trim();
        if (trimmed.Length == 0)
            return key;
        return trimmed.Length > MaxLabelLength ? trimmed[..MaxLabelLength] : trimmed;
    }
}
