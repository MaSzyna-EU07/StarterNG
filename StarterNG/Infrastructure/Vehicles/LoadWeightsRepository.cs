using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Vehicles;

/// <summary>
/// Reads data/load_weights.txt into a <see cref="LoadWeightsTable"/>.
/// </summary>
/// <remarks>
/// The file is a brace-delimited list of "name : weight" pairs with '#', ';' and
/// '//' comments. It is read once and kept, since it does not change while the
/// starter runs.
/// </remarks>
public sealed class LoadWeightsRepository
{
    private const string FileName = "load_weights.txt";

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly object _gate = new();

    private LoadWeightsTable? _table;

    public LoadWeightsRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log)
    {
        _files = files;
        _paths = paths;
        _log = log;
    }

    public LoadWeightsTable Table
    {
        get
        {
            lock (_gate)
                return _table ??= Read();
        }
    }

    private LoadWeightsTable Read()
    {
        string path = Path.Combine(_paths.Data, FileName);
        if (!_files.FileExists(path))
            return new LoadWeightsTable();

        try
        {
            return new LoadWeightsTable(ParsePairs(_files.ReadAllText(path, LegacyText.CodePage1250)));
        }
        catch (Exception ex)
        {
            _log.Log($"data/{FileName}", ex);
            return new LoadWeightsTable();
        }
    }

    private static Dictionary<string, int> ParsePairs(string text)
    {
        var body = new StringBuilder();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw;

            int comment = line.IndexOfAny(new[] { '#', ';' });
            if (comment >= 0)
                line = line[..comment];

            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0)
                line = line[..slashes];

            body.Append(line).Append(' ');
        }

        // Colons may be written tight against their neighbours, so space them out
        // before tokenising.
        string[] tokens = body.Replace(":", " : ").ToString()
                              .Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i + 1 < tokens.Length; i++)
        {
            if (tokens[i] != ":")
                continue;

            string name = tokens[i - 1];
            if (name is "{" or "}")
                continue;

            if (int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                weights[name] = weight;
        }

        return weights;
    }

}
