using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StarterNG.Classes;

/// <summary>
/// Vehicle physics read from a .fiz file (dynamic/&lt;dir&gt;/&lt;model&gt;.fiz), mirroring
/// the original Starter's TLexParser.ParsePhysics. Provides mass, top speed,
/// length, accepted loads and the coupler AllowedFlag / ControlType used for the
/// automatic coupling calculation.
/// </summary>
public sealed class Physics
{
    public double Mass;        // kg, as written in the .fiz
    public double VMax;        // km/h
    public double Length;      // metres
    public string LoadAccepted = "";
    public int MaxLoad;
    public int AllowedFlagA = 3;
    public int AllowedFlagB = 3;
    public string ControlTypeA = "";
    public string ControlTypeB = "";

    private static readonly Dictionary<string, Physics?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Index of every .fiz under dynamic/, keyed by its file name without extension
    // (e.g. "en57", "en57dumb"). Built once - the directory layout of the data
    // folder vs the scenery's mmd token is unreliable, so we resolve by model name.
    private static Dictionary<string, string>? _fizIndex;

    /// <summary>Builds the .fiz index up front (call from the startup load).</summary>
    public static void PreloadIndex(string dynamicRoot = "dynamic") => FizIndex(dynamicRoot);

    private static Dictionary<string, string> FizIndex(string dynamicRoot)
    {
        if (_fizIndex != null)
            return _fizIndex;

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(dynamicRoot))
                foreach (string f in Directory.EnumerateFiles(dynamicRoot, "*.fiz", SearchOption.AllDirectories))
                    index[Path.GetFileNameWithoutExtension(f)] = f;
        }
        catch { /* unreadable dynamic/ - no physics */ }

        _fizIndex = index;
        return index;
    }

    /// <summary>
    /// Loads (and caches) the physics for a model name (the .fiz is located by name
    /// anywhere under dynamic/). The <paramref name="dataFolder"/> is unused now but
    /// kept for call-site clarity. Returns null when nothing matches. Never throws.
    /// </summary>
    public static Physics? For(string? dataFolder, string? model, string dynamicRoot = "dynamic")
    {
        if (string.IsNullOrEmpty(model))
            return null;

        string m = Path.GetFileNameWithoutExtension(model!); // strip any .mmd/.t3d/etc.
        if (m.Length == 0)
            return null;

        if (Cache.TryGetValue(m, out var cached))
            return cached;

        var index = FizIndex(dynamicRoot);
        string? path = null;
        if (index.TryGetValue(m, out var p1)) path = p1;
        else if (index.TryGetValue(m + "dumb", out var p2)) path = p2;

        Physics? phys = null;
        if (path != null)
        {
            phys = new Physics { AllowedFlagB = -1 }; // -1 = "B end not declared yet"
            try { Parse(phys, path, Path.GetDirectoryName(path) ?? "", null, 0); }
            catch { /* partial data still useful */ }

            // The original copies the A-end coupler onto the B-end when only one
            // BuffCoupl section is declared.
            if (phys.AllowedFlagB == -1)
            {
                phys.AllowedFlagB = phys.AllowedFlagA;
                if (string.IsNullOrEmpty(phys.ControlTypeB))
                    phys.ControlTypeB = phys.ControlTypeA;
            }
        }

        Cache[m] = phys;
        return phys;
    }

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "M", "Vmax", "L", "LoadAccepted", "MaxLoad", "AllowedFlag", "ControlType"
    };

    private static void Parse(Physics p, string path, string dir, string[]? prms, int depth)
    {
        if (depth > 6 || !File.Exists(path))
            return;

        var tokens = Tokenize(File.ReadAllText(path, Encoding.GetEncoding(1250)));

        string section = "";
        string? pendingKey = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            string tok = tokens[i];
            if (tok == "=")
                continue;

            // include <file> <params...> end  -> parse the included physics
            if (tok.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= tokens.Count) break;
                string incFile = Resolve(tokens[i], prms);
                var incParams = new List<string>();
                i++;
                while (i < tokens.Count && !tokens[i].Equals("end", StringComparison.OrdinalIgnoreCase))
                    incParams.Add(Resolve(tokens[i++], prms));
                Parse(p, Path.Combine(dir, incFile), dir, incParams.ToArray(), depth + 1);
                pendingKey = null;
                continue;
            }

            int eq = tok.IndexOf('=');
            if (eq >= 0)
            {
                string k = tok[..eq].TrimStart('.').Trim();
                string v = tok[(eq + 1)..].Trim();
                if (v.Length == 0) { pendingKey = k; continue; }
                Apply(p, section, k, Resolve(v, prms));
                pendingKey = null;
                continue;
            }

            string t = tok.TrimStart('.').Trim();
            if (pendingKey != null)
            {
                Apply(p, section, pendingKey, Resolve(t, prms));
                pendingKey = null;
            }
            else if (KnownKeys.Contains(t))
            {
                pendingKey = t; // value comes in a following token (spaced "Key = value")
            }
            else
            {
                // any other bare identifier is a section header (Param, Load,
                // Dimensions, BuffCoupl/1/2, or one we don't read). Keep only
                // letters/digits so "Param:" or "Param," still match.
                section = new string(t.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            }
        }
    }

    private static void Apply(Physics p, string section, string key, string value)
    {
        switch (section)
        {
            case "param":
                if (key.Equals("M", StringComparison.OrdinalIgnoreCase)) p.Mass = ParseD(value, p.Mass);
                else if (key.Equals("Vmax", StringComparison.OrdinalIgnoreCase)) p.VMax = ParseD(value, p.VMax);
                break;
            case "load":
                if (key.Equals("LoadAccepted", StringComparison.OrdinalIgnoreCase)) p.LoadAccepted = value;
                else if (key.Equals("MaxLoad", StringComparison.OrdinalIgnoreCase)) p.MaxLoad = (int)ParseD(value, p.MaxLoad);
                break;
            case "dimensions":
                if (key.Equals("L", StringComparison.OrdinalIgnoreCase)) p.Length = ParseD(value, p.Length);
                break;
            case "buffcoupl":
            case "buffcoupl1":
                if (key.Equals("AllowedFlag", StringComparison.OrdinalIgnoreCase)) p.AllowedFlagA = Flag(value, p.AllowedFlagA);
                else if (key.Equals("ControlType", StringComparison.OrdinalIgnoreCase)) p.ControlTypeA = value;
                break;
            case "buffcoupl2":
                if (key.Equals("AllowedFlag", StringComparison.OrdinalIgnoreCase)) p.AllowedFlagB = Flag(value, p.AllowedFlagB);
                else if (key.Equals("ControlType", StringComparison.OrdinalIgnoreCase)) p.ControlTypeB = value;
                break;
        }
    }

    // A negative AllowedFlag means a permanent coupling: abs + 128 (workshop-lock bit).
    private static int Flag(string v, int fallback)
        => int.TryParse(v, System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture, out int f)
            ? (f < 0 ? -f + 128 : f)
            : fallback;

    private static double ParseD(string v, double fallback)
        => double.TryParse(v, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out double d)
            ? d
            : fallback;

    // Substitutes an include placeholder like "(c1)" with the matching include
    // parameter (1-based), otherwise returns the token unchanged.
    private static string Resolve(string token, string[]? prms)
    {
        if (prms == null || token.Length < 3 || token[0] != '(' || token[^1] != ')')
            return token;
        string digits = new string(token.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int idx) && idx >= 1 && idx <= prms.Length
            ? prms[idx - 1]
            : token;
    }

    private static List<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            string l = line;
            int hash = l.IndexOf('#');
            if (hash >= 0) l = l[..hash];
            sb.Append(l).Append(' ');
        }
        return sb.ToString()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
