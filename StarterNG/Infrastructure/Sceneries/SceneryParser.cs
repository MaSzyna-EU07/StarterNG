using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class SceneryParser
{
    private readonly IClock _clock;
    private readonly IRandomSource _random;

    public SceneryParser(IClock clock, IRandomSource random)
    {
        _clock = clock;
        _random = random;
    }

    public Scenery Parse(string path, string content)
    {
        string original = content;

        var faults = new List<string>();
        var attachments = ParseAttachments(original);

        var includes = new List<SceneryInclude>();
        content = StripOptionalIncludes(content, includes);

        var trainsetEntries = new List<string>();
        content = Regex.Replace(content, @"trainset\b[\s\S]*?\bendtrainset\b",
            match =>
            {
                trainsetEntries.Add(match.Value);
                return $"{{{{{trainsetEntries.Count - 1}}}}}";
            },
            RegexOptions.IgnoreCase);

        var looseVehicles = new List<Dynamic>();
        var looseNames = new List<string>();
        content = ExtractLooseVehicles(content, looseVehicles, looseNames, faults);

        var scenery = new Scenery(path, content)
        {
            Group = MatchDirective(original, "l"),
            Name = MatchDirective(original, "n"),
            Description = MatchAllDirectives(original, "d"),
            ImageName = MatchDirective(original, "i"),
            Archival = MatchDirective(original, "a") != null
        };

        scenery.Attachments.AddRange(attachments);
        scenery.Includes.AddRange(includes);
        scenery.LooseVehicles.AddRange(looseVehicles);
        scenery.LooseVehicleNames.AddRange(looseNames);
        scenery.Faults.AddRange(faults);

        ParseWeather(original, scenery.Weather);
        scenery.Weather.AcceptAsAuthored();

        foreach (string entry in trainsetEntries)
        {
            var trainset = new Trainset(entry);
            scenery.Trainsets.Add(trainset);
            if (!trainset.Parsed)
                scenery.Faults.Add($"# trainset parse fault: {trainset.Name} ({trainset.Track})");
        }

        return scenery;
    }

    private static List<SceneryAttachment> ParseAttachments(string content)
    {
        var attachments = new List<SceneryAttachment>();

        foreach (Match m in Regex.Matches(content, @"^//\$f\b[ \t]*([^\r\n]*)", RegexOptions.Multiline))
        {
            string rest = m.Groups[1].Value.Trim();
            if (rest.Length == 0)
                continue;

            string[] parts = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3 && parts[0].Length <= 2)
                attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[1],
                    Label = string.Join(" ", parts.Skip(2))
                });
            else if (parts.Length >= 2)
                attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[0],
                    Label = string.Join(" ", parts.Skip(1))
                });
            else
                attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[0],
                    Label = System.IO.Path.GetFileName(parts[0])
                });
        }

        return attachments;
    }

    private static string StripOptionalIncludes(string content, List<SceneryInclude> includes)
    {
        if (content.IndexOf("$optional", StringComparison.OrdinalIgnoreCase) < 0)
            return content;

        return Regex.Replace(
            content,
            @"include\s+(\S+(?:\s+\S+)*?)\s+end\s*//[^\r\n]*\$optional([^\r\n]*)\r?\n([\s\S]*?)endoptional\b",
            m =>
            {
                string includePath = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ");
                string raw = m.Groups[2].Value.Trim().TrimStart(',', ' ');
                string[] parameters = raw.Split(new[] { ',', '|', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                int kind = 1;
                string description = includePath;
                if (parameters.Length >= 1 && int.TryParse(parameters[0], out int parsedKind))
                    kind = parsedKind;
                if (parameters.Length >= 2)
                    description = parameters[1];

                includes.Add(new SceneryInclude
                {
                    FilePath = includePath,
                    Desc = description,
                    Kind = kind,
                    Selected = kind == 1
                });
                return " ";
            },
            RegexOptions.IgnoreCase);
    }

    private static string ExtractLooseVehicles(string content, List<Dynamic> vehicles, List<string> names,
                                               List<string> faults)
    {
        var inv = CultureInfo.InvariantCulture;

        return Regex.Replace(
            content,
            @"(?is)\bnode\s+(\S+)\s+(\S+)\s+(\S+)\s+dynamic\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)((?:\s+(?!enddynamic)\S+)*)\s*enddynamic\b",
            m =>
            {
                string name = m.Groups[3].Value;

                if (name.StartsWith("{{", StringComparison.Ordinal))
                    return m.Value;

                try
                {
                    var vehicle = new Dynamic
                    {
                        RangeMax = float.Parse(m.Groups[1].Value, inv),
                        RangeMin = float.Parse(m.Groups[2].Value, inv),
                        Name = name,
                        DataFolder = m.Groups[4].Value,
                        SkinFile = Dynamic.StripSkinExtension(m.Groups[5].Value),
                        MmdFile = m.Groups[6].Value,
                        PathName = m.Groups[7].Value,
                        Offset = float.Parse(m.Groups[8].Value, inv),
                        DriverType = m.Groups[9].Value.ToLowerInvariant() switch
                        {
                            "headdriver" => eDriverType.Headdriver,
                            "reardriver" => eDriverType.Reardriver,
                            "passenger" => eDriverType.Passenger,
                            _ => eDriverType.Nobody
                        },
                        HasVelocity = true,
                        Velocity = float.Parse(m.Groups[10].Value, inv)
                    };

                    var trailing = m.Groups[11].Value
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                    vehicle.ReadTrailing(trailing);

                    vehicles.Add(vehicle);
                    names.Add(vehicle.Name);
                    return "\n";
                }
                catch
                {
                    faults.Add($"# loose dynamic parse fault: {name}");
                    names.Add(name);
                    return m.Value;
                }
            });
    }

    private void ParseWeather(string content, SceneryWeather weather)
    {
        var time = Regex.Match(content, @"(?im)^\s*time\s+(\d{1,2})[:.](\d{2})\b");
        if (time.Success)
        {
            weather.Time = $"{time.Groups[1].Value.PadLeft(2, '0')}:{time.Groups[2].Value}";
            weather.ScenarioTimeOverride = weather.Time;
        }

        var over = Regex.Match(content, @"(?i)scenario\.time\.override\s+(\d{1,2})[:.](\d{2})");
        if (over.Success)
        {
            weather.ScenarioTimeOverride = $"{over.Groups[1].Value.PadLeft(2, '0')}:{over.Groups[2].Value}";
            weather.IsAuthored = true;
        }
        else if (!time.Success)
        {
            var now = _clock.Now;
            weather.ScenarioTimeOverride = $"{now.Hour:D2}:{now.Minute:D2}";
        }

        var move = Regex.Match(content, @"(?im)\bmovelight\s+(-?\d+)");
        if (move.Success && int.TryParse(move.Groups[1].Value, out int day))
        {
            weather.Day = day;
            weather.IsAuthored = true;
        }

        var temperature = Regex.Match(content, @"(?i)scenario\.weather\.temperature\s+(-?\d+(?:\.\d+)?)");
        if (temperature.Success &&
            double.TryParse(temperature.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double celsius))
        {
            weather.Temperature = celsius;
            weather.IsAuthored = true;
        }

        var atmo = Regex.Match(content, @"(?is)\batmo\b(.*?)\bendatmo\b");
        if (!atmo.Success)
            return;

        var numbers = Regex.Matches(atmo.Groups[1].Value, @"-?\d+(?:\.\d+)?")
            .Select(m => m.Value)
            .ToList();

        if (numbers.Count >= 5 &&
            int.TryParse(numbers[3], out int fogStart) &&
            int.TryParse(numbers[4], out int fogEnd))
        {
            weather.SetFogRange(fogStart, fogEnd, _random);
            weather.IsAuthored = true;
        }

        if (numbers.Count >= 6 &&
            double.TryParse(numbers[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double overcast))
            weather.Overcast = overcast;
    }

    private static string? MatchAllDirectives(string content, string letter)
    {
        var matches = Regex.Matches(content, @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)", RegexOptions.Multiline);
        return matches.Count == 0
            ? null
            : string.Join("\n", matches.Select(m => m.Groups[1].Value.TrimEnd()));
    }

    private static string? MatchDirective(string content, string letter)
    {
        var match = Regex.Match(content, @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
