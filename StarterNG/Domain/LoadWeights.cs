using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using StarterNG.Classes;

namespace StarterNG.Domain;

public static class LoadWeights
{
    private static Dictionary<string, int>? _weights;

    public static int WeightOf(string? name)
    {
        Load();
        return !string.IsNullOrEmpty(name) && _weights!.TryGetValue(name!, out int w) ? w : 1000;
    }

    public static string Describe(string name) =>
        string.Equals(name, Dynamic.PantState, StringComparison.OrdinalIgnoreCase)
            ? App.Loc["LoadPantState"]
            : name;

    private static void Load()
    {
        if (_weights != null)
            return;

        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Dynamic.PantState] = 0
        };
        try
        {
            string path = Path.Combine("data", "load_weights.txt");
            if (File.Exists(path))
            {
                var body = new StringBuilder();
                foreach (var raw in File.ReadAllLines(path, Encoding.GetEncoding(1250)))
                {
                    string line = raw;
                    int comment = line.IndexOfAny(new[] { '#', ';' });
                    if (comment >= 0) line = line[..comment];
                    int slashes = line.IndexOf("//", StringComparison.Ordinal);
                    if (slashes >= 0) line = line[..slashes];
                    body.Append(line).Append(' ');
                }

                var tokens = body.Replace(":", " : ").ToString()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 1; i + 1 < tokens.Length; i++)
                {
                    if (tokens[i] != ":") continue;
                    string name = tokens[i - 1];
                    if (name is "{" or "}") continue;
                    if (!int.TryParse(tokens[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int w))
                        continue;
                    weights[name] = w;
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log("data/load_weights.txt", ex);
        }

        _weights = weights;
    }
}
