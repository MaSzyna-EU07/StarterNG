using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
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

    /// <param name="findInclude">
    /// What the simulator makes of an include, as the include names it; null leaves
    /// includes unchecked.
    /// </param>
    public Scenery Parse(string path, string content, Func<string, IncludeLookup>? findInclude = null)
    {
        string original = content;

        // A file put in place of its include has directives of its own, for itself.
        string own = SceneryIncludes.WithoutInlined(original);

        var attachments = ParseAttachments(own);

        var includes = new List<SceneryInclude>();
        content = StripOptionalIncludes(content, includes);

        // Read from the text with its optional includes blanked, which keeps every
        // place where it was, so a fault's line is the line in the file.
        var syntax = ScenerySyntax.Scan(content, System.IO.Path.GetFileName(path), findInclude);
        var faults = syntax.Faults.ToList();
        var inlined = SceneryIncludes.Ranges(content);

        var trainsets = new List<Trainset>();
        var looseVehicles = new List<Dynamic>();
        var looseNames = new List<string>();
        var cuts = new List<(int Start, int End, string With)>();

        foreach (var (start, end) in syntax.Trainsets)
        {
            var trainset = new Trainset(content[start..end]) { SourceFile = SceneryIncludes.FileAt(inlined, start) };
            if (!trainset.Parsed && !faults.Any(f => f.Offset >= start && f.Offset < end))
                faults.Add(syntax.Lines.Fault(SceneryFaultKind.TrainsetUnreadable, start, trainset.Name));
            cuts.Add((start, end, $"{{{{{trainsets.Count}}}}}"));
            trainsets.Add(trainset);
        }

        foreach (var (start, end) in syntax.LooseVehicles)
        {
            // Included files' loose vehicles - road traffic, mostly - stay where they
            // are as written, only their names are noted.
            var vehicle = ReadLooseVehicle(content[start..end]);
            looseNames.Add(vehicle?.Name ?? LooseName(content[start..end]));
            if (SceneryIncludes.FileAt(inlined, start) != null)
                continue;

            if (vehicle is null)
            {
                if (!faults.Any(f => f.Offset >= start && f.Offset < end))
                    faults.Add(syntax.Lines.Fault(SceneryFaultKind.VehicleUnreadable, start, LooseName(content[start..end])));
                continue;
            }

            looseVehicles.Add(vehicle);
            cuts.Add((start, end, "\n"));
        }

        var scenery = new Scenery(path, Cut(content, cuts))
        {
            Group = MatchDirective(own, "l"),
            Name = MatchDirective(own, "n"),
            Description = MatchAllDirectives(own, "d"),
            ImageName = MatchDirective(own, "i"),
            Archival = MatchDirective(own, "a") != null
        };

        scenery.Attachments.AddRange(attachments);
        scenery.Includes.AddRange(includes);
        scenery.Trainsets.AddRange(trainsets);
        scenery.LooseVehicles.AddRange(looseVehicles);
        scenery.LooseVehicleNames.AddRange(looseNames);
        scenery.Faults.AddRange(faults.OrderBy(f => f.Offset));

        // Without its comments: what is read here is what the writer later puts down as active.
        ParseWeather(SceneryCode.Mask(original, out _), scenery.Weather);
        scenery.Weather.AcceptAsAuthored();

        return scenery;
    }

    private static string Cut(string text, List<(int Start, int End, string With)> cuts)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int at = 0;
        foreach (var (start, end, with) in cuts.OrderBy(c => c.Start))
        {
            sb.Append(text, at, start - at).Append(with);
            at = end;
        }
        return sb.Append(text, at, text.Length - at).ToString();
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
            @"include[ \t]+(\S+(?:[ \t]+\S+)*?)[ \t]+end[ \t]*//[^\r\n]*\$optional([^\r\n]*)\r?\n([\s\S]*?)endoptional\b",
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
                return Regex.Replace(m.Value, @"[^\r\n]", " ");
            },
            RegexOptions.IgnoreCase);
    }

    private static readonly Regex LooseNode = new(
        @"(?is)^node\s+(\S+)\s+(\S+)\s+(\S+)\s+dynamic\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)((?:\s+(?!enddynamic\b)\S+)*)\s*enddynamic$");

    private static string LooseName(string node) =>
        SceneryCode.Tokens(SceneryCode.Mask(node, out _)).ElementAtOrDefault(3).Text ?? "";

    /// <summary>A vehicle standing on its own, or null when its node cannot be read.</summary>
    private static Dynamic? ReadLooseVehicle(string node)
    {
        var inv = CultureInfo.InvariantCulture;
        var m = LooseNode.Match(SceneryCode.Mask(node, out _));
        if (!m.Success)
            return null;

        try
        {
            var vehicle = new Dynamic
            {
                RangeMax = float.Parse(m.Groups[1].Value, inv),
                RangeMin = float.Parse(m.Groups[2].Value, inv),
                Name = m.Groups[3].Value,
                DataFolder = m.Groups[4].Value,
                SkinFile = Dynamic.StripSkinExtension(m.Groups[5].Value),
                MmdFile = m.Groups[6].Value,
                PathName = m.Groups[7].Value,
                Offset = float.Parse(m.Groups[8].Value, inv),
                DriverType = Dynamic.ParseDriver(m.Groups[9].Value),
                HasVelocity = true,
                Velocity = float.Parse(m.Groups[10].Value, inv)
            };

            vehicle.ReadTrailing(m.Groups[11].Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .ToList());
            return vehicle;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
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
