using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StarterNG.Classes;

public enum eDriverType
{
    Headdriver,
    Reardriver,
    Passenger,
    Nobody
}

public class Trainset
{
    public string Name;
    public string Track;
    public float Offset;
    public float Velocity;
    public float OriginalVelocity;
    public string Description;
    public List<Dynamic> Vehicles;

    public string RawEntry;

    public bool Parsed;

    public bool Decor;

    public string? Logo;

    public string? Mini;

    public Trainset(string trainsetEntry)
    {
        RawEntry = trainsetEntry;
        Decor = Regex.IsMatch(trainsetEntry, @"//\$decor\b", RegexOptions.IgnoreCase);
        Vehicles = new List<Dynamic>();
        Description = "";
        try
        {
            ParseEntry(trainsetEntry);
            Parsed = true;
        }
        catch
        {
            Parsed = false;
        }
    }

    private void ParseEntry(string trainsetEntry)
    {
        List<string> tokens = Regex
            .Matches(trainsetEntry, @"/\*[\s\S]*?\*/|//[^\r\n]*|[^\s\r\n]+")
            .Select(m => m.Value)
            .Where(t => !t.StartsWith("//") && !t.StartsWith("/*"))
            .ToList();

        var match = Regex.Match(
            trainsetEntry,
            @"^\s*//\$o\s*(.*)$",
            RegexOptions.Multiline
        );
        if (match.Success)
            this.Description = match.Groups[1].Value.Trim();

        var logo = Regex.Match(trainsetEntry, @"^\s*//\$il\b[ \t]*([^\r\n]*)", RegexOptions.Multiline);
        if (logo.Success)
            Logo = logo.Groups[1].Value.Trim();

        var mini = Regex.Match(trainsetEntry, @"^\s*//\$it\b[ \t]*([^\r\n]*)", RegexOptions.Multiline);
        if (mini.Success)
            Mini = mini.Groups[1].Value.Trim();

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "endtrainset")
                break;

            if (tokens[i] == "trainset")
            {
                this.Name = tokens[++i];
                this.Track = tokens[++i];
                this.Offset = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                this.Velocity = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                this.OriginalVelocity = this.Velocity;
                continue;
            }

            if (tokens[i] == "assignments")
            {
                while (i < tokens.Count && tokens[i] != "endassignment")
                    i++;
                i++;
                continue;
            }

            if (tokens[i] == "node")
            {
                Dynamic nodeDynamic = new Dynamic();
                nodeDynamic.RangeMax = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.RangeMin = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.Name = tokens[++i];
                i++;
                nodeDynamic.DataFolder = tokens[++i];
                nodeDynamic.SkinFile = tokens[++i];
                nodeDynamic.MmdFile = tokens[++i];
                nodeDynamic.Offset = float.Parse(tokens[++i], CultureInfo.InvariantCulture);

                nodeDynamic.DriverType = tokens[++i] switch
                {
                    "headdriver" => eDriverType.Headdriver,
                    "reardriver" => eDriverType.Reardriver,
                    "passenger"  => eDriverType.Passenger,
                    _            => eDriverType.Nobody
                };

                nodeDynamic.Coupling = Coupling.Parse(tokens[++i]);

                var trailing = new List<string>();
                while (i + 1 < tokens.Count && tokens[i + 1] != "enddynamic")
                    trailing.Add(tokens[++i]);
                nodeDynamic.ReadTrailing(trailing);

                i++;

                Vehicles.Add(nodeDynamic);
            }
        }
    }

    public string ToSceneryEntry() =>
        Parsed ? GetTrainsetEntry() + "endtrainset\n" : RawEntry;

    public string GetTrainsetEntry()
    {

        var sb = new StringBuilder();
        if (Decor)
            sb.Append("//$decor\n");
        if (!string.IsNullOrEmpty(Description))
            sb.Append("//$o ").Append(Description).Append('\n');
        if (!string.IsNullOrEmpty(Logo))
            sb.Append("//$il ").Append(Logo).Append('\n');
        if (!string.IsNullOrEmpty(Mini))
            sb.Append("//$it ").Append(Mini).Append('\n');

        sb.Append("trainset ");
        sb.Append(Name).Append(' ');
        sb.Append(Track).Append(' ');
        sb.Append(Offset.ToString(CultureInfo.InvariantCulture)).Append(' ');
        sb.Append(Velocity.ToString(CultureInfo.InvariantCulture));
        sb.Append('\n');

        string entry = sb.ToString();
        foreach (Dynamic vehicle in Vehicles)
        {
            string driverType = "";
            switch (vehicle.DriverType)
            {
                case eDriverType.Headdriver:
                    driverType = "headdriver";
                    break;
                case eDriverType.Reardriver:
                    driverType = "reardriver";
                    break;
                case eDriverType.Passenger:
                    driverType = "passenger";
                    break;
                default:
                    driverType = "nobody";
                    break;
            }

            entry +=
                $"node {vehicle.RangeMax} {vehicle.RangeMin} {vehicle.Name} dynamic " +
                $"{vehicle.DataFolder} {vehicle.SkinFile} {vehicle.MmdFile} " +
                $"{vehicle.Offset.ToString(CultureInfo.InvariantCulture)} " +
                $"{driverType} {vehicle.Coupling}{vehicle.WriteTrailing()} enddynamic\n";
        }

        return entry;
    }
}

public class Dynamic
{
    public float RangeMax = -1;
    public float RangeMin = 0f;
    public string Name;
    public string DataFolder;
    public string SkinFile;
    public string MmdFile;
    public string PathName;
    public float Offset;
    public eDriverType DriverType;

    public Coupling Coupling = new();

    public byte couplingData
    {
        get => (byte)(Coupling.AbsFlags & 0xFF);
        set => Coupling.Flags = value;
    }

    public string? MaxLoad;

    public bool HasVelocity;

    public float Velocity;

    public int LoadCount;

    public string? LoadType;

    public List<string> Destinations = new();

    public string? MiniName;

    public bool Flipped;

    internal void ReadTrailing(List<string> t)
    {
        int p = 0;

        if (p < t.Count && (t[p][0] == 'L' || t[p][0] == 'l')
            && t[p].Length > 1 && char.IsDigit(t[p][1]))
            MaxLoad = t[p++];

        if (p < t.Count && float.TryParse(t[p], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v))
        {
            Velocity = v;
            HasVelocity = true;
            p++;
        }

        if (p < t.Count && int.TryParse(t[p], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int lc))
        {
            LoadCount = lc;
            p++;
            if (lc > 0 && p < t.Count)
                LoadType = t[p++];
        }

        while (p < t.Count)
            Destinations.Add(t[p++]);
    }

    internal string WriteTrailing()
    {
        var sb = new StringBuilder();
        if (MaxLoad != null)
            sb.Append(' ').Append(MaxLoad);
        if (HasVelocity)
            sb.Append(' ').Append(Velocity.ToString(CultureInfo.InvariantCulture));
        if (LoadCount > 0)
        {
            sb.Append(' ').Append(LoadCount.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(LoadType))
                sb.Append(' ').Append(LoadType);
        }
        foreach (string d in Destinations)
            sb.Append(' ').Append(d);
        return sb.ToString();
    }

    public Dynamic Clone() => new Dynamic
    {
        RangeMax = RangeMax,
        RangeMin = RangeMin,
        Name = Name,
        DataFolder = DataFolder,
        SkinFile = SkinFile,
        MmdFile = MmdFile,
        PathName = PathName,
        Offset = Offset,
        DriverType = DriverType,
        Coupling = Coupling.Clone(),
        MaxLoad = MaxLoad,
        HasVelocity = HasVelocity,
        Velocity = Velocity,
        LoadCount = LoadCount,
        LoadType = LoadType,
        Destinations = new List<string>(Destinations),
        MiniName = MiniName,
        Flipped = Flipped
    };

    public string ToLooseNode()
    {
        string driver = DriverType switch
        {
            eDriverType.Headdriver => "headdriver",
            eDriverType.Reardriver => "reardriver",
            eDriverType.Passenger => "passenger",
            _ => "nobody"
        };
        string path = string.IsNullOrEmpty(PathName) ? "none" : PathName;
        string vel = (HasVelocity ? Velocity : 0f).ToString(CultureInfo.InvariantCulture);
        return
            $"node {RangeMax.ToString(CultureInfo.InvariantCulture)} " +
            $"{RangeMin.ToString(CultureInfo.InvariantCulture)} {Name} dynamic " +
            $"{DataFolder} {SkinFile} {MmdFile} {path} " +
            $"{Offset.ToString(CultureInfo.InvariantCulture)} {driver} {vel}" +
            $"{WriteTrailing()} enddynamic\n";
    }
}

public sealed class ConsistItem
{
    public List<Dynamic> Cars { get; set; } = new();
    public bool Grouped { get; set; }
    public bool Flipped { get; set; }
    public eDriverType Driver { get; set; } = eDriverType.Nobody;
}

public sealed class Coupling
{

    public const int Mechanical   = 1;
    public const int BrakePipe    = 2;
    public const int ControlMU    = 4;
    public const int HighVoltage  = 8;
    public const int Gangway      = 16;
    public const int AuxPneumatic = 32;
    public const int Heating      = 64;
    public const int WorkshopLock = 128;

    public int Flags;

    public List<string> Parameters = new();

    public int AbsFlags => Flags < 0 ? -Flags : Flags;

    public bool Locked
    {
        get => Flags < 0;
        set => Flags = value ? -AbsFlags : AbsFlags;
    }

    public bool Has(int bit) => (AbsFlags & bit) != 0;

    public void Set(int bit, bool on)
    {
        int abs = on ? (AbsFlags | bit) : (AbsFlags & ~bit);
        Flags = Locked ? -abs : abs;
    }

    public static Coupling Parse(string token)
    {
        var c = new Coupling();
        if (string.IsNullOrEmpty(token))
            return c;

        string[] parts = token.Split('.');
        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags);
        c.Flags = flags;
        for (int i = 1; i < parts.Length; i++)
            if (parts[i].Length > 0)
                c.Parameters.Add(parts[i]);
        return c;
    }

    public BrakeSetting? GetBrake() =>
        BrakeSetting.FromParameter(
            Parameters.FirstOrDefault(p => p.StartsWith("B", StringComparison.Ordinal)));

    public void SetBrake(BrakeSetting? brake)
    {
        Parameters.RemoveAll(p => p.StartsWith("B", StringComparison.Ordinal));
        if (brake != null)
            Parameters.Insert(0, brake.ToParameter());
    }

    public WheelSettings? GetWheels() =>
        WheelSettings.FromParameter(
            Parameters.FirstOrDefault(p => p.StartsWith("W", StringComparison.Ordinal)));

    public void SetWheels(WheelSettings? wheels)
    {
        Parameters.RemoveAll(p => p.StartsWith("W", StringComparison.Ordinal));
        if (wheels != null && !wheels.IsEmpty)
            Parameters.Add(wheels.ToParameter());
    }

    public bool ThermoDynamic
    {
        get => Parameters.Exists(p => p.Equals("TA", StringComparison.OrdinalIgnoreCase));
        set
        {
            Parameters.RemoveAll(p => p.Equals("TA", StringComparison.OrdinalIgnoreCase));
            if (value) Parameters.Add("TA");
        }
    }

    public Coupling Clone() => new()
    {
        Flags = Flags,
        Parameters = new List<string>(Parameters)
    };

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Flags.ToString(CultureInfo.InvariantCulture));
        foreach (string p in Parameters)
            sb.Append('.').Append(p);
        return sb.ToString();
    }
}

public sealed class BrakeSetting
{
    public static readonly string[] Modes = { "R+Mg", "G", "P", "R", "Q", "O", "A" };

    public string Mode = "P";

    public string? Load;

    public string? Switch;

    public static BrakeSetting? FromParameter(string? param)
    {
        if (string.IsNullOrEmpty(param) || param![0] != 'B' || param.Length < 2)
            return null;

        string body = param.Substring(1);
        string? mode = Modes.FirstOrDefault(m => body.StartsWith(m, StringComparison.Ordinal));
        if (mode is null)
            return null;

        var b = new BrakeSetting { Mode = mode };
        int p = mode.Length;
        if (p < body.Length && "THFA".IndexOf(body[p]) >= 0)
            b.Load = body[p++].ToString();
        if (p < body.Length && "01A".IndexOf(body[p]) >= 0)
            b.Switch = body[p].ToString();
        return b;
    }

    public string ToParameter() => "B" + Mode + (Load ?? "") + (Switch ?? "");
}

public sealed class WheelSettings
{
    public int Sway;

    public int Flatness;

    public int FlatnessRand;

    public int FlatnessProb;

    public bool IsEmpty => Sway <= 0 && Flatness <= 0 && FlatnessRand <= 0 && FlatnessProb <= 0;

    public static WheelSettings? FromParameter(string? param)
    {
        if (string.IsNullOrEmpty(param) || param![0] != 'W')
            return null;

        var w = new WheelSettings();
        string body = param.Substring(1);
        int i = 0;
        while (i < body.Length)
        {
            char code = body[i++];
            int start = i;
            while (i < body.Length && char.IsDigit(body[i])) i++;
            if (!int.TryParse(body[start..i], out int val)) continue;
            switch (char.ToUpperInvariant(code))
            {
                case 'H': w.Sway = val; break;
                case 'F': w.Flatness = val; break;
                case 'R': w.FlatnessRand = val; break;
                case 'P': w.FlatnessProb = val; break;
            }
        }
        return w;
    }

    public string ToParameter()
    {
        var sb = new StringBuilder("W");
        if (Sway > 0) sb.Append('H').Append(Sway);
        if (Flatness > 0) sb.Append('F').Append(Flatness);
        if (FlatnessRand > 0) sb.Append('R').Append(FlatnessRand);
        if (FlatnessProb > 0) sb.Append('P').Append(FlatnessProb);
        return sb.ToString();
    }
}
