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

    Stream OpenRead(string path);

    void WriteAllText(string path, string contents);

    void WriteAllText(string path, string contents, Encoding encoding);

    void WriteAllLines(string path, IEnumerable<string> lines);

    void WriteAllBytes(string path, byte[] bytes);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    IReadOnlyList<string> GetFiles(string path, string searchPattern = "*");

    IReadOnlyList<string> GetDirectories(string path);

    IReadOnlyList<string> GetFilesRecursive(string path, string searchPattern);

    DateTime GetLastWriteTimeUtc(string path);

    bool IsExecutable(string path);
}
