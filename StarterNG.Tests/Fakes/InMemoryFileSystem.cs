using System.Text;
using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>
/// An <see cref="IFileSystem"/> over a dictionary, so a whole MaSzyna
/// installation can be described inline in a test.
/// </summary>
/// <remarks>
/// Paths are normalised to forward slashes and compared case-insensitively,
/// matching how the parsers treat installation paths on both platforms.
/// </remarks>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _executable = new(StringComparer.OrdinalIgnoreCase);

    public DateTime LastWriteTimeUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Adds a text file, creating its parent directories.</summary>
    public InMemoryFileSystem WithFile(string path, string contents)
    {
        string key = Normalize(path);
        _files[key] = Encoding.UTF8.GetBytes(contents);
        AddParents(key);
        return this;
    }

    /// <summary>Adds an empty directory.</summary>
    public InMemoryFileSystem WithDirectory(string path)
    {
        string key = Normalize(path);
        _directories.Add(key);
        AddParents(key);
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path) => Encoding.UTF8.GetString(Bytes(path));

    public string ReadAllText(string path, Encoding encoding) => encoding.GetString(Bytes(path));

    public string[] ReadAllLines(string path) =>
        ReadAllText(path).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    public Stream OpenRead(string path) => new MemoryStream(Bytes(path), writable: false);

    public void WriteAllText(string path, string contents) => WithFile(path, contents);

    public void WriteAllText(string path, string contents, Encoding encoding)
    {
        string key = Normalize(path);
        _files[key] = encoding.GetBytes(contents);
        AddParents(key);
    }

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
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(key => pattern.IsMatch(key[(key.LastIndexOf('/') + 1)..]))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetDirectories(string path)
    {
        string prefix = Normalize(path) + "/";
        return _directories
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(key => !key[prefix.Length..].Contains('/'))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DateTime GetLastWriteTimeUtc(string path) => LastWriteTimeUtc;

    /// <summary>Files added through <see cref="WithExecutable"/> carry the execute bit.</summary>
    public bool IsExecutable(string path) => _executable.Contains(Normalize(path));

    public InMemoryFileSystem WithExecutable(string path, string contents = "\u007fELF")
    {
        WithFile(path, contents);
        _executable.Add(Normalize(path));
        return this;
    }

    private byte[] Bytes(string path) =>
        _files.TryGetValue(Normalize(path), out var bytes)
            ? bytes
            : throw new FileNotFoundException(path);

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
