using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Domain;

namespace StarterNG.Classes;

public sealed class SceneryAttachment
{
    public string FilePath = "";
    public string Label = "";
}

public sealed class SceneryInclude
{
    public string FilePath = "";
    public string Desc = "";
    public int Kind;
    public bool Selected;
}

public class Scenery
{
    public List<string> Lines;
    public List<Trainset> Trainsets;
    public List<SceneryAttachment> Attachments = new();
    public List<SceneryInclude> Includes = new();

    public List<string> LooseVehicleNames = new();

    public List<Dynamic> LooseVehicles = new();

    public List<string> Faults = new();
    public string Group;
    public string Path;

    public string Name;
    public string Description;
    public string ImageName;

    public bool Archival;

    public string Time = "10:30";
    public string ScenarioTimeOverride = "10:30";
    public int Day = 0;
    public double Temperature = 15;
    public int FogStart = 10;
    public int FogEnd = 2000;
    public double Overcast = 0;

    public bool HasWeather;

    public bool WeatherDirty;

    private string _origTime = "10:30";
    private string _origOverride = "10:30";
    private int _origDay;
    private double _origTemperature = 15;
    private int _origFogEnd = 2000;
    private double _origOvercast;

    private readonly string _template;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return SceneryI18n.T(Name);
            return System.IO.Path.GetFileNameWithoutExtension(Path);
        }
    }

    public string LocalizedDescription =>
        string.IsNullOrEmpty(Description) ? "" :
        string.Join("\n", Description.Split('\n').Select(SceneryI18n.T));

    private string _imagePath;
    private bool _imagePathResolved;

    public Scenery(string path)
    {
        this.Path = path;
        Trainsets = new List<Trainset>();
        if (!File.Exists(path))
            throw new FileNotFoundException(path);
        var encoding = Encoding.GetEncoding(1250);
        string content = File.ReadAllText(path, encoding);

        this.Group = MatchDirective(content, "l");
        this.Name = MatchDirective(content, "n");

        this.Description = MatchAllDirectives(content, "d");
        this.ImageName = MatchDirective(content, "i");

        this.Archival = MatchDirective(content, "a") != null;

        ParseWeather(content);
        SnapshotWeather();

        ParseAttachments(content);
        content = ParseAndStripOptionalIncludes(content);

        List<string> trainsetEntries = new  List<string>();
        Regex regex = new Regex(
            @"trainset\b[\s\S]*?\bendtrainset\b",
            RegexOptions.IgnoreCase
        );
        int idx = 0;
        content = regex.Replace(content, match =>
        {
            trainsetEntries.Add(match.Value);
            return $"{{{{{idx++}}}}}";
        });
        content = ExtractLooseVehicles(content);
        _template = content;

        foreach (string trainsetEntry in trainsetEntries)
        {
            var ts = new Trainset(trainsetEntry);
            Trainsets.Add(ts);
            if (!ts.Parsed)
                Faults.Add($"# trainset parse fault: {ts.Name} ({ts.Track})");
        }
    }

    private string ExtractLooseVehicles(string content)
    {
        LooseVehicles.Clear();
        LooseVehicleNames.Clear();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

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
                    var d = new Dynamic
                    {
                        RangeMax = float.Parse(m.Groups[1].Value, inv),
                        RangeMin = float.Parse(m.Groups[2].Value, inv),
                        Name = name,
                        DataFolder = m.Groups[4].Value,
                        SkinFile = m.Groups[5].Value,
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
                    d.ReadTrailing(trailing);
                    LooseVehicles.Add(d);
                    LooseVehicleNames.Add(d.Name);
                    return "\n";
                }
                catch
                {
                    Faults.Add($"# loose dynamic parse fault: {name}");
                    LooseVehicleNames.Add(name);
                    return m.Value;
                }
            });
    }

    public string BuildExportContent(bool skipDecorTrainsets = false)
    {
        string result = _template;

        if (WeatherDirty)
            result = RewriteWeather(result);

        result = InjectIncludes(result);

        for (int i = 0; i < Trainsets.Count; i++)
        {
            string entry = skipDecorTrainsets && Trainsets[i].Decor
                ? string.Empty
                : Trainsets[i].ToSceneryEntry();
            result = result.Replace("{{" + i + "}}", entry);
        }

        if (LooseVehicles.Count > 0)
            result = AppendLooseVehicles(result);

        return result;
    }

    private string AppendLooseVehicles(string text)
    {
        var sb = new StringBuilder();
        foreach (var v in LooseVehicles)
            sb.Append(v.ToLooseNode());
        if (sb.Length == 0) return text;

        var fi = Regex.Match(text, @"\bFirstInit\b", RegexOptions.IgnoreCase);
        if (fi.Success)
            return text[..fi.Index] + sb + text[fi.Index..];
        return text + sb;
    }

    private void ParseAttachments(string content)
    {
        Attachments.Clear();
        foreach (Match m in Regex.Matches(content, @"^//\$f\b[ \t]*([^\r\n]*)", RegexOptions.Multiline))
        {
            string rest = m.Groups[1].Value.Trim();
            if (rest.Length == 0) continue;
            string[] parts = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3 && parts[0].Length <= 2)
                Attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[1],
                    Label = string.Join(" ", parts.Skip(2))
                });
            else if (parts.Length >= 2)
                Attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[0],
                    Label = string.Join(" ", parts.Skip(1))
                });
            else
                Attachments.Add(new SceneryAttachment
                {
                    FilePath = parts[0],
                    Label = System.IO.Path.GetFileName(parts[0])
                });
        }
    }

    private string ParseAndStripOptionalIncludes(string content)
    {
        Includes.Clear();
        return Regex.Replace(
            content,
            @"include\s+(\S+(?:\s+\S+)*?)\s+end\s*//[^\r\n]*\$optional([^\r\n]*)\r?\n([\s\S]*?)endoptional\b",
            m =>
            {
                string incPath = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ");
                string raw = m.Groups[2].Value.Trim().TrimStart(',', ' ');
                string[] par = raw.Split(new[] { ',', '|', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                int kind = 1;
                string desc = incPath;
                if (par.Length >= 1 && int.TryParse(par[0], out int k))
                    kind = k;
                if (par.Length >= 2)
                    desc = par[1];

                Includes.Add(new SceneryInclude
                {
                    FilePath = incPath,
                    Desc = desc,
                    Kind = kind,
                    Selected = kind == 1
                });
                return " ";
            },
            RegexOptions.IgnoreCase);
    }

    private string InjectIncludes(string text)
    {
        if (Includes.Count == 0)
            return text;

        var sb = new StringBuilder();
        string sbt = System.IO.Path.ChangeExtension(Path, ".sbt");
        bool hasSbt = File.Exists(sbt);

        foreach (var inc in Includes)
        {
            if (inc.Kind == 2)
            {
                if (!hasSbt)
                    sb.Append("include ").Append(inc.FilePath).Append(" end\r\n");
                continue;
            }
            if (inc.Selected)
                sb.Append("include ").Append(inc.FilePath).Append(" end\r\n");
        }

        if (sb.Length == 0)
            return text;

        var fi = Regex.Match(text, @"\bFirstInit\b", RegexOptions.IgnoreCase);
        if (fi.Success)
            return text[..fi.Index] + sb + text[fi.Index..];
        return sb + text;
    }

    private void ParseWeather(string content)
    {

        var time = Regex.Match(content, @"(?im)^\s*time\s+(\d{1,2})[:.](\d{2})\b");
        if (time.Success)
        {
            Time = $"{time.Groups[1].Value.PadLeft(2, '0')}:{time.Groups[2].Value}";
            ScenarioTimeOverride = Time;
        }

        var ovr = Regex.Match(content, @"(?i)scenario\.time\.override\s+(\d{1,2})[:.](\d{2})");
        if (ovr.Success)
        {
            ScenarioTimeOverride = $"{ovr.Groups[1].Value.PadLeft(2, '0')}:{ovr.Groups[2].Value}";
            HasWeather = true;
        }
        else if (!time.Success)
        {

            var now = System.DateTime.Now;
            ScenarioTimeOverride = $"{now.Hour:D2}:{now.Minute:D2}";
        }

        var move = Regex.Match(content, @"(?im)\bmovelight\s+(-?\d+)");
        if (move.Success && int.TryParse(move.Groups[1].Value, out int day))
        {
            Day = day;
            HasWeather = true;
        }

        var temp = Regex.Match(content, @"(?i)scenario\.weather\.temperature\s+(-?\d+(?:\.\d+)?)");
        if (temp.Success &&
            double.TryParse(temp.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double t))
        {
            Temperature = t;
            HasWeather = true;
        }

        var atmo = Regex.Match(content, @"(?is)\batmo\b(.*?)\bendatmo\b");
        if (atmo.Success)
        {
            var nums = Regex.Matches(atmo.Groups[1].Value, @"-?\d+(?:\.\d+)?")
                .Select(m => m.Value).ToList();
            if (nums.Count >= 5 &&
                int.TryParse(nums[3], out int fogStart) &&
                int.TryParse(nums[4], out int fogEnd))
            {
                FogStart = Math.Clamp(fogStart, 10, 2500);
                fogEnd = Math.Clamp(fogEnd, FogStart, 2500);
                FogEnd = fogEnd > FogStart
                    ? Random.Shared.Next(FogStart, fogEnd + 1)
                    : fogEnd;
                HasWeather = true;
            }
            if (nums.Count >= 6 &&
                double.TryParse(nums[^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double oc))
                Overcast = oc;
        }
    }

    private void SnapshotWeather()
    {
        _origTime = Time;
        _origOverride = ScenarioTimeOverride;
        _origDay = Day;
        _origTemperature = Temperature;
        _origFogEnd = FogEnd;
        _origOvercast = Overcast;
    }

    public void RestoreWeather()
    {
        Time = _origTime;
        ScenarioTimeOverride = _origOverride;
        Day = _origDay;
        Temperature = _origTemperature;
        FogEnd = _origFogEnd;
        Overcast = _origOvercast;
        WeatherDirty = true;
    }

    private string RewriteWeather(string text)
    {
        try
        {

            string s = text;
            s = Regex.Replace(s, @"(?is)\bconfig\b.*?\bendconfig\b", " ");
            s = Regex.Replace(s, @"(?is)\btime\b\s+\d[^\r\n]*?\bendtime\b", " ");
            s = Regex.Replace(s, @"(?is)\batmo\b.*?\bendatmo\b", " ");
            s = Regex.Replace(s, @"(?im)^[ \t]*movelight\s+\S+", " ");
            s = Regex.Replace(s, @"(?i)scenario\.weather\.temperature\s+\S+", " ");
            s = Regex.Replace(s, @"(?i)scenario\.time\.override\s+\S+", " ");

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            string config =
                "config\r\n" +
                $"movelight {Day}\r\n" +
                $"scenario.weather.temperature {Temperature.ToString(inv)}\r\n" +
                $"scenario.time.override {ScenarioTimeOverride}\r\n" +
                "endconfig\r\n" +
                $"atmo 0 0 0 {FogEnd} {FogEnd} 0 0 0 {Overcast.ToString(inv)} endatmo\r\n" +
                $"time {Time} 0 0 endtime\r\n";

            return config + s;
        }
        catch
        {
            return text;
        }
    }

    private static string MatchAllDirectives(string content, string letter)
    {
        var matches = Regex.Matches(
            content,
            @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)",
            RegexOptions.Multiline
        );
        if (matches.Count == 0)
            return null;
        return string.Join("\n", matches.Select(m => m.Groups[1].Value.TrimEnd()));
    }

    private static string MatchDirective(string content, string letter)
    {
        var match = Regex.Match(
            content,
            @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)",
            RegexOptions.Multiline
        );
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public string ImagePath
    {
        get
        {
            if (_imagePathResolved)
                return _imagePath;
            _imagePathResolved = true;
            _imagePath = ResolveImagePath();
            return _imagePath;
        }
    }

    private string ResolveImagePath()
    {
        if (string.IsNullOrWhiteSpace(ImageName))
            return null;

        string name = ImageName.Replace('\\', '/').Trim();
        string scnDir = System.IO.Path.GetDirectoryName(Path) ?? ".";
        string root = System.IO.Path.GetDirectoryName(scnDir) ?? ".";

        var candidates = new List<string>
        {
            name,
            System.IO.Path.Combine(root, name),
            System.IO.Path.Combine(scnDir, name),
            System.IO.Path.Combine(scnDir, "images", name),
            System.IO.Path.Combine(root, "scenery", "images", name),
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
