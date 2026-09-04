using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

/// <summary>
/// Loads scenarios from the installation's scenery folder.
/// </summary>
/// <remarks>
/// Sceneries are code page 1250 text, and a large installation holds hundreds of
/// them, so the sweep is parallel; each file that throws is logged and dropped
/// rather than taking the whole load down.
/// </remarks>
public sealed class SceneryRepository : ISceneryRepository
{
    /// <summary>Files starting with '$' are authoring scratch, not scenarios.</summary>
    private const char ExcludedPrefix = '$';

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly SceneryParser _parser;
    private readonly Encoding _encoding;

    public SceneryRepository(IFileSystem files, IGamePaths paths, IDiagnosticsLog log, SceneryParser parser)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _parser = parser;
        _encoding = CodePage1250();
    }

    public IReadOnlyList<Scenery> LoadAll(IProgress<SceneryLoadProgress>? progress = null)
    {
        var files = SceneryFiles().ToList();
        var loaded = new ConcurrentBag<Scenery>();
        int total = Math.Max(1, files.Count);
        int done = 0;

        Parallel.ForEach(files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var scenery = Load(file);
                if (scenery is not null)
                    loaded.Add(scenery);

                progress?.Report(new SceneryLoadProgress(Interlocked.Increment(ref done), total,
                                                         Path.GetFileName(file)));
            });

        return loaded.ToList();
    }

    public Scenery? Load(string path)
    {
        try
        {
            if (!_files.FileExists(path))
                return null;

            var scenery = _parser.Parse(path, _files.ReadAllText(path, _encoding));
            scenery.HasCompanionTimetable = _files.FileExists(Path.ChangeExtension(path, ".sbt"));
            return scenery;
        }
        catch (Exception ex)
        {
            _log.Log($"scenery/{Path.GetFileName(path)}", ex);
            return null;
        }
    }

    private IEnumerable<string> SceneryFiles() =>
        _files.GetFiles(_paths.Scenery, "*.scn")
              .Where(path => Path.GetFileName(path).FirstOrDefault() != ExcludedPrefix);

    /// <summary>
    /// The .scn dialect predates Unicode. The provider is registered by the entry
    /// point; fall back to Latin-1 if that ever fails so loading still works.
    /// </summary>
    private static Encoding CodePage1250()
    {
        try
        {
            return Encoding.GetEncoding(1250);
        }
        catch (Exception)
        {
            return Encoding.Latin1;
        }
    }
}
