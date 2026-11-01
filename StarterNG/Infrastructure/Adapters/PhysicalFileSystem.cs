using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class PhysicalFileSystem : IFileSystem
{
    public const string TempSuffix = ".starter.tmp";

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    // Shared for writing too, so a file the simulator still holds open - a physics log of
    // a running trip - can be read.
    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public void WriteAllText(string path, string contents, Encoding encoding) =>
        File.WriteAllText(path, contents, encoding);

    public void WriteAllTextAtomic(string path, string contents) =>
        WriteAllBytesAtomic(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents));

    /// <summary>
    /// Written beside the file and moved over it. A crash at any moment leaves the old
    /// file or the new one whole - at worst with the temporary one beside it, which
    /// the next save writes over, under a name nothing in the game reads.
    /// </summary>
    public void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        // Through a link, to its file: moved over the link, the text would take its place.
        if (File.Exists(path) && File.ResolveLinkTarget(path, returnFinalTarget: true) is { } linked)
            path = linked.FullName;

        string temporary = path + TempSuffix;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                // On the disk itself first, or a power cut could leave the new name with no text under it.
                stream.Flush(flushToDisk: true);
            }

            // A new file gets the default mode; a write in place kept the old one.
            if (!OperatingSystem.IsWindows() && File.Exists(path) &&
                File.GetUnixFileMode(path) is var mode && mode != File.GetUnixFileMode(temporary))
                File.SetUnixFileMode(temporary, mode);

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The failure to tell is the one that brought us here.
            }
            throw;
        }
    }

    public void WriteAllLines(string path, IEnumerable<string> lines) => File.WriteAllLines(path, lines);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern = "*") =>
        Directory.Exists(path) ? Directory.GetFiles(path, searchPattern) : Array.Empty<string>();

    // A folder it may not enter costs what is in it, not the whole listing.
    public IReadOnlyList<string> GetFilesRecursive(string path, string searchPattern) =>
        Directory.Exists(path)
            ? Directory.GetFiles(path, searchPattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                MatchType = MatchType.Win32
            })
            : Array.Empty<string>();

    public IReadOnlyList<string> GetDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;

        const UnixFileMode anyExecute =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & anyExecute) != 0;
    }

    public string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    public void CreateDirectoryLink(string path, string target) => Directory.CreateSymbolicLink(path, target);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);
}
