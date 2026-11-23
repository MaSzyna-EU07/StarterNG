using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Sceneries;

/// <summary>
/// The sceneries' binary terrain files (.sbt): the simulator writes one beside a
/// scenery on its first load and from then on takes the terrain from it, passing
/// the scenery's terrain includes (<c>*_ter.scm</c>) over, and the triangles and
/// lines of its other files with them. It never asks whether the text changed
/// since, so a file made by an older simulator, or before the scenery was updated,
/// keeps showing what was there then.
/// <para>
/// A file goes only when the text of every scenery reading it still holds what it
/// was made of: some sceneries come with the .sbt in place of their terrain
/// includes, and without it they would have no terrain at all.
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

    // The only words that tell what comes after them; "endterrain" is found by the second.
    private static readonly SearchValues<string> Keywords =
        SearchValues.Create(["include", "terrain"], StringComparison.OrdinalIgnoreCase);

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
    /// file with geometry its text names and the installation lacks, or the terrain
    /// directive that brings terrain from elsewhere; null when the text holds it all.
    /// Another scenery reading the same .sbt counts too: what that one lacks comes
    /// with its name.
    /// </summary>
    public string? MissingOriginal(string sceneryPath)
    {
        string name = Path.GetFileName(sceneryPath);
        try
        {
            var texts = new Texts(this);
            return texts.Resolve(name) is null
                ? name
                : Unmakeable(Path.GetFileNameWithoutExtension(name) + Extension, texts);
        }
        catch (Exception ex)
        {
            _log.Log($"sbt/{name}", ex);
            return name;
        }
    }

    /// <summary>
    /// What keeps an .sbt of the scenery folder from being made again as it is, for
    /// every scenery reading it: the one it is named after, which is where the
    /// simulator writes it, the same one with the <c>$</c> a scenery editor puts
    /// ahead of the name, which the simulator drops, and any other naming it in a
    /// terrain directive. Null when all of them can do without it.
    /// </summary>
    private string? Unmakeable(string sbt, Texts texts)
    {
        string stem = Path.GetFileNameWithoutExtension(sbt);
        var sceneries = texts.Sceneries();

        // The scenery named as the file goes first: what it lacks is told without its name.
        var owners = sceneries.Where(scenery => SameName(Stem(scenery), stem)).ToList();
        owners.AddRange(sceneries.Where(scenery => !SameName(Stem(scenery), stem)
                                                   && SameName(Stem(scenery).TrimStart('$'), stem)));
        if (owners.Count == 0)
            return stem + ".scn";

        foreach (string owner in owners)
        {
            var text = texts.Of(owner);
            string? missing = text.Missing
                              ?? (text.BinaryTerrain.Count > 0 ? "terrain " + text.BinaryTerrain[0] : null);
            if (missing is not null)
                return SameName(Stem(owner), stem) ? missing : $"{missing} ({owner})";
        }

        // A scenery naming the file in a terrain directive takes its geometry from it. With the
        // file gone the simulator reads that scenery's own text, which then has to hold it all.
        foreach (string other in sceneries.Except(owners))
        {
            var text = texts.Of(other);
            if (text.Missing is { } missing && text.BinaryTerrain.Any(named => Names(named, stem)))
                return $"{missing} ({other})";
        }

        return null;
    }

    private static string Stem(string file) => Path.GetFileNameWithoutExtension(file);

    private static bool SameName(string one, string other) =>
        one.Equals(other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a terrain directive's word names the .sbt of the scenery folder with this
    /// name, the way the simulator reads it: the <c>$</c> ahead dropped, the file looked
    /// for under the scenery folder.
    /// </summary>
    private static bool Names(string word, string stem)
    {
        string name = word.Replace('\\', '/').TrimStart('$').TrimStart('/');
        return !name.Contains('/') && SameName(Stem(name), stem);
    }

    /// <summary>
    /// The scenery files read for one question, each of them once however many
    /// sceneries include it: most of an installation's sceneries share their files.
    /// </summary>
    private sealed class Texts
    {
        private readonly BinaryTerrainFiles _owner;
        private readonly CaseInsensitivePaths _names;
        private readonly Dictionary<string, string?> _resolved = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FileText> _files;
        private readonly Dictionary<string, SceneryText> _texts;
        private List<string>? _sceneries;

        public Texts(BinaryTerrainFiles owner)
        {
            _owner = owner;
            _names = new CaseInsensitivePaths(owner._files, keepListings: true);
            var comparer = owner._files.IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _files = new Dictionary<string, FileText>(comparer);
            _texts = new Dictionary<string, SceneryText>(comparer);
        }

        /// <summary>The file named so under the scenery folder as it is on disk; null when there is none.</summary>
        public string? Resolve(string name)
        {
            if (!_resolved.TryGetValue(name, out string? found))
                _resolved[name] = found = _names.Resolve(_owner._paths.Scenery, name);
            return found;
        }

        /// <summary>The sceneries of the installation, by their file names.</summary>
        public IReadOnlyList<string> Sceneries() =>
            _sceneries ??= _owner._files.GetFiles(_owner._paths.Scenery)
                                 .Where(file => Path.GetExtension(file).Equals(".scn", StringComparison.OrdinalIgnoreCase))
                                 .Select(file => Path.GetFileName(file))
                                 .OrderBy(file => file, StringComparer.Ordinal)
                                 .ToList();

        /// <summary>The whole text of a scenery, the files it includes read in turn.</summary>
        public SceneryText Of(string scenery)
        {
            if (_texts.TryGetValue(scenery, out var known))
                return known;

            string? missing = null;
            var binaryTerrain = new List<string>();
            try
            {
                var seen = new HashSet<string>(_files.Comparer);
                var pending = new Stack<string>();
                pending.Push(scenery);
                while (pending.Count > 0)
                {
                    string file = pending.Pop();
                    if (!seen.Add(file))
                        continue;

                    if (!_files.TryGetValue(file, out var text))
                        _files[file] = text = _owner.Scan(file, this);
                    missing ??= text.Missing;
                    binaryTerrain.AddRange(text.BinaryTerrain);
                    foreach (string include in text.Includes)
                        pending.Push(include);
                }
            }
            catch (Exception ex)
            {
                // Not read to its end, nothing tells what it would take to make again.
                _owner._log.Log($"sbt/{scenery}", ex);
                missing ??= scenery;
            }

            return _texts[scenery] = new SceneryText(missing, binaryTerrain);
        }
    }

    /// <param name="Missing">The first file with geometry named there that the installation lacks.</param>
    /// <param name="Includes">The files it includes that are to be read in turn.</param>
    /// <param name="BinaryTerrain">The .sbt files its terrain directives name.</param>
    private sealed record FileText(string? Missing, IReadOnlyList<string> Includes, IReadOnlyList<string> BinaryTerrain);

    /// <param name="Missing">The first file with geometry its text names that the installation lacks.</param>
    /// <param name="BinaryTerrain">The .sbt files its terrain directives name.</param>
    private sealed record SceneryText(string? Missing, IReadOnlyList<string> BinaryTerrain);

    // A terrain file larger than this is geometry and is left unopened - they are the largest
    // files of all. A smaller one may be a list of the files that hold it.
    private const long LargestTerrainFileRead = 1024 * 1024;

    /// <summary>
    /// Reads one file of the scenery for the files it names. With an .sbt there the
    /// simulator passes over every triangle and line of the text, in any file, so
    /// each scenery file included has to be there for the .sbt to be made again:
    /// the terrain ones (<c>*_ter.scm</c>) and any other .scm or .scn. The node
    /// templates (.inc) do not count: a scenery naming one that is not there is
    /// common, and none comes with an .sbt in its place.
    /// </summary>
    private FileText Scan(string file, Texts texts)
    {
        string text = LegacyText.Decode(_files.ReadAllBytes(Path.Combine(_paths.Scenery, file)));
        string code = SceneryCode.Mask(text, out _);

        string? missing = null;
        var includes = new List<string>();
        var binaryTerrain = new List<string>();
        bool includeNext = false;
        bool terrainDirective = false;
        int at = 0;
        while (true)
        {
            if (!includeNext && !terrainDirective)
            {
                // Nothing is asked of the words up to the next one that may be a keyword,
                // and a scenery file is mostly numbers.
                int hit = code.AsSpan(at).IndexOfAny(Keywords);
                if (hit < 0)
                    break;
                for (at += hit; at > 0 && !Separates(code[at - 1]); at--) { }
            }

            if (!NextWord(code, ref at, out var token))
                break;

            if (includeNext)
            {
                includeNext = false;
                string name = token.ToString();
                bool terrain = SimulatorNames.Lower(name).Contains("_ter.scm");
                string? found = texts.Resolve(name);
                if (found is null)
                {
                    if (terrain || IsSceneryFile(name))
                        missing ??= name;
                }
                else if (!terrain
                         || _files.GetFileSize(Path.Combine(_paths.Scenery, found)) <= LargestTerrainFileRead)
                    includes.Add(found);
            }
            // An include is one wherever it stands, and a word that only reads as the start of
            // a terrain directive - the name of a node, of a texture - must not hide those after it.
            else if (token.Equals("include", StringComparison.OrdinalIgnoreCase))
                includeNext = true;
            else if (token.Equals("terrain", StringComparison.OrdinalIgnoreCase))
                terrainDirective = true;
            else if (token.Equals("endterrain", StringComparison.OrdinalIgnoreCase))
                terrainDirective = false;
            else if (terrainDirective)
            {
                var extension = Path.GetExtension(token);
                if (extension.Equals(Extension, StringComparison.OrdinalIgnoreCase))
                    binaryTerrain.Add(token.ToString());
                else if (extension.Equals(".txtf", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".btf", StringComparison.OrdinalIgnoreCase))
                    missing ??= TerrainFileMissing(token.ToString(), texts);
            }
        }

        return new FileText(missing, includes, binaryTerrain);
    }

    // The simulator splits its text into words at spaces, tabs, line ends and semicolons.
    private static bool Separates(char c) => c == ';' || char.IsWhiteSpace(c);

    private static bool NextWord(string code, ref int at, out ReadOnlySpan<char> word)
    {
        while (at < code.Length && Separates(code[at]))
            at++;
        int start = at;
        while (at < code.Length && !Separates(code[at]))
            at++;
        word = code.AsSpan(start, at - start);
        return at > start;
    }

    private static bool IsSceneryFile(string name)
    {
        string extension = Path.GetExtension(name);
        return extension.Equals(".scm", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".scn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A terrain file (.txtf) named by a terrain directive has its geometry in itself
    /// or in its binary copy (.btf) beside it: with either one there, the scenery's
    /// .sbt only keeps that terrain from being used, and the simulator asks for it
    /// to go. An .sbt named there is another matter: it holds the geometry of the
    /// whole scenery and nothing makes it again, so the scenery's own goes only
    /// without one.
    /// </summary>
    private static string? TerrainFileMissing(string token, Texts texts)
    {
        string stem = token[..^Path.GetExtension(token).Length];
        return texts.Resolve(stem + ".txtf") is null && texts.Resolve(stem + ".btf") is null
            ? stem + ".txtf"
            : null;
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
        var texts = new Texts(this);

        foreach (string sbt in _files.GetFiles(_paths.Scenery)
                                     .Where(f => Path.GetExtension(f).Equals(Extension, StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(sbt);
            string? missing;
            try
            {
                missing = Unmakeable(name, texts);
            }
            catch (Exception ex)
            {
                _log.Log($"sbt/{name}", ex);
                missing = Path.GetFileNameWithoutExtension(name) + ".scn";
            }

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
