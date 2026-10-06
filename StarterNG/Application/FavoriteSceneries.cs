using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Application;

/// <summary>
/// The sceneries starred in the list, by file name, one per line in a file of the
/// starter's own - eu07.ini is the simulator's as much as ours.
/// </summary>
public sealed class FavoriteSceneries
{
    private readonly IFileSystem _files;
    private readonly IDiagnosticsLog _log;
    private readonly string _path;
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public FavoriteSceneries(IFileSystem files, IDiagnosticsLog log, string path)
    {
        _files = files;
        _log = log;
        _path = path;
        Load();
    }

    public bool Contains(Scenery scenery) => _names.Contains(Key(scenery));

    public void Toggle(Scenery scenery)
    {
        string key = Key(scenery);
        if (!_names.Remove(key))
            _names.Add(key);
        Save();
    }

    private static string Key(Scenery scenery) => Path.GetFileName(scenery.Path);

    private void Load()
    {
        try
        {
            if (!_files.FileExists(_path))
                return;

            foreach (string line in _files.ReadAllText(_path).Split('\n'))
                if (line.Trim() is { Length: > 0 } name)
                    _names.Add(name);
        }
        catch (Exception ex)
        {
            _log.Log($"reading {_path}", ex);
        }
    }

    private void Save()
    {
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } directory)
                _files.CreateDirectory(directory);

            _files.WriteAllText(_path, string.Join('\n', _names.Order(StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            _log.Log($"writing {_path}", ex);
        }
    }
}
