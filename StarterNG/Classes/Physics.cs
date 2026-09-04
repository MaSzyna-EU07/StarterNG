using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StarterNG.Classes;

public sealed class Physics
{
    public double Mass;
    public double VMax;
    public double Length;
    public string LoadAccepted = "";
    public string LoadQ = "";
    public int MaxLoad;
    public int AllowedFlagA = 3;
    public int AllowedFlagB = 3;
    public string ControlTypeA = "";
    public string ControlTypeB = "";

    public string EngineType = "";

    /// <summary>
    /// True for the engine types the simulator actually runs its diesel heat model for.
    /// Mover.cpp calls dizel_Update - and through it dizel_Heat - only for DieselEngine
    /// and DieselElectric; LoadFIZ_EngineDecode maps the legacy "DumbDE" onto
    /// DieselElectric. On anything else the coolant temperature struct is written and
    /// never read.
    /// </summary>
    public bool IsDieselEngine =>
        EngineType.Equals("DieselEngine", StringComparison.OrdinalIgnoreCase)
        || EngineType.Equals("DieselElectric", StringComparison.OrdinalIgnoreCase)
        || EngineType.Equals("DumbDE", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Physics?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string>? _fizIndex;

    public static void PreloadIndex(string dynamicRoot = "dynamic") => FizIndex(dynamicRoot);

    public static int IndexedCount => _fizIndex?.Count ?? 0;

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
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log($"indexing {dynamicRoot}", ex);
        }

        _fizIndex = index;
        return index;
    }

    public static Physics? For(string? dataFolder, string? model, string dynamicRoot = "dynamic")
    {
        if (string.IsNullOrEmpty(model))
            return null;

        string m = Path.GetFileNameWithoutExtension(model!);
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
            phys = new Physics { AllowedFlagB = -1 };
            try { Parse(phys, path, Path.GetDirectoryName(path) ?? "", null, 0); }
            catch (Exception ex) { StarterNG.Infrastructure.Diagnostics.Log($"physics/{Path.GetFileName(path)}", ex); }

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
        "M", "Vmax", "L", "LoadAccepted", "LoadQ", "MaxLoad", "AllowedFlag", "ControlType",
        "EngineType"
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
                pendingKey = t;
            }
            else
            {

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
                else if (key.Equals("LoadQ", StringComparison.OrdinalIgnoreCase)) p.LoadQ = value;
                else if (key.Equals("MaxLoad", StringComparison.OrdinalIgnoreCase)) p.MaxLoad = (int)ParseD(value, p.MaxLoad);
                break;
            case "engine":
                if (key.Equals("EngineType", StringComparison.OrdinalIgnoreCase)) p.EngineType = value;
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
