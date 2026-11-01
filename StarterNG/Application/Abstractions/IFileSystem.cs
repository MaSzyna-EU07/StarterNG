using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarterNG.Application.Abstractions;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    string ReadAllText(string path, Encoding encoding);

    string[] ReadAllLines(string path);

    byte[] ReadAllBytes(string path);

    Stream OpenRead(string path);

    void WriteAllText(string path, string contents);

    void WriteAllText(string path, string contents, Encoding encoding);

    /// <summary>
    /// Writes the file whole or not at all: a crash or a full disk half way leaves
    /// what stood there, not the start of what was on its way. A link stays a link,
    /// its file gets the contents; a file keeps its mode.
    /// </summary>
    void WriteAllBytesAtomic(string path, byte[] bytes);

    /// <summary>As UTF-8 without a mark, by <see cref="WriteAllBytesAtomic"/>.</summary>
    void WriteAllTextAtomic(string path, string contents);

    void WriteAllLines(string path, IEnumerable<string> lines);

    void WriteAllBytes(string path, byte[] bytes);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    IReadOnlyList<string> GetFiles(string path, string searchPattern = "*");

    IReadOnlyList<string> GetDirectories(string path);

    IReadOnlyList<string> GetFilesRecursive(string path, string searchPattern);

    DateTime GetLastWriteTimeUtc(string path);

    long GetFileSize(string path);

    bool IsExecutable(string path);

    /// <summary>Where the symbolic link at <paramref name="path"/> points, or null for anything else.</summary>
    string? LinkTarget(string path);

    /// <summary>Makes <paramref name="path"/> a symbolic link to the directory <paramref name="target"/>.</summary>
    void CreateDirectoryLink(string path, string target);

    void MoveDirectory(string sourcePath, string destinationPath);
}

public static class FileSystemExtensions
{
    /// <summary>
    /// Copies a file that could not be read beside itself, before something replaces
    /// it: it may hold what was not read. Throws when it cannot; then it must stay.
    /// </summary>
    public static void KeepAside(this IFileSystem files, string path, IDiagnosticsLog log)
    {
        if (!files.FileExists(path))
            return;

        string aside = path + ".unreadable";
        for (int n = 2; files.FileExists(aside); n++)
            aside = $"{path}.unreadable-{n}";

        files.CopyFile(path, aside, overwrite: false);
        log.Log($"{path} could not be read; a copy is kept as {aside}");
    }
}
