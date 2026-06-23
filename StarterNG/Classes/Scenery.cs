using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StarterNG.Classes;

public class Scenery
{
    public List<string> Lines;
    public List<Trainset> Trainsets;
    public string Group;
    public string Path;

    // Starter directives (comment-syntax metadata, see wiki "Plik scenerii").
    // They do not affect the simulation, only how a starter presents the scenery.
    public string Name;        // //$n - scenery name
    public string Description; // //$d - scenery description
    public string ImageName;   // //$i - main-window image (scenery thumbnail)

    // Weather / environment. Like the original Starter, these are editable and are
    // written into the scenery's "config" block on launch (see RewriteWeather).
    // Defaults mirror the original (15 °C, day 0, 10:30, clear sky).
    public string WeatherTime = "10:30"; // h:mm   -> "time"/"scenario.time.override"
    public int Day = 0;                  // "movelight <day>" (day of year / season)
    public double Temperature = 15;      // "scenario.weather.temperature"
    public int FogEnd = 2000;            // visibility in metres (atmo fog range)
    public double Overcast = 0;          // atmo overcast factor (-1.5 .. 1.5)

    /// <summary>True when the scenery actually declared any weather command.</summary>
    public bool HasWeather;

    /// <summary>Set once the user edits the weather, so export rewrites the config.</summary>
    public bool WeatherDirty;

    // The file content with each trainset block replaced by a {{i}} placeholder,
    // used to rebuild the .scn on export.
    private readonly string _template;

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

        // weather/environment is read from the raw text before trainset blocks
        // are stripped out below (the atmosphere commands live outside trainsets)
        ParseWeather(content);


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
        _template = content;

        // 1:1 with placeholders - the Trainset ctor never throws (unparsable
        // blocks are kept verbatim), so indices stay aligned for export.
        foreach (string trainsetEntry in trainsetEntries)
            Trainsets.Add(new Trainset(trainsetEntry));
    }

    /// <summary>
    /// Rebuilds the full .scn content with the (possibly modified) trainsets
    /// substituted back into their original positions.
    /// </summary>
    public string BuildExportContent()
    {
        string result = _template;

        // Inject the (edited) weather into the config block before substituting
        // trainsets, so the {{i}} placeholders shield the trainset text from the
        // weather rewrite. Only done when the user actually changed the weather.
        if (WeatherDirty)
            result = RewriteWeather(result);

        for (int i = 0; i < Trainsets.Count; i++)
            result = result.Replace("{{" + i + "}}", Trainsets[i].ToSceneryEntry());
        return result;
    }

    /// <summary>
    /// Reads the scenery's environment commands into the editable weather fields.
    /// Mirrors the original Starter (config: movelight / scenario.weather.temperature
    /// / scenario.time.override, plus top-level time and the atmo fog/overcast block).
    /// </summary>
    private void ParseWeather(string content)
    {
        // start time: "scenario.time.override h:mm" wins, else top-level "time h:mm"
        var ovr = Regex.Match(content, @"(?i)scenario\.time\.override\s+(\d{1,2})[:.](\d{2})");
        var time = ovr.Success
            ? ovr
            : Regex.Match(content, @"(?im)^\s*time\s+(\d{1,2})[:.](\d{2})\b");
        if (time.Success)
        {
            WeatherTime = $"{time.Groups[1].Value.PadLeft(2, '0')}:{time.Groups[2].Value}";
            HasWeather = true;
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
        var atmo = Regex.Match(content, @"(?is)\batmo\b(.*?)\bendatmo\b");
        if (atmo.Success)
        {
            var nums = Regex.Matches(atmo.Groups[1].Value, @"-?\d+(?:\.\d+)?")
                .Select(m => m.Value).ToList();
            if (nums.Count >= 5 && int.TryParse(nums[4], out int fog))
            {
                FogEnd = fog;
                HasWeather = true;
            }
            if (nums.Count >= 6 &&
                double.TryParse(nums[^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double oc))
                Overcast = oc;
        }
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
            string config =
                "config\r\n" +
                $"movelight {Day}\r\n" +
                $"scenario.weather.temperature {Temperature.ToString(inv)}\r\n" +
                $"scenario.time.override {WeatherTime}\r\n" +
                $"time {WeatherTime} 0 0 endtime\r\n" +
                $"atmo 0 0 0 {FogEnd} {FogEnd} 0 0 0 {Overcast.ToString(inv)} endatmo\r\n" +
                "endconfig\r\n";

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
