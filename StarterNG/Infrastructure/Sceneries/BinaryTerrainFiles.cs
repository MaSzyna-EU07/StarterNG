using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Domain;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Sceneries;

/// <summary>
/// The sceneries' binary terrain files (.sbt): the simulator writes one beside a
/// scenery on its first load and from then on takes the terrain from it, passing
/// the scenery's terrain includes (<c>*_ter.scm</c>) over. It never asks whether
/// the text changed since, so a file made by an older simulator, or before the
/// scenery was updated, keeps showing what was there then.
/// <para>
/// A file goes only when the scenery's text still holds what it was made of: some
/// sceneries come with the .sbt in place of their terrain includes, and without it
/// they would have no terrain at all.
/// </para>
/// </summary>
public sealed class BinaryTerrainFiles
{
    /// <summary>
    /// Raised by a release that wants the .sbt files made anew: each installation is
    /// cleared once for each value, on the first start of a starter carrying it.
    /// Zero clears nothing.
    /// </summary>
    public const int CleanupGeneration = 1;

    private const string Extension = ".sbt";

    // The simulator splits its text into words at spaces, tabs, line ends and semicolons.
    private static readonly Regex Word = new(@"[^\s;]+", RegexOptions.CultureInvariant);

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly string _recordPath;

    /// <param name="recordPath">Where the cleanups done are noted, for each installation.</param>
    public BinaryTerrainFiles(IFileSystem files, IGamePaths paths, IDiagnosticsLog log, string recordPath)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _recordPath = recordPath;
    }

    /// <summary>The scenery's own .sbt as it is on disk, whatever the case of its letters; null when it has none.</summary>
    public string? Of(string sceneryPath)
    {
        string? file = new CaseInsensitivePaths(_files)
            .Resolve(_paths.Scenery, Path.GetFileNameWithoutExtension(sceneryPath) + Extension);
        return file is null ? null : Path.Combine(_paths.Scenery, file);
    }

    /// <summary>
    /// What the simulator would miss to make the scenery's .sbt again - the first
    /// terrain file its text names and the installation lacks, or the terrain directive
    /// that brings terrain from elsewhere; null when the text holds it all.
    /// </summary>
    public string? MissingOriginal(string sceneryPath)
    {
        try
        {
            var names = new CaseInsensitivePaths(_files, keepListings: true);
            string? own = names.Resolve(_paths.Scenery, Path.GetFileName(sceneryPath));
            if (own is null)
                return Path.GetFileName(sceneryPath);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(own);
            while (pending.Count > 0)
            {
                string file = pending.Pop();
                if (!seen.Add(file))
                    continue;

                if (Scan(file, names, pending) is { } missing)
                    return missing;
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Log($"sbt/{Path.GetFileName(sceneryPath)}", ex);
            return Path.GetFileName(sceneryPath);
        }
    }

    /// <summary>
    /// Reads one file of the scenery for its includes: the terrain ones have to be
    /// there, the others are read in turn. Terrain files are not opened - they are the
    /// largest of all and include nothing.
    /// </summary>
    private string? Scan(string file, CaseInsensitivePaths names, Stack<string> pending)
    {
        string text = LegacyText.Decode(_files.ReadAllBytes(Path.Combine(_paths.Scenery, file)));
        string code = SceneryCode.Mask(text, out _);

        bool includeNext = false;
        for (var word = Word.Match(code); word.Success; word = word.NextMatch())
        {
            string token = word.Value;
            if (includeNext)
            {
                includeNext = false;
                bool terrain = SimulatorNames.Lower(token).Contains("_ter.scm");
                string? found = names.Resolve(_paths.Scenery, token);
                if (found is null && terrain)
                    return token;
                // Any other file the simulator reads from the text, .sbt or not: missing,
                // it is missing either way, and the .sbt made again lacks nothing.
                if (found is not null && !terrain)
                    pending.Push(found);
            }
            else if (token.Equals("include", StringComparison.OrdinalIgnoreCase))
                includeNext = true;
            else if (token.Equals("terrain", StringComparison.OrdinalIgnoreCase))
                return token;
        }

        return null;
    }

    /// <summary>Removes the scenery's .sbt; false when it has none or it could not go.</summary>
    public bool Delete(string sceneryPath)
    {
        if (Of(sceneryPath) is not { } file)
            return false;

        try
        {
            _files.DeleteFile(file);
            _log.Log($"{file} removed, the simulator makes it again on the next load of the scenery");
            return true;
        }
        catch (Exception ex)
        {
            _log.Log($"sbt/{Path.GetFileName(file)}", ex);
            return false;
        }
    }

    /// <summary>
    /// Clears the .sbt files the simulator can make again, once for each
    /// <paramref name="generation"/> on this installation; null when it was done
    /// already. A file that could not go leaves the cleanup to the next start.
    /// </summary>
    public BinaryTerrainCleanup? CleanUpOnce(int generation = CleanupGeneration)
    {
        if (generation <= 0 || !_files.DirectoryExists(_paths.Scenery))
            return null;

        var record = ReadRecord();
        string installation = Installation();
        if (record.TryGetValue(installation, out int done) && done >= generation)
            return null;

        var deleted = new List<string>();
        var kept = new List<(string File, string Reason)>();
        var failed = new List<string>();
        var names = new CaseInsensitivePaths(_files);

        foreach (string sbt in _files.GetFiles(_paths.Scenery)
                                     .Where(f => Path.GetExtension(f).Equals(Extension, StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(sbt);
            string? scenery = names.Resolve(_paths.Scenery, Path.GetFileNameWithoutExtension(sbt) + ".scn");
            string? missing = scenery is null ? Path.GetFileNameWithoutExtension(sbt) + ".scn" : MissingOriginal(scenery);
            if (missing is not null)
            {
                kept.Add((name, missing));
                _log.Log($"{name} kept: the scenery cannot be made without it, {missing} is missing");
                continue;
            }

            try
            {
                _files.DeleteFile(sbt);
                deleted.Add(name);
            }
            catch (Exception ex)
            {
                failed.Add(name);
                _log.Log($"sbt/{name}", ex);
            }
        }

        if (deleted.Count > 0)
            _log.Log($"Binary terrain files removed, the simulator makes them again: {string.Join(", ", deleted)}");

        if (failed.Count == 0)
        {
            record[installation] = generation;
            WriteRecord(record);
        }

        return new BinaryTerrainCleanup(deleted, kept, failed);
    }

    private string Installation() =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(_paths.Root));

    private Dictionary<string, int> ReadRecord()
    {
        var record = new Dictionary<string, int>(
            _files.IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            if (!_files.FileExists(_recordPath))
                return record;

            foreach (string line in _files.ReadAllLines(_recordPath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line[..tab], out int generation))
                    record[line[(tab + 1)..]] = generation;
            }
        }
        catch (Exception ex)
        {
            _log.Log("sbt cleanup record", ex);
        }

        return record;
    }

    private void WriteRecord(Dictionary<string, int> record)
    {
        try
        {
            if (Path.GetDirectoryName(_recordPath) is { Length: > 0 } folder)
                _files.CreateDirectory(folder);
            _files.WriteAllTextAtomic(_recordPath,
                string.Concat(record.Select(entry => $"{entry.Value}\t{entry.Key}\n")));
        }
        catch (Exception ex)
        {
            _log.Log("sbt cleanup record", ex);
        }
    }
}

/// <param name="Deleted">The files removed.</param>
/// <param name="Kept">The files left, as nothing could make them again, each with what is missing.</param>
/// <param name="Failed">The files that could not be removed.</param>
public sealed record BinaryTerrainCleanup(
    IReadOnlyList<string> Deleted,
    IReadOnlyList<(string File, string Reason)> Kept,
    IReadOnlyList<string> Failed);
