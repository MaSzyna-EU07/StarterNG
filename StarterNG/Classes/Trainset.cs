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

    /// <summary>Original .scn text of this trainset block (incl. endtrainset).</summary>
    public string RawEntry;

    /// <summary>True if the block parsed cleanly; false blocks are exported verbatim.</summary>
    public bool Parsed;

    /// <summary>True when the block carries the //$decor flag (decoration, not drivable).</summary>
    public bool Decor;

    /// <summary>Loading-screen logo key from <c>//$il</c>.</summary>
    public string? Logo;

    /// <summary>Optional trainset mini from <c>//$it</c>.</summary>
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

        // handle specific descriptions
        // //$o
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
        
        // parse trainset
        
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "endtrainset")
                break;
            
            // trainset properties
            if (tokens[i] == "trainset")
            {
                this.Name = tokens[++i];
                this.Track = tokens[++i];
                this.Offset = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                this.Velocity = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                this.OriginalVelocity = this.Velocity;
                continue;
            }
            
            // skip entire assignments block
            if (tokens[i] == "assignments")
            {
                while (i < tokens.Count && tokens[i] != "endassignment")
                    i++;
                i++; // jump over endassignment
                continue;
            }
            
            // load vehicles
            if (tokens[i] == "node")
            {
                Dynamic nodeDynamic = new Dynamic();
                nodeDynamic.RangeMax = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.RangeMin = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.Name = tokens[++i];
                i++; // dynamic keyword
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

                // couplingdata: numeric bit-mask plus optional ".B/.W/.T" parameter
                // codes (brake / wheel / thermo settings) kept verbatim.
                nodeDynamic.Coupling = Coupling.Parse(tokens[++i]);

                // Everything before enddynamic is the optional, positional tail
                // documented for node::dynamic:
                //   [Lx] velocity [loadcount [loadtype]] [destination [destination]]
                var trailing = new List<string>();
                while (i + 1 < tokens.Count && tokens[i + 1] != "enddynamic")
                    trailing.Add(tokens[++i]);
                nodeDynamic.ReadTrailing(trailing);

                i++; // jump over enddynamic

                Vehicles.Add(nodeDynamic);
            }
        }
    }
    
    /// <summary>
    /// The .scn text for this trainset for export. Unparsed blocks are written
    /// back verbatim; parsed blocks are regenerated from the current vehicles.
    /// </summary>
    public string ToSceneryEntry() =>
        Parsed ? GetTrainsetEntry() + "endtrainset\n" : RawEntry;

    public string GetTrainsetEntry()
    {
        // Starter metadata comments (parsed for filters/UI). Pascal's launch rewrite
        // omits them from $scn; we keep them so a regenerated block stays filterable
        // if the template is re-opened or compared.
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

    /// <summary>
    /// Coupling with the next vehicle (the <c>couplingdata</c> field): the bit-mask
    /// plus any ".B/.W/.T" parameter codes (brake / wheel / thermo settings).
    /// </summary>
    public Coupling Coupling = new();

    /// <summary>
    /// Low byte of the coupling mask, exposed for the depot's per-bit editor.
    /// Backed by <see cref="Coupling"/> so the two never diverge.
    /// </summary>
    public byte couplingData
    {
        get => (byte)(Coupling.AbsFlags & 0xFF);
        set => Coupling.Flags = value;
    }

    // --- optional node::dynamic trailing parameters (all positional, all optional) ---

    /// <summary>"Lx" MaxLoad override token (e.g. "L0"), or null when absent.</summary>
    public string? MaxLoad;

    /// <summary>Whether a per-vehicle velocity token is present / should be written.</summary>
    public bool HasVelocity;

    /// <summary>Per-vehicle starting velocity.</summary>
    public float Velocity;

    /// <summary>Cargo amount (loadcount).</summary>
    public int LoadCount;

    /// <summary>Cargo type (loadtype) — only meaningful when <see cref="LoadCount"/> &gt; 0.</summary>
    public string? LoadType;

    /// <summary>Optional cargo destination tokens (rarely used; preserved verbatim).</summary>
    public List<string> Destinations = new();

    // --- depot / consist-builder extras (not part of the .scn syntax) ---

    /// <summary>Explicit miniature name (from the vehicle database). Falls back to SkinFile.</summary>
    public string? MiniName;

    /// <summary>Whether the vehicle is visually reversed in the consist.</summary>
    public bool Flipped;

    /// <summary>
    /// Parses the optional positional parameters that follow couplingdata:
    /// <c>[Lx] velocity [loadcount [loadtype]] [destination [destination]]</c>.
    /// See https://wiki.eu07.pl/index.php?title=Obiekt_node::dynamic
    /// </summary>
    internal void ReadTrailing(List<string> t)
    {
        int p = 0;

        // Lx — MaxLoad override (e.g. "L0"); only when it genuinely looks like one.
        if (p < t.Count && (t[p][0] == 'L' || t[p][0] == 'l')
            && t[p].Length > 1 && char.IsDigit(t[p][1]))
            MaxLoad = t[p++];

        // velocity — the first plain number after the coupling
        if (p < t.Count && float.TryParse(t[p], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v))
        {
            Velocity = v;
            HasVelocity = true;
            p++;
        }

        // loadcount [loadtype] — loadtype is present only when loadcount > 0
        if (p < t.Count && int.TryParse(t[p], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int lc))
        {
            LoadCount = lc;
            p++;
            if (lc > 0 && p < t.Count)
                LoadType = t[p++];
        }

        // remaining tokens: optional destinations, kept verbatim
        while (p < t.Count)
            Destinations.Add(t[p++]);
    }

    /// <summary>
    /// Regenerates the trailing parameter string (each present token prefixed with
    /// a space), the inverse of <see cref="ReadTrailing"/>. A loadcount of 0 is
    /// omitted, matching the canonical trainset entries the simulator expects.
    /// </summary>
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

    /// <summary>Deep copy, used when importing a trainset into the editable consist.</summary>
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

    /// <summary>Pascal <c>PrepareNode(..., TrainSet=False)</c>.</summary>
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

/// <summary>One entry in the depot consist: a single vehicle or a locked multi-car unit.</summary>
public sealed class ConsistItem
{
    public List<Dynamic> Cars { get; set; } = new();
    public bool Grouped { get; set; }
    public bool Flipped { get; set; }
    public eDriverType Driver { get; set; } = eDriverType.Nobody;
}

/// <summary>
/// The <c>couplingdata</c> value joining a vehicle to the next one: a bit-mask of
/// the active coupling types, optionally followed by ".B/.W/.T" parameter codes.
/// A negative mask means the coupling is locked (cannot be uncoupled in-sim); the
/// simulator takes its absolute value before testing the individual bits.
/// See https://wiki.eu07.pl/index.php?title=Wpisy_hamulca_dla_pojazdow
/// </summary>
public sealed class Coupling
{
    // Mask bit values, per the wiki.
    public const int Mechanical   = 1;    // mechanical link
    public const int BrakePipe    = 2;    // pneumatic 5 atm (brakes)
    public const int ControlMU    = 4;    // multiple-unit control
    public const int HighVoltage  = 8;    // high voltage
    public const int Gangway      = 16;   // passage between vehicles
    public const int AuxPneumatic = 32;   // auxiliary pneumatic 8 atm
    public const int Heating      = 64;   // heating
    public const int WorkshopLock = 128;  // workshop coupling (uncouple lock)

    /// <summary>Signed mask exactly as written; negative = locked.</summary>
    public int Flags;

    /// <summary>
    /// Textual parameter codes that follow the mask after a '.', in order
    /// (e.g. "BR", "WH25F5"). Preserved verbatim so brake / wheel / thermo
    /// settings survive a round-trip even when the launcher does not edit them.
    /// </summary>
    public List<string> Parameters = new();

    /// <summary>Mask with the lock sign removed (what the simulator tests bits on).</summary>
    public int AbsFlags => Flags < 0 ? -Flags : Flags;

    /// <summary>True when the coupling is marked permanent (negative mask).</summary>
    public bool Locked
    {
        get => Flags < 0;
        set => Flags = value ? -AbsFlags : AbsFlags;
    }

    public bool Has(int bit) => (AbsFlags & bit) != 0;

    /// <summary>Sets or clears a mask bit, preserving the lock sign.</summary>
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

    /// <summary>The brake-rack setting carried in the "B" parameter, or null.</summary>
    public BrakeSetting? GetBrake() =>
        BrakeSetting.FromParameter(
            Parameters.FirstOrDefault(p => p.StartsWith("B", StringComparison.Ordinal)));

    /// <summary>Replaces (or clears, when null) the "B" brake parameter.</summary>
    public void SetBrake(BrakeSetting? brake)
    {
        Parameters.RemoveAll(p => p.StartsWith("B", StringComparison.Ordinal));
        if (brake != null)
            Parameters.Insert(0, brake.ToParameter());
    }

    /// <summary>The wheel/damage setting carried in the "W" parameter, or null.</summary>
    public WheelSettings? GetWheels() =>
        WheelSettings.FromParameter(
            Parameters.FirstOrDefault(p => p.StartsWith("W", StringComparison.Ordinal)));

    /// <summary>Replaces (or clears, when empty/null) the "W" wheel parameter.</summary>
    public void SetWheels(WheelSettings? wheels)
    {
        Parameters.RemoveAll(p => p.StartsWith("W", StringComparison.Ordinal));
        if (wheels != null && !wheels.IsEmpty)
            Parameters.Add(wheels.ToParameter());
    }

    /// <summary>Coolant at ambient temperature (<c>.TA</c>), like Pascal ThermoDynamic.</summary>
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

    /// <summary>Serialises back to "&lt;flags&gt;[.param[.param…]]".</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Flags.ToString(CultureInfo.InvariantCulture));
        foreach (string p in Parameters)
            sb.Append('.').Append(p);
        return sb.ToString();
    }
}

/// <summary>
/// A vehicle's brake-rack setting, carried in the "B" parameter of its coupling
/// field: <c>B&lt;mode&gt;[&lt;load&gt;][&lt;switch&gt;]</c> (e.g. "BP", "BRA", "BG1").
/// See https://wiki.eu07.pl/index.php?title=Wpisy_hamulca_dla_pojazdow
/// </summary>
public sealed class BrakeSetting
{
    /// <summary>Brake modes, longest first so "R+Mg" wins over "R" while parsing.</summary>
    public static readonly string[] Modes = { "R+Mg", "G", "P", "R", "Q", "O", "A" };

    /// <summary>Brake mode: G (freight), P (passenger), R (express), R+Mg, Q, O (off), A (auto).</summary>
    public string Mode = "P";

    /// <summary>Load adaptation: T (empty), H (medium), F (loaded), A (auto), or null.</summary>
    public string? Load;

    /// <summary>Brake on/off switch: 0 (off), 1 (off ~10%), A (on), or null.</summary>
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

/// <summary>
/// Wheel-damage settings carried in the "W" parameter of a vehicle's coupling
/// field: <c>W[H&lt;sway%&gt;][F&lt;flat mm&gt;][R&lt;random flat mm&gt;][P&lt;flat prob%&gt;]</c>
/// (e.g. "WH25F5R10P8"). See https://wiki.eu07.pl/index.php?title=Wpisy_hamulca_dla_pojazdow
/// </summary>
public sealed class WheelSettings
{
    /// <summary>H — sway ("wężykowanie") probability, %.</summary>
    public int Sway;

    /// <summary>F — flat spot ("podkucie") of this size, mm.</summary>
    public int Flatness;

    /// <summary>R — additional random flat spot, 0..x mm.</summary>
    public int FlatnessRand;

    /// <summary>P — flat-spot probability, % (guaranteed when omitted).</summary>
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