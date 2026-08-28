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
    /// <summary>0 = default off, 1 = default on, 2 = terrain (auto if no .sbt).</summary>
    public int Kind;
    public bool Selected;
}

public class Scenery
{
    public List<string> Lines;
    public List<Trainset> Trainsets;
    public List<SceneryAttachment> Attachments = new();
    public List<SceneryInclude> Includes = new();
    /// <summary>
    /// Names of <c>node … dynamic</c> entries outside trainsets (kept in the
    /// template on export; collected so the depot can avoid name clashes).
    /// </summary>
    public List<string> LooseVehicleNames = new();
    /// <summary>Parsed loose dynamics (Pascal <c>SCN.Vehicles</c>).</summary>
    public List<Dynamic> LooseVehicles = new();
    /// <summary>Parse / vehicle faults for the Info tab (Pascal FaultList).</summary>
    public List<string> Faults = new();
    public string Group;
    public string Path;

    // Starter directives (comment-syntax metadata, see wiki "Plik scenerii").
    // They do not affect the simulation, only how a starter presents the scenery.
    public string Name;        // //$n - scenery name
    public string Description; // //$d - scenery description
    public string ImageName;   // //$i - main-window image (scenery thumbnail)

    /// <summary>True when the scenery declares the //$a "archival" flag.</summary>
    public bool Archival;

    // Weather / environment. Like the original Starter, these are editable and are
    // written into the scenery's "config" block on launch (see RewriteWeather).
    //
    // Two clocks, matching Pascal TConfig.StartTime / TConfig.Time:
    //   Time                  -> classic "time … endtime" (from the SCN; UI does not edit it)
    //   ScenarioTimeOverride  -> "scenario.time.override" (what the Weather picker edits)
    // Defaults: classic start 10:30; override falls back to "now" when the SCN has neither.
    public string Time = "10:30"; // h:mm   -> "time … endtime"
    public string ScenarioTimeOverride = "10:30"; // h:mm   -> "scenario.time.override"
    public int Day = 0;                  // "movelight <day>" (day of year / season)
    public double Temperature = 15;      // "scenario.weather.temperature"
    public int FogStart = 10;            // atmo fog start (metres)
    public int FogEnd = 2000;            // visibility in metres (atmo fog range)
    public double Overcast = 0;          // atmo overcast factor (-1.5 .. 1.5)

    /// <summary>True when the scenery actually declared any weather command.</summary>
    public bool HasWeather;

    /// <summary>Set once the user edits the weather, so export rewrites the config.</summary>
    public bool WeatherDirty;

    // Snapshot of weather as loaded from the SCN (Pascal actRestoreWeather).
    private string _origTime = "10:30";
    private string _origOverride = "10:30";
    private int _origDay;
    private double _origTemperature = 15;
    private int _origFogEnd = 2000;
    private double _origOvercast;

    // The file content with each trainset block replaced by a {{i}} placeholder,
    // used to rebuild the .scn on export.
    private readonly string _template;

    /// <summary>Display label: //$n when set (with @token i18n), otherwise the file name.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return SceneryI18n.T(Name);
            return System.IO.Path.GetFileNameWithoutExtension(Path);
        }
    }

    /// <summary>//$d lines with @token translation applied.</summary>
    public string LocalizedDescription =>
        string.IsNullOrEmpty(Description) ? "" :
        string.Join("\n", Description.Split('\n').Select(SceneryI18n.T));

    // Lazily resolved, cached path to the //$i image on disk (null if not found).
    private string _imagePath;
    private bool _imagePathResolved;

    public Scenery(string path)
    {
        this.Path = path;
        Trainsets = new List<Trainset>();
        if (!File.Exists(path))
            throw new FileNotFoundException(path);
        var encoding = Encoding.GetEncoding(1250); // Windows-1250
        string content = File.ReadAllText(path, encoding);

        // property scanning - starter directives written as // comments.
        // \b after the letter keeps //$d from matching //$decor, //$i from
        // matching //$it, etc.
        this.Group = MatchDirective(content, "l");
        this.Name = MatchDirective(content, "n");
        // //$d may appear on several lines; each is one line of the description.
        this.Description = MatchAllDirectives(content, "d");
        this.ImageName = MatchDirective(content, "i");
        // //$a marks an archival scenery (present = archival, regardless of value).
        this.Archival = MatchDirective(content, "a") != null;

        // weather/environment is read from the raw text before trainset blocks
        // are stripped out below (the atmosphere commands live outside trainsets)
        ParseWeather(content);
        SnapshotWeather();

        ParseAttachments(content);
        content = ParseAndStripOptionalIncludes(content);

        // parsing trainsets
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

        // 1:1 with placeholders - the Trainset ctor never throws (unparsable
        // blocks are kept verbatim), so indices stay aligned for export.
        foreach (string trainsetEntry in trainsetEntries)
        {
            var ts = new Trainset(trainsetEntry);
            Trainsets.Add(ts);
            if (!ts.Parsed)
                Faults.Add($"# trainset parse fault: {ts.Name} ({ts.Track})");
        }
    }

    // Pull standalone node…dynamic blocks out of the template (Pascal SCN.Vehicles)
    // so export can re-emit them via PrepareNode(false) and names stay editable.
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

    /// <summary>
    /// Rebuilds the full .scn content with the (possibly modified) trainsets
    /// substituted back into their original positions.
    /// </summary>
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
            // "$f XX path label…" (Pascal) or "$f path label"
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

    // Pulls include … end //$optional… … endoptional out of the template so Start
    // can re-inject only the checked ones (Pascal ParseInc / actStartExecute).
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

    /// <summary>
    /// Reads the scenery's environment commands into the editable weather fields.
    /// Mirrors the original Starter (config: movelight / scenario.weather.temperature
    /// / scenario.time.override, plus top-level time and the atmo fog/overcast block).
    /// </summary>
    private void ParseWeather(string content)
    {
        // Classic "time h:mm" seeds both clocks (Pascal: Config.Time + Config.StartTime).
        // "scenario.time.override" seeds only the UI clock (Pascal ignored it on load;
        // we honour it so a previously exported $scn round-trips correctly).
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
            // No clock in the SCN → UI shows "now", classic export stays 10:30 (Pascal).
            var now = System.DateTime.Now;
            ScenarioTimeOverride = $"{now.Hour:D2}:{now.Minute:D2}";
        }

        // "movelight <day>" - day of the year (sun elevation / season)
        var move = Regex.Match(content, @"(?im)\bmovelight\s+(-?\d+)");
        if (move.Success && int.TryParse(move.Groups[1].Value, out int day))
        {
            Day = day;
            HasWeather = true;
        }

        // "scenario.weather.temperature <°C>"
        var temp = Regex.Match(content, @"(?i)scenario\.weather\.temperature\s+(-?\d+(?:\.\d+)?)");
        if (temp.Success &&
            double.TryParse(temp.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double t))
        {
            Temperature = t;
            HasWeather = true;
        }

        // "atmo R G B fogStart fogEnd R G B overcast endatmo"
        // Pascal ParseAtmo: FogEnd := RandomRange(FogStart, FogEnd).
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

    /// <summary>Restores weather fields to the values parsed from the SCN file.</summary>
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

    /// <summary>
    /// Replaces the scenery's environment commands with a fresh config block built
    /// from the current weather fields (the C# equivalent of the original Starter's
    /// TLexParser.ChangeConfig). On any failure the original text is returned
    /// unchanged so a launch is never blocked by a bad rewrite.
    /// </summary>
    private string RewriteWeather(string text)
    {
        try
        {
            // strip the existing weather commands
            string s = text;
            s = Regex.Replace(s, @"(?is)\bconfig\b.*?\bendconfig\b", " ");
            s = Regex.Replace(s, @"(?is)\btime\b\s+\d[^\r\n]*?\bendtime\b", " ");
            s = Regex.Replace(s, @"(?is)\batmo\b.*?\bendatmo\b", " ");
            s = Regex.Replace(s, @"(?im)^[ \t]*movelight\s+\S+", " ");
            s = Regex.Replace(s, @"(?i)scenario\.weather\.temperature\s+\S+", " ");
            s = Regex.Replace(s, @"(?i)scenario\.time\.override\s+\S+", " ");

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // Override = picker value; classic time = original SCN start (unchanged by UI).
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

    /// <summary>
    /// Collects every //$&lt;letter&gt; line (e.g. all //$d lines) and joins them
    /// into one multi-line string. Returns null when none are present.
    /// </summary>
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

    /// <summary>
    /// Reads a single starter directive value (the text after //$&lt;letter&gt;).
    /// Returns null when the directive is absent. The trailing whitespace
    /// requirement separates e.g. //$d from //$decor and //$i from //$it.
    /// </summary>
    private static string MatchDirective(string content, string letter)
    {
        var match = Regex.Match(
            content,
            @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)",
            RegexOptions.Multiline
        );
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Resolved on-disk path of the //$i scenery image, or null if none is
    /// declared or the file cannot be found. The value of //$i may be a bare
    /// file name or a path; common locations are probed and the result cached.
    /// </summary>
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

        // normalise legacy back-slashes so paths work cross-platform
        string name = ImageName.Replace('\\', '/').Trim();
        string scnDir = System.IO.Path.GetDirectoryName(Path) ?? ".";   // e.g. scenery/
        string root = System.IO.Path.GetDirectoryName(scnDir) ?? ".";   // MaSzyna root

        // probe the usual places, first hit wins
        var candidates = new List<string>
        {
            name,                                                       // as given (cwd / absolute)
            System.IO.Path.Combine(root, name),                         // relative to MaSzyna root
            System.IO.Path.Combine(scnDir, name),                       // next to the .scn
            System.IO.Path.Combine(scnDir, "images", name),             // scenery/images/
            System.IO.Path.Combine(root, "scenery", "images", name),    // scenery/images/ from root
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
