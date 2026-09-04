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

    // The scenery format has no battery field: a trainset with a non-zero speed is
    // the prepared one. DynObj.cpp derives ReadyFlag from it, and that branch of
    // CheckLocomotiveParameters is what charges the pipes, sets the brake handle
    // and closes the battery contactor. The sign carries the direction, so a
    // consist authored on the move keeps its own speed.
    private const float StandingStill = 0.01f;
    private const float PreparedCreep = 0.1f;

    public bool ReadyToGo
    {
        get => MathF.Abs(Velocity) >= StandingStill;
        set
        {
            if (!value)
            {
                Velocity = 0f;
                return;
            }

            Velocity = MathF.Abs(OriginalVelocity) < StandingStill ? PreparedCreep : OriginalVelocity;
        }
    }
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
                nodeDynamic.SkinFile = Dynamic.StripSkinExtension(tokens[++i]);
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
            entry += vehicle.ToTrainsetNode();

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

    public const string PantState = "pantstate";

    public const int PantStateMax = 3;

    /// <summary>
    /// Lives in the coupling token as ".L&lt;n&gt;" - that is where the simulator reads
    /// it from (DynObj.cpp, the MoreParams loop). The older space-separated "L&lt;n&gt;"
    /// token in front of the load count is still read for legacy files, but never
    /// written back: the current scenario parser reads that slot as the load count.
    /// </summary>
    public int MaxLoad
    {
        get => Coupling.MaxLoad;
        set => Coupling.MaxLoad = value;
    }

    public bool HasVelocity;

    public float Velocity;

    public int LoadCount;

    public string? LoadType;

    public string? MiniName;

    public bool Flipped;

    public static bool IsPantStateType(string? type) =>
        string.Equals(type, PantState, StringComparison.OrdinalIgnoreCase);

    public bool IsPantState => IsPantStateType(LoadType);

    internal void ReadTrailing(List<string> t)
    {
        int p = 0;

        // Legacy form: max load as its own token in front of the load count. The
        // current scenario parser reads that slot as an int, so this is read only -
        // the setter moves the value into the coupling token, where it is written.
        if (p < t.Count && t[p].Length > 0 && t[p][0] == 'L')
        {
            if (int.TryParse(t[p].AsSpan(1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int ml))
                MaxLoad = ml;
            p++;
        }

        if (p < t.Count && int.TryParse(t[p], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int lc))
        {
            LoadCount = lc;
            p++;
            if (p < t.Count && (lc > 0 || IsPantStateType(t[p])))
                LoadType = t[p++];
        }

    }

    internal static string StripSkinExtension(string token)
    {
        int dot = token.LastIndexOf('.');
        return dot > 0 ? token[..dot] : token;
    }

    internal string WriteTrailing()
    {
        var sb = new StringBuilder();

        bool loaded = !string.IsNullOrEmpty(LoadType);
        sb.Append(' ').Append((loaded ? LoadCount : 0).ToString(CultureInfo.InvariantCulture));

        if (loaded && (LoadCount > 0 || IsPantState))
            sb.Append(' ').Append(LoadType);

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
        HasVelocity = HasVelocity,
        Velocity = Velocity,
        LoadCount = LoadCount,
        LoadType = LoadType,
        MiniName = MiniName,
        Flipped = Flipped
    };

    public string ToTrainsetNode()
    {
        string driver = DriverType switch
        {
            eDriverType.Headdriver => "headdriver",
            eDriverType.Reardriver => "reardriver",
            eDriverType.Passenger  => "passenger",
            _                      => "nobody"
        };

        return
            $"node {RangeMax} {RangeMin} {Name} dynamic " +
            $"{DataFolder} {SkinFile} {MmdFile} " +
            $"{Offset.ToString(CultureInfo.InvariantCulture)} " +
            $"{driver} {Coupling}{WriteTrailing()} enddynamic\n";
    }

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
        if (brake is { IsEmpty: false })
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

    /// <summary>
    /// Max load override, the ".L&lt;n&gt;" sub-parameter. -1 means "not set", so the
    /// vehicle keeps the MaxLoad from its .fiz.
    /// </summary>
    public int MaxLoad
    {
        get => Parameters.Find(IsMaxLoad) is { } p
            && int.TryParse(p.AsSpan(1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int v) ? v : -1;
        set
        {
            Parameters.RemoveAll(IsMaxLoad);
            if (value >= 0)
                Parameters.Add("L" + value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool IsMaxLoad(string param) =>
        param.Length > 1
        && char.ToUpperInvariant(param[0]) == 'L'
        && param.Skip(1).All(char.IsDigit);

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
    public static readonly string[] Modes = { "G", "P", "R", "M" };

    public static readonly string[] Loads = { "T", "H", "F" };

    // The simulator tests every letter of the B parameter independently, so the
    // "off" states are a separate axis from the G/P/R/M delay position. They stay
    // one combo because they contradict each other, but they no longer block a
    // delay position from being picked alongside them (e.g. "BRQ").
    public static readonly string[] Switches = { "<>", "0", "1", "X", "E", "Q" };

    public string? Mode;

    public string? Load;

    public string? Switch;

    // A brake switch or a load adaptation with no delay position is still a
    // parameter worth writing, so all three axes count towards emptiness.
    public bool IsEmpty => Mode is null && Load is null && Switch is null;

    public static BrakeSetting? FromParameter(string? param)
    {
        if (string.IsNullOrEmpty(param) || char.ToUpperInvariant(param![0]) != 'B')
            return null;

        string body = param.Substring(1).ToUpperInvariant();
        var b = new BrakeSetting
        {
            // "M" first: the simulator reads it as R plus the magnetic rail brake,
            // so a bare "R" must not swallow it. Switches are matched in the
            // declared order too, so "<>" wins over the digits it cannot contain.
            Mode = First(body, "M", "G", "P", "R"),
            Load = First(body, Loads),
            Switch = First(body, Switches)
        };
        return b.Mode is null && b.Load is null && b.Switch is null ? null : b;

        static string? First(string body, params string[] codes) =>
            Array.Find(codes, c => body.Contains(c, StringComparison.Ordinal));
    }

    public string ToParameter() => "B" + Mode + Switch + Load;
}

public sealed class WheelSettings
{
    public int Sway;

    public int Flatness;

    public int FlatnessRand;

    /// <summary>
    /// Chance (%) that the flat spot is applied at all. The simulator defaults it
    /// to 100 when the P sub-parameter is missing, so that is the default here too
    /// - otherwise "0" would be written as "absent" and mean the exact opposite.
    /// </summary>
    public int FlatnessProb = DefaultFlatnessProb;

    public const int DefaultFlatnessProb = 100;

    // A probability on its own does nothing: without a flat size there is nothing
    // to roll for.
    public bool IsEmpty => Sway <= 0 && Flatness <= 0 && FlatnessRand <= 0;

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
        if (FlatnessProb != DefaultFlatnessProb) sb.Append('P').Append(FlatnessProb);
        return sb.ToString();
    }
}
