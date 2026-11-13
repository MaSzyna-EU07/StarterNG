using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure;

/// <summary>
/// Finds a file or folder of the game's data by a name whose letters may stand in
/// another case than on disk, as Windows, Wine and the simulator find it, and gives
/// the name as the disk has it. The data is made on Windows: its names and the
/// names written in it seldom agree letter for letter, which on Linux matters.
/// </summary>
public sealed class CaseInsensitivePaths
{
    private readonly IFileSystem _files;
    private readonly Func<(string Folder, bool Files), ILookup<string, string>> _list;

    /// <summary>Each folder's files and folders by name, listed once until <see cref="Forget"/>; null lists every time.</summary>
    private readonly ConcurrentDictionary<(string Folder, bool Files), ILookup<string, string>>? _listings;

    /// <param name="keepListings">
    /// Lists each folder once, for many names looked up in data that stays as it is
    /// meanwhile, until <see cref="Forget"/>.
    /// </param>
    public CaseInsensitivePaths(IFileSystem files, bool keepListings = false)
    {
        _files = files;
        _list = List;
        if (keepListings)
            _listings = new ConcurrentDictionary<(string, bool), ILookup<string, string>>();
    }

    /// <summary>Lets the next lookups list the folders again.</summary>
    public void Forget() => _listings?.Clear();

    /// <summary>
    /// The file named so under <paramref name="folder"/> as it is named on disk, relative
    /// to the folder with forward slashes; null when nothing there answers to it. A name
    /// written with its letters as on disk wins over one differing only in their case;
    /// a disk that does not tell case apart gives it back as written.
    /// </summary>
    /// <param name="name">
    /// A name under the folder, with either slash, joined to it as the simulator joins
    /// it: a slash ahead of it counts for nothing.
    /// </param>
    /// <param name="directory">Looks for a folder instead of a file.</param>
    public string? Resolve(string folder, string name, bool directory = false)
    {
        string[] parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        string exact = string.Join('/', parts);
        string whole = Path.Combine(folder, exact);
        if (directory ? _files.DirectoryExists(whole) : _files.FileExists(whole))
            return exact;
        // Where case does not matter, missed as written is missed in any case.
        if (_files.IgnoresCase)
            return null;

        var found = new string[parts.Length];
        return Walk(folder, parts, 0, directory, found) ? string.Join('/', found) : null;
    }

    /// <summary>
    /// The full path of the file named so, its letters as on disk; null when there is
    /// none. A name given in full is looked up from its own root, any other under
    /// <paramref name="folder"/> as <see cref="Resolve"/> does.
    /// </summary>
    public string? Find(string folder, string name, bool directory = false)
    {
        if (Path.IsPathFullyQualified(name))
        {
            folder = Path.GetPathRoot(name)!;
            name = name[folder.Length..];
        }

        return Resolve(folder, name, directory) is { } found
            ? Path.Combine(folder, found.Replace('/', Path.DirectorySeparatorChar))
            : null;
    }

    /// <summary>
    /// Finds the parts from <paramref name="at"/> on under <paramref name="current"/>. Of
    /// folders named alike in other case - two copies of the data on Linux - each is
    /// tried, the one written as on disk first.
    /// </summary>
    private bool Walk(string current, string[] parts, int at, bool directory, string[] found)
    {
        if (at == parts.Length)
            return true;

        string part = parts[at];
        if (part is "." or "..")
        {
            found[at] = part;
            return Walk(Path.Combine(current, part), parts, at + 1, directory, found);
        }

        bool last = at == parts.Length - 1;
        var matches = Listing(current, files: last && !directory)[part]
            .OrderBy(entry => string.Equals(Path.GetFileName(entry), part, StringComparison.Ordinal) ? 0 : 1);
        foreach (string match in matches)
        {
            found[at] = Path.GetFileName(match);
            if (Walk(match, parts, at + 1, directory, found))
                return true;
        }
        return false;
    }

    private ILookup<string, string> Listing(string folder, bool files) =>
        _listings is null ? List((folder, files)) : _listings.GetOrAdd((folder, files), _list);

    // A folder that cannot be listed holds nothing to be found.
    private ILookup<string, string> List((string Folder, bool Files) key)
    {
        var entries = Array.Empty<string>();
        try
        {
            if (_files.DirectoryExists(key.Folder))
                entries = (key.Files ? _files.GetFiles(key.Folder) : _files.GetDirectories(key.Folder)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing listed, nothing found.
        }
        return entries.ToLookup(entry => Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase);
    }
}
