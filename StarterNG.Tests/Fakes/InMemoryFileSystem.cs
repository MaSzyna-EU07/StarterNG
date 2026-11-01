using System.Text;
using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>
/// An <see cref="IFileSystem"/> over a dictionary, so a whole MaSzyna
/// installation can be described inline in a test.
/// </summary>
/// <remarks>
/// Paths are normalised to forward slashes and compared case-insensitively,
/// matching how the parsers treat installation paths on both platforms - or,
/// asked for, case-sensitively, as a Linux disk holds them.
/// </remarks>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly StringComparison _comparison;
    private readonly Dictionary<string, byte[]> _files;
    private readonly HashSet<string> _directories;
    private readonly HashSet<string> _executable;
    private readonly Dictionary<string, string> _links;
    private readonly HashSet<string> _unreadable;
    private readonly HashSet<string> _unlistable;
    private readonly Dictionary<string, DateTime> _written;

    public InMemoryFileSystem(bool caseSensitive = false)
    {
        _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var comparer = StringComparer.FromComparison(_comparison);
        _files = new Dictionary<string, byte[]>(comparer);
        _directories = new HashSet<string>(comparer);
        _executable = new HashSet<string>(comparer);
        _links = new Dictionary<string, string>(comparer);
        _unreadable = new HashSet<string>(comparer);
        _unlistable = new HashSet<string>(comparer);
        _written = new Dictionary<string, DateTime>(comparer);
    }

    public DateTime LastWriteTimeUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Making a link throws, as on a disk that cannot hold one.</summary>
    public bool LinksFail { get; set; }

    /// <summary>A folder moved to a destination this says yes to, its path with forward slashes, throws.</summary>
    public Func<string, bool>? MoveFails { get; set; }

    /// <summary>The file stays, but reading or copying it throws, as a locked one does; writing it still goes through.</summary>
    public InMemoryFileSystem FailReading(string path)
    {
        _unreadable.Add(Normalize(path));
        return this;
    }

    public InMemoryFileSystem AllowReading(string path)
    {
        _unreadable.Remove(Normalize(path));
        return this;
    }

    /// <summary>The folder is there, but listing it throws, as one without the permission does.</summary>
    public InMemoryFileSystem DenyListing(string path)
    {
        _unlistable.Add(Normalize(path));
        return this;
    }

    static InMemoryFileSystem()
    {
        // The game's own files are code page 1250; a test that writes Polish text
        // into one needs the provider registered just as the application does.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Adds a UTF-8 text file, creating its parent directories.</summary>
    public InMemoryFileSystem WithFile(string path, string contents) =>
        WithFile(path, contents, Encoding.UTF8);

    /// <summary>Adds a text file in a given encoding, as the game's files are stored.</summary>
    public InMemoryFileSystem WithFile(string path, string contents, Encoding encoding)
    {
        string key = Normalize(path);
        _files[key] = encoding.GetBytes(contents);
        AddParents(key);
        return this;
    }

    /// <summary>Adds a file in the code page the .scn and .fiz formats use.</summary>
    public InMemoryFileSystem WithLegacyFile(string path, string contents) =>
        WithFile(path, contents, Encoding.GetEncoding(1250));

    /// <summary>Adds an empty directory.</summary>
    public InMemoryFileSystem WithDirectory(string path)
    {
        string key = Normalize(path);
        _directories.Add(key);
        AddParents(key);
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    /// <summary>A link counts as the directory it points at, as the disk has it.</summary>
    public bool DirectoryExists(string path) =>
        _directories.Contains(Normalize(path)) ||
        (_links.TryGetValue(Normalize(path), out string? target) && _directories.Contains(Normalize(target)));

    public string ReadAllText(string path) => Encoding.UTF8.GetString(Bytes(path));

    public string ReadAllText(string path, Encoding encoding) => encoding.GetString(Bytes(path));

    public string[] ReadAllLines(string path) =>
        ReadAllText(path).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    public byte[] ReadAllBytes(string path) => Bytes(path);

    public Stream OpenRead(string path) => new MemoryStream(Bytes(path), writable: false);

    public void WriteAllText(string path, string contents) => WithFile(path, contents);

    public void WriteAllText(string path, string contents, Encoding encoding)
    {
        string key = Normalize(path);
        _files[key] = encoding.GetBytes(contents);
        AddParents(key);
    }

    public void WriteAllTextAtomic(string path, string contents) => WithFile(path, contents);

    public void WriteAllBytesAtomic(string path, byte[] bytes) => WriteAllBytes(path, bytes);

    public void WriteAllLines(string path, IEnumerable<string> lines) =>
        WithFile(path, string.Join("\n", lines));

    public void WriteAllBytes(string path, byte[] bytes)
    {
        string key = Normalize(path);
        _files[key] = bytes;
        AddParents(key);
    }

    public void CreateDirectory(string path) => WithDirectory(path);

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        string destination = Normalize(destinationPath);
        if (!overwrite && _files.ContainsKey(destination))
            throw new IOException($"{destinationPath} already exists");
        WriteAllBytes(destinationPath, Bytes(sourcePath));
    }

    public IReadOnlyList<string> GetFiles(string path, string searchPattern = "*")
    {
        string prefix = Normalize(path) + "/";
        var pattern = ToRegex(searchPattern);
        return _files.Keys
            .Where(key => key.StartsWith(prefix, _comparison))
            .Where(key => !key[prefix.Length..].Contains('/'))
            .Where(key => pattern.IsMatch(key[prefix.Length..]))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetFilesRecursive(string path, string searchPattern)
    {
        string prefix = Normalize(path) + "/";
        var pattern = ToRegex(searchPattern);
        return _files.Keys
            .Where(key => key.StartsWith(prefix, _comparison))
            .Where(key => !_unlistable.Any(folder => key.StartsWith(folder + "/", _comparison)))
            .Where(key => pattern.IsMatch(key[(key.LastIndexOf('/') + 1)..]))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetDirectories(string path)
    {
        if (_unlistable.Contains(Normalize(path)))
            throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");

        string prefix = Normalize(path) + "/";
        return _directories
            .Where(key => key.StartsWith(prefix, _comparison))
            .Where(key => !key[prefix.Length..].Contains('/'))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DateTime GetLastWriteTimeUtc(string path) =>
        _written.TryGetValue(Normalize(path), out var written) ? written : LastWriteTimeUtc;

    /// <summary>One file's own time, in place of <see cref="LastWriteTimeUtc"/>.</summary>
    public InMemoryFileSystem WrittenAt(string path, DateTime utc)
    {
        _written[Normalize(path)] = utc;
        return this;
    }

    /// <summary>Files added through <see cref="WithExecutable"/> carry the execute bit.</summary>
    public long GetFileSize(string path) => Bytes(path).LongLength;

    public bool IsExecutable(string path) => _executable.Contains(Normalize(path));

    public InMemoryFileSystem WithExecutable(string path, string contents = "\u007fELF")
    {
        WithFile(path, contents);
        _executable.Add(Normalize(path));
        return this;
    }

    public string? LinkTarget(string path) => _links.GetValueOrDefault(Normalize(path));

    public void CreateDirectoryLink(string path, string target)
    {
        if (LinksFail)
            throw new IOException($"cannot link {path}");

        string key = Normalize(path);
        if (_links.ContainsKey(key) || _directories.Contains(key) || _files.ContainsKey(key))
            throw new IOException($"{path} already exists");
        _links[key] = target;
        AddParents(key);
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        if (MoveFails?.Invoke(Normalize(destinationPath)) == true)
            throw new IOException($"cannot move to {destinationPath}");

        string from = Normalize(sourcePath), to = Normalize(destinationPath);
        if (!_directories.Contains(from) && !_links.ContainsKey(from))
            throw new DirectoryNotFoundException(sourcePath);
        if (_directories.Contains(to) || _links.ContainsKey(to) || _files.ContainsKey(to))
            throw new IOException($"{destinationPath} already exists");

        string? Moved(string key) =>
            string.Equals(key, from, _comparison) ? to
            : key.StartsWith(from + "/", _comparison) ? to + key[from.Length..]
            : null;

        foreach (var (key, bytes) in _files.ToList())
            if (Moved(key) is { } moved) { _files.Remove(key); _files[moved] = bytes; }
        foreach (string key in _executable.ToList())
            if (Moved(key) is { } moved) { _executable.Remove(key); _executable.Add(moved); }
        foreach (var (key, target) in _links.ToList())
            if (Moved(key) is { } moved) { _links.Remove(key); _links[moved] = target; }
        foreach (string key in _directories.ToList())
            if (Moved(key) is { } moved) { _directories.Remove(key); _directories.Add(moved); }
        AddParents(to);
    }

    private byte[] Bytes(string path)
    {
        if (_unreadable.Contains(Normalize(path)))
            throw new IOException($"The process cannot access the file '{path}'.");

        return _files.TryGetValue(Normalize(path), out var bytes)
            ? bytes
            : throw new FileNotFoundException(path);
    }

    private void AddParents(string key)
    {
        int slash = key.LastIndexOf('/');
        while (slash > 0)
        {
            key = key[..slash];
            if (!_directories.Add(key))
                return;
            slash = key.LastIndexOf('/');
        }
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static System.Text.RegularExpressions.Regex ToRegex(string searchPattern) =>
        new("^" + System.Text.RegularExpressions.Regex.Escape(searchPattern)
                .Replace("\\*", ".*").Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
