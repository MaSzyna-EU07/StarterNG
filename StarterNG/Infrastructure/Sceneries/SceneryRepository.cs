using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;
using StarterNG.Domain;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class SceneryRepository : ISceneryRepository
{
    private const char ExcludedPrefix = '$';

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly SceneryParser _parser;

    /// <summary>The scenery folders' files and folders, listed once per load of them all.</summary>
    private readonly ConcurrentDictionary<(string, bool), IReadOnlyList<string>> _listings = new();

    public SceneryRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log, SceneryParser parser)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _parser = parser;
    }

    public IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null)
    {
        _listings.Clear();
        var files = SceneryFiles().ToList();
        var loaded = new ConcurrentBag<Scenery>();
        int total = Math.Max(1, files.Count);
        int done = 0;

        Parallel.ForEach(files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var scenery = LoadOne(file);
                if (scenery is not null)
                    loaded.Add(scenery);

                progress?.Report(new SceneryLoadProgress(Interlocked.Increment(ref done), total,
                                                         Path.GetFileName(file)));
            });

        return loaded.ToList();
    }

    public Scenery? Load(string path)
    {
        _listings.Clear();
        return LoadOne(path);
    }

    public IReadOnlyList<string>? ListFiles() =>
        _files.DirectoryExists(_paths.Scenery) ? SceneryFiles().ToList() : null;

    private Scenery? LoadOne(string path)
    {
        try
        {
            if (!_files.FileExists(path))
                return null;

            // Looked at ahead of the reading: changed meanwhile, it counts as changed.
            var sources = new Dictionary<string, FileStamp?> { [path] = FileStamp.Of(_files, path) };

            // With its own .sbt the scenery's terrain comes from it, and the simulator
            // passes its terrain includes over without opening them.
            bool binaryTerrain = Opens(Path.GetFileNameWithoutExtension(path) + ".sbt");
            IncludeLookup FindInclude(string name) =>
                Opens(name) || (binaryTerrain && SimulatorNames.Lower(name).Contains("_ter.scm"))
                    ? new IncludeLookup(true)
                    : new IncludeLookup(false, ResolveInclude(name));

            (string File, string Text)? ReadInclude(string name)
            {
                string? file = ResolveInclude(name);
                string full = Path.Combine(_paths.Scenery, file ?? name.Replace('\\', '/'));
                sources[full] = FileStamp.Of(_files, full);
                return file is null ? null : (file, LegacyText.Decode(_files.ReadAllBytes(full)));
            }

            string text = SceneryIncludes.Inline(LegacyText.Decode(_files.ReadAllBytes(path)), ReadInclude);
            var scenery = _parser.Parse(path, text, FindInclude);
            scenery.HasCompanionTimetable = binaryTerrain;
            scenery.Sources = sources;
            return scenery;
        }
        catch (Exception ex)
        {
            _log.Log($"scenery/{Path.GetFileName(path)}", ex);
            return null;
        }
    }

    /// <summary>
    /// Whether the simulator opens a file of the scenery folder named so: by the
    /// name in lower case, which on Linux has to be how it is named on disk.
    /// </summary>
    private bool Opens(string name) =>
        _files.FileExists(Path.Combine(_paths.Scenery, SimulatorNames.Lower(name.Replace('\\', '/'))));

    /// <summary>
    /// An include's file as it is named on disk, from the scenery folder, whatever
    /// the case of its letters - as Windows, or Wine, finds it: whose consists the
    /// copy written for a start carries in place of the include, so they come along
    /// on Linux too.
    /// </summary>
    private string? ResolveInclude(string name)
    {
        string relative = name.Replace('\\', '/');
        if (_files.FileExists(Path.Combine(_paths.Scenery, relative)))
            return relative;

        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string directory = _paths.Scenery;
        var found = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "." or "..")
            {
                directory = Path.Combine(directory, parts[i]);
                found.Add(parts[i]);
                continue;
            }

            bool last = i == parts.Length - 1;
            string? match = Listing(directory, last)
                .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), parts[i],
                                                       StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return null;
            directory = match;
            found.Add(Path.GetFileName(match));
        }

        return string.Join('/', found);
    }

    private IReadOnlyList<string> Listing(string directory, bool files) =>
        _listings.GetOrAdd((directory, files), key =>
        {
            if (!_files.DirectoryExists(key.Item1))
                return Array.Empty<string>();
            return key.Item2 ? _files.GetFiles(key.Item1) : _files.GetDirectories(key.Item1);
        });

    private IEnumerable<string> SceneryFiles() =>
        _files.GetFiles(_paths.Scenery, "*.scn")
              .Where(path => Path.GetFileName(path).FirstOrDefault() != ExcludedPrefix);
}
