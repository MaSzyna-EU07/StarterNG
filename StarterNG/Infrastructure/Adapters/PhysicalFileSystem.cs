using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// <see cref="IFileSystem"/> backed by the real disk. The only place in the
/// application allowed to touch <see cref="File"/> and <see cref="Directory"/>.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public void WriteAllText(string path, string contents, Encoding encoding) =>
        File.WriteAllText(path, contents, encoding);

    public void WriteAllLines(string path, IEnumerable<string> lines) => File.WriteAllLines(path, lines);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern = "*") =>
        Directory.Exists(path) ? Directory.GetFiles(path, searchPattern) : Array.Empty<string>();

    public IReadOnlyList<string> GetFilesRecursive(string path, string searchPattern) =>
        Directory.Exists(path)
            ? Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories)
            : Array.Empty<string>();

    public IReadOnlyList<string> GetDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;

        const UnixFileMode anyExecute =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & anyExecute) != 0;
    }
}
