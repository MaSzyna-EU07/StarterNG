using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Classes;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Domain;

public sealed class TexRandomizer
{
    private readonly VehicleCatalog _db;
    private readonly Random _rng;
    private readonly List<string[]> _stock = new();

    public int RevisionTolerance { get; set; } = -1;
    public int RevYear { get; set; } = -1;
    public bool WithoutArchival { get; set; } = true;

    public static string RulesPath
    {
        get
        {
            string cwd = Directory.GetCurrentDirectory();
            string primary = Path.Combine(cwd, "starter", "reguly.txt");
            if (File.Exists(primary)) return primary;
            string bundled = Path.Combine(AppContext.BaseDirectory, "startercfg", "reguly.txt");
            if (File.Exists(bundled)) return bundled;
            return primary;
        }
    }

    public TexRandomizer(VehicleCatalog db, Random? rng = null)
    {
        _db = db;
        _rng = rng ?? Random.Shared;
        LoadRules();
    }

    public void LoadRules()
    {
        _stock.Clear();
        string path = RulesPath;
        if (!File.Exists(path)) return;

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 0)
                    _stock.Add(parts);
            }
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log("texture randomizer stock list", ex);
        }
    }

    public int Apply(Consist consist, Func<VehicleTexture, Dynamic> makeCar)
    {
        int changed = 0;
        for (int i = 0; i < consist.Count; i++)
        {
            var item = consist[i];
            if (item.Cars.Count == 0) continue;

            var current = _db.TextureForSkin(item.Cars[0].SkinFile);
            if (current != null && _db.IsSetFollower(current))
                continue;

            string? mini = ResolveFilterMini(item, i, consist, current);
            if (string.IsNullOrEmpty(mini)) continue;

            int? revYear = null;
            if (RevisionTolerance > 0)
            {
                revYear = RevYear > 0 ? RevYear : ParseRevisionYear(current?.Meta?.RevisionDate);
                if (revYear is null) continue;
            }

            string? owner = null;
            string? cat = current?.ResolvedCategory;

            if (current != null && cat is not ("z" or "a"))
                owner = current.Meta?.Operator;

            var aliases = ExpandMini(mini);
            var pool = FindCandidates(aliases, owner, revYear, current);
            if (pool.Count < 2) continue;

            var pick = pool[_rng.Next(pool.Count)];
            if (ReferenceEquals(pick, current) ||
                string.Equals(pick.Skinfile, current?.Skinfile, StringComparison.OrdinalIgnoreCase))
                continue;

            var set = _db.ResolveSet(pick);
            var unit = set ?? new List<VehicleTexture> { pick };
            consist[i] = new ConsistItem
            {
                Cars = unit.Select(makeCar).ToList(),
                Grouped = unit.Count > 1,
                Driver = item.Driver,
                Flipped = item.Flipped
            };
            changed++;
        }
        return changed;
    }

    private string? ResolveFilterMini(ConsistItem item, int index, Consist consist, VehicleTexture? current)
    {
        if (current != null)
        {
            string skin = current.Skinfile ?? "";
            if (skin.Contains("112E", StringComparison.OrdinalIgnoreCase))
                return null;
            if (current.Wreck) return null;

            string mini = current.ResolvedClass;
            if (string.IsNullOrEmpty(mini))
                mini = _db.ResolveMiniName(current) ?? "";
            return string.IsNullOrEmpty(mini) ? null : mini;
        }

        if (index > 0)
        {
            var prev = _db.TextureForSkin(consist[index - 1].Cars[0].SkinFile);
            if (prev != null && !string.IsNullOrEmpty(prev.ResolvedClass))
                return prev.ResolvedClass;
        }
        if (index + 1 < consist.Count)
        {
            var next = _db.TextureForSkin(consist[index + 1].Cars[0].SkinFile);
            if (next != null && !string.IsNullOrEmpty(next.ResolvedClass))
                return next.ResolvedClass;
        }

        bool driven = item.Driver is eDriverType.Headdriver or eDriverType.Reardriver;
        return driven ? "st44" : "A_112A";
    }

    private List<string> ExpandMini(string mini)
    {
        var list = new List<string> { mini };
        string upper = mini.ToUpperInvariant();
        foreach (var stock in _stock)
        {
            bool hit = stock.Any(s => s.ToUpperInvariant().Contains(upper) || upper.Contains(s.ToUpperInvariant()));
            if (!hit) continue;
            foreach (string s in stock)
            {
                if (!list.Contains(s, StringComparer.OrdinalIgnoreCase))
                    list.Add(s);
            }
            break;
        }
        return list;
    }

    private List<VehicleTexture> FindCandidates(
        IReadOnlyList<string> minis,
        string? owner,
        int? revYear,
        VehicleTexture? current)
    {
        var result = new List<VehicleTexture>();
        foreach (var t in _db.Textures)
        {
            if (t.Wreck) continue;
            if (WithoutArchival && t.ResolvedArchived) continue;
            if (_db.IsSetFollower(t)) continue;

            string candidateMini = t.ResolvedClass;
            if (string.IsNullOrEmpty(candidateMini))
                candidateMini = _db.ResolveMiniName(t) ?? "";
            if (string.IsNullOrEmpty(candidateMini)) continue;

            bool miniOk = minis.Any(m =>
                string.Equals(m, candidateMini, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m, t.TextureMini, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m, t.MiniRef, StringComparison.OrdinalIgnoreCase));
            if (!miniOk) continue;

            if (!string.IsNullOrEmpty(owner) &&
                !string.Equals(owner, t.Meta?.Operator, StringComparison.OrdinalIgnoreCase))
                continue;

            if (revYear is int year && RevisionTolerance > 0)
            {
                int? y = ParseRevisionYear(t.Meta?.RevisionDate);
                if (y is null || Math.Abs(y.Value - year) > RevisionTolerance)
                    continue;
            }

            result.Add(t);
        }

        if (current != null && !result.Contains(current))
            result.Add(current);

        return result;
    }

    public static int? ParseRevisionYear(string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision)) return null;

        if (revision.Length >= 10 && revision[2] == '.' && revision[5] == '.' &&
            int.TryParse(revision.AsSpan(6, 4), out int yDot) && yDot is >= 1900 and <= 2100)
            return yDot;

        for (int i = 0; i + 3 < revision.Length; i++)
        {
            if (char.IsDigit(revision[i]) && char.IsDigit(revision[i + 1]) &&
                char.IsDigit(revision[i + 2]) && char.IsDigit(revision[i + 3]))
            {
                int y = (revision[i] - '0') * 1000 + (revision[i + 1] - '0') * 100
                      + (revision[i + 2] - '0') * 10 + (revision[i + 3] - '0');
                if (y is >= 1900 and <= 2100) return y;
            }
        }
        return null;
    }
}
