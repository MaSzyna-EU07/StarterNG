using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Application;

public enum SceneryTrainsetSaveOutcome
{
    Saved,

    /// <summary>The file no longer holds the trainset as it was read - edited outside meanwhile.</summary>
    NotFound,

    Failed
}

/// <summary>
/// Writes a consist edited in the depot into the scenery file itself, so it is
/// there the next time too - not only in the copy written for one start. The
/// file's first version is kept beside it, once - under a name of the starter's
/// own, since the simulator's editor keeps its <c>.scn.bak</c> there and rewrites
/// it on every save.
/// </summary>
public sealed class SaveSceneryTrainset
{
    public const string BackupSuffix = ".starter.bak";

    private readonly IFileSystem _files;
    private readonly IDiagnosticsLog _log;

    public SaveSceneryTrainset(IFileSystem files, IDiagnosticsLog log)
    {
        _files = files;
        _log = log;
    }

    public (SceneryTrainsetSaveOutcome Outcome, string? Detail) Execute(Scenery scenery, Trainset trainset)
    {
        string path = FileOf(scenery, trainset);
        try
        {
            byte[] bytes = _files.ReadAllBytes(path);
            var (encoding, bom) = LegacyText.Detect(bytes);
            string text = encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);

            // The trainset's own line ends, if it has any: a file can hold both kinds.
            string lines = trainset.RawEntry.Contains('\n') ? trainset.RawEntry : text;
            string newline = lines.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string entry = SceneryTrainsetText.Compose(trainset, newline);
            var sameFile = scenery.Trainsets.Where(t => t.SourceFile == trainset.SourceFile).ToList();
            if (SceneryTrainsetText.Replace(text, trainset, sameFile, entry) is not { } updated)
                return (SceneryTrainsetSaveOutcome.NotFound, null);

            string backup = path + BackupSuffix;
            if (!_files.FileExists(backup))
                _files.WriteAllBytesAtomic(backup, bytes);

            _files.WriteAllBytesAtomic(path, bom.Concat(encoding.GetBytes(updated)).ToArray());

            // Written here, so no change from outside for the scenery to follow.
            scenery.Sources = new Dictionary<string, FileStamp?>(scenery.Sources) { [path] = FileStamp.Of(_files, path) };

            // What the file holds now is what the next save looks for.
            trainset.RawEntry = entry;
            trainset.OriginalVelocity = trainset.Velocity;
            return (SceneryTrainsetSaveOutcome.Saved, null);
        }
        catch (Exception ex)
        {
            _log.Log($"saving trainset {trainset.Name} to {path}", ex);
            return (SceneryTrainsetSaveOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// The scenery file, or the included one the trainset came from - named, as
    /// includes are, from the scenery folder the scenery itself is in.
    /// </summary>
    public static string FileOf(Scenery scenery, Trainset trainset) =>
        trainset.SourceFile is { } file
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scenery.Path) ?? "", file.Replace('\\', '/'))
            : scenery.Path;
}
