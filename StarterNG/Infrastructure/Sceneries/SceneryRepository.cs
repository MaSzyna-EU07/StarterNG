using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    private readonly CaseInsensitivePaths _names;

    public SceneryRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log, SceneryParser parser)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _parser = parser;
        _names = new CaseInsensitivePaths(files, keepListings: true);
    }

    public IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null)
    {
        _names.Forget();
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
        _names.Forget();
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
            var encodings = new Dictionary<string, Encoding>(StringComparer.Ordinal);

            // With its own .sbt the scenery's terrain comes from it, and the simulator
            // passes its terrain includes over without opening them.
            bool binaryTerrain = Opens(Path.GetFileNameWithoutExtension(path) + ".sbt");
            bool OpensInclude(string name) =>
                Opens(name) || (binaryTerrain && SimulatorNames.Lower(name).Contains("_ter.scm"));

            (string File, string Text)? ReadInclude(string name)
            {
                string? file = ResolveInclude(name);
                string full = Path.Combine(_paths.Scenery, file ?? name.Replace('\\', '/'));
                sources[full] = FileStamp.Of(_files, full);
                if (file is null)
                    return null;

                string included = LegacyText.Decode(_files.ReadAllBytes(full), out var encoding);
                encodings[file] = encoding;
                return (file, included);
            }

            string own = LegacyText.Decode(_files.ReadAllBytes(path), out var ownEncoding);
            string text = SceneryIncludes.Inline(own, ReadInclude);
            var scenery = _parser.Parse(path, text, OpensInclude);
            scenery.HasCompanionTimetable = binaryTerrain;
            scenery.Sources = sources;
            scenery.TextEncoding = ownEncoding;
            scenery.IncludeEncodings = encodings;
            return scenery;
        }
        catch (Exception ex)
        {
            _log.Log($"scenery/{Path.GetFileName(path)}", ex);
            return null;
        }
    }

    /// <summary>
    /// Whether the simulator opens a file of the scenery folder named so: whatever the
    /// case of its letters, on Linux too. A shade more lenient than eu07 there, which
    /// folds only the letters A to Z and of two folders named alike takes the first.
    /// </summary>
    private bool Opens(string name) => ResolveInclude(name) is not null;

    /// <summary>
    /// An include's file as it is named on disk, from the scenery folder, whatever
    /// the case of its letters - as Windows, or Wine, finds it: whose consists the
    /// copy written for a start carries in place of the include, so they come along
    /// on Linux too.
    /// </summary>
    private string? ResolveInclude(string name) => _names.Resolve(_paths.Scenery, name);

    private IEnumerable<string> SceneryFiles() =>
        _files.GetFiles(_paths.Scenery, "*.scn")
              .Where(path => Path.GetFileName(path).FirstOrDefault() != ExcludedPrefix);
}
