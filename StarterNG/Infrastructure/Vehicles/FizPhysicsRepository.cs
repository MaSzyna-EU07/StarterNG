using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Vehicles;

public sealed class FizPhysicsRepository : IPhysicsRepository
{
    private const int MaxIncludeDepth = 6;

    private const int UndeclaredCoupler = -1;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "M", "Vmax", "L", "LoadAccepted", "LoadQ", "MaxLoad", "AllowedFlag", "ControlType", "EngineType"
    };

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;

    private readonly ConcurrentDictionary<string, VehiclePhysics?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // Names repeat between folders: 214m.fiz is one vehicle in 21xm_v1, another in sa134_v1.
    private sealed record FizIndex(Dictionary<string, string> ByFolderAndName, Dictionary<string, string> ByName);

    private readonly object _indexGate = new();
    private FizIndex? _index;

    public FizPhysicsRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log)
    {
        _files = files;
        _paths = paths;
        _log = log;
    }

    public void Preload() => Index();

    public int IndexedCount => _index?.ByName.Count ?? 0;

    public VehiclePhysics? For(string? dataFolder, string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        string name = Path.GetFileNameWithoutExtension(model);
        if (name.Length == 0)
            return null;

        string folder = FolderKey(dataFolder);
        string key = FizKey(folder, name);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var physics = Read(folder, name);
        _cache[key] = physics;
        return physics;
    }

    private static string FizKey(string folder, string name) => $"{folder}/{name}";

    /// <summary>A vehicle's folder under dynamic, however the caller spells it.</summary>
    private static string FolderKey(string? folder)
    {
        const string dynamicPrefix = "dynamic/";
        string key = (folder ?? "").Replace('\\', '/').Trim('/');
        return key.StartsWith(dynamicPrefix, StringComparison.OrdinalIgnoreCase) ? key[dynamicPrefix.Length..] : key;
    }

    private VehiclePhysics? Read(string folder, string name)
    {
        var index = Index();

        if (!index.ByFolderAndName.TryGetValue(FizKey(folder, name), out string? path) &&
            !index.ByFolderAndName.TryGetValue(FizKey(folder, name + "dumb"), out path) &&
            !index.ByName.TryGetValue(name, out path) &&
            !index.ByName.TryGetValue(name + "dumb", out path))
            return null;

        var physics = new VehiclePhysics { AllowedFlagB = UndeclaredCoupler };
        try
        {
            Parse(physics, path, Path.GetDirectoryName(path) ?? "", parameters: null, depth: 0);
        }
        catch (Exception ex)
        {
            _log.Log($"physics/{Path.GetFileName(path)}", ex);
        }

        if (physics.AllowedFlagB == UndeclaredCoupler)
        {
            physics.AllowedFlagB = physics.AllowedFlagA;
            if (string.IsNullOrEmpty(physics.ControlTypeB))
                physics.ControlTypeB = physics.ControlTypeA;
        }

        return physics;
    }

    private FizIndex Index()
    {
        lock (_indexGate)
        {
            if (_index is not null)
                return _index;

            var byFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Sorted, so which of two namesakes a name alone finds is the same on every machine.
                foreach (string file in _files.GetFilesRecursive(_paths.Dynamic, "*.fiz").Order(StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string folder = FolderKey(Path.GetDirectoryName(Path.GetRelativePath(_paths.Dynamic, file)));
                    byFolder[FizKey(folder, name)] = file;
                    byName.TryAdd(name, file);
                }
            }
            catch (Exception ex)
            {
                _log.Log($"indexing {_paths.Dynamic}", ex);
            }

            _index = new FizIndex(byFolder, byName);
            return _index;
        }
    }

    private void Parse(VehiclePhysics physics, string path, string directory, string[]? parameters, int depth)
    {
        if (depth > MaxIncludeDepth || !_files.FileExists(path))
            return;

        var tokens = Tokenize(LegacyText.Decode(_files.ReadAllBytes(path)));

        string section = "";
        string? pendingKey = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (token == "=")
                continue;

            if (token.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= tokens.Count)
                    break;

                string includeFile = Resolve(tokens[i], parameters);
                var includeParameters = new List<string>();
                i++;
                while (i < tokens.Count && !tokens[i].Equals("end", StringComparison.OrdinalIgnoreCase))
                    includeParameters.Add(Resolve(tokens[i++], parameters));

                Parse(physics, Path.Combine(directory, includeFile), directory, includeParameters.ToArray(),
                      depth + 1);
                pendingKey = null;
                continue;
            }

            int equals = token.IndexOf('=');
            if (equals >= 0)
            {
                string key = token[..equals].TrimStart('.').Trim();
                string value = token[(equals + 1)..].Trim();
                if (value.Length == 0)
                {
                    pendingKey = key;
                    continue;
                }
                Apply(physics, section, key, Resolve(value, parameters));
                pendingKey = null;
                continue;
            }

            string bare = token.TrimStart('.').Trim();
            if (pendingKey is not null)
            {
                Apply(physics, section, pendingKey, Resolve(bare, parameters));
                pendingKey = null;
            }
            else if (KnownKeys.Contains(bare))
            {
                pendingKey = bare;
            }
            else
            {
                section = new string(bare.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            }
        }
    }

    private static void Apply(VehiclePhysics physics, string section, string key, string value)
    {
        switch (section)
        {
            case "param":
                if (Is(key, "M")) physics.Mass = ParseDouble(value, physics.Mass);
                else if (Is(key, "Vmax")) physics.VMax = ParseDouble(value, physics.VMax);
                break;
            case "load":
                if (Is(key, "LoadAccepted")) physics.LoadAccepted = value;
                else if (Is(key, "LoadQ")) physics.LoadQ = value;
                else if (Is(key, "MaxLoad")) physics.MaxLoad = (int)ParseDouble(value, physics.MaxLoad);
                break;
            case "engine":
                if (Is(key, "EngineType")) physics.EngineType = value;
                break;
            case "dimensions":
                if (Is(key, "L")) physics.Length = ParseDouble(value, physics.Length);
                break;
            case "buffcoupl":
            case "buffcoupl1":
                if (Is(key, "AllowedFlag")) physics.AllowedFlagA = ParseFlag(value, physics.AllowedFlagA);
                else if (Is(key, "ControlType")) physics.ControlTypeA = value;
                break;
            case "buffcoupl2":
                if (Is(key, "AllowedFlag")) physics.AllowedFlagB = ParseFlag(value, physics.AllowedFlagB);
                else if (Is(key, "ControlType")) physics.ControlTypeB = value;
                break;
        }
    }

    private static bool Is(string key, string name) => key.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static int ParseFlag(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag)
            ? flag < 0 ? -flag + 128 : flag
            : fallback;

    private static double ParseDouble(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;

    private static string Resolve(string token, string[]? parameters)
    {
        if (parameters is null || token.Length < 3 || token[0] != '(' || token[^1] != ')')
            return token;

        string digits = new(token.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int index) && index >= 1 && index <= parameters.Length
            ? parameters[index - 1]
            : token;
    }

    private static List<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            string stripped = line;
            int comment = stripped.IndexOf('#');
            if (comment >= 0)
                stripped = stripped[..comment];
            sb.Append(stripped).Append(' ');
        }

        return sb.ToString()
                 .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                 .ToList();
    }
}
