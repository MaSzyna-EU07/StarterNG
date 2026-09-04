using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Classes;
using StarterNG.Infrastructure;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Domain;

public static class UDepot
{
    public sealed class DepotTrain
    {
        public string TrainName = "";
        public List<Dynamic> Vehicles = new();
    }

    public static string? FindDepotFile(string? path = null)
    {
        if (!string.IsNullOrEmpty(path))
            return File.Exists(path) ? path : null;

        string root = Directory.GetCurrentDirectory();
        foreach (string rel in new[] { Path.Combine("starter", "magazyn.ini"), "starter.ini", "RAINSTED.INI" })
        {
            string full = Path.Combine(root, rel);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public static List<DepotTrain> ReadDepot(string? path = null)
    {
        var result = new List<DepotTrain>();
        string? file = FindDepotFile(path);
        if (file == null) return result;

        string text;
        try
        {
            text = File.ReadAllText(file, LegacyText.CodePage1250);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Błąd parsowania magazynu {file}", ex);
            return result;
        }

        DepotTrain? current = null;
        int line = 0;

        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            line++;
            string s = raw.Trim();
            if (s.Length == 0) continue;

            var header = Regex.Match(s, @"^\[\s*TRAINSET\d+\s*(?:=\s*([^\]]*))?\]", RegexOptions.IgnoreCase);
            if (header.Success)
            {
                current = new DepotTrain { TrainName = header.Groups[1].Value.Trim() };
                result.Add(current);
                continue;
            }

            if (current == null) continue;

            var entry = Regex.Match(s, @"^\d+\s*=\s*(.*)$");
            string body = entry.Success ? entry.Groups[1].Value.Trim() : s;
            if (!body.StartsWith("node", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var v = ParseNode(body);
                if (v != null) current.Vehicles.Add(v);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Błąd parsowania magazynu. Linia: {line}", ex);
            }
        }

        result.RemoveAll(t => t.Vehicles.Count == 0);
        return result;
    }

    private static Dynamic? ParseNode(string body)
    {
        var t = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 10) return null;

        var inv = CultureInfo.InvariantCulture;
        int p = 1;

        var d = new Dynamic
        {
            RangeMax = float.Parse(t[p++], inv),
            RangeMin = float.Parse(t[p++], inv),
            Name = t[p++]
        };
        p++;
        d.DataFolder = t[p++];
        d.SkinFile = Dynamic.StripSkinExtension(t[p++]);
        d.MmdFile = t[p++];
        d.Offset = float.Parse(t[p++], inv);
        d.DriverType = t[p++].ToLowerInvariant() switch
        {
            "headdriver" => eDriverType.Headdriver,
            "reardriver" => eDriverType.Reardriver,
            "passenger" => eDriverType.Passenger,
            _ => eDriverType.Nobody
        };
        d.Coupling = Coupling.Parse(t[p++]);

        var trailing = new List<string>();
        while (p < t.Length && !string.Equals(t[p], "enddynamic", StringComparison.OrdinalIgnoreCase))
            trailing.Add(t[p++]);
        d.ReadTrailing(trailing);
        return d;
    }

    public static bool SaveDepot(IReadOnlyList<DepotTrain> depot, string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "starter", "magazyn.ini");
        try
        {
            var sb = new StringBuilder();
            for (int i = 0; i < depot.Count; i++)
            {
                if (depot[i].Vehicles.Count == 0) continue;

                sb.Append('[').Append("TRAINSET").Append(i).Append('=')
                  .Append(depot[i].TrainName).Append("]\n");

                for (int y = 0; y < depot[i].Vehicles.Count; y++)
                    sb.Append(y.ToString("00", CultureInfo.InvariantCulture)).Append('=')
                      .Append(depot[i].Vehicles[y].ToTrainsetNode());
            }

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString(), LegacyText.CodePage1250);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Błąd zapisu magazynu", ex);
            return false;
        }
    }
}
