using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// Port over the file system. Everything that reads or writes the MaSzyna
/// installation goes through this interface instead of <see cref="File"/> and
/// <see cref="Directory"/>, so parsers and use cases can be exercised against an
/// in-memory installation in tests.
/// </summary>
/// <remarks>
/// Kept deliberately narrow: only the operations the application actually
/// performs are exposed. Paths are passed through verbatim to the adapter, which
/// resolves them relative to the process working directory just like the
/// framework methods it replaces.
/// </remarks>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    string ReadAllText(string path, Encoding encoding);

    string[] ReadAllLines(string path);

    Stream OpenRead(string path);

    void WriteAllText(string path, string contents);

    void WriteAllText(string path, string contents, Encoding encoding);

    void WriteAllLines(string path, IEnumerable<string> lines);

    void WriteAllBytes(string path, byte[] bytes);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>Top-level files, empty when the directory does not exist.</summary>
    IReadOnlyList<string> GetFiles(string path, string searchPattern = "*");

    /// <summary>Top-level directories, empty when the directory does not exist.</summary>
    IReadOnlyList<string> GetDirectories(string path);

    DateTime GetLastWriteTimeUtc(string path);
}
