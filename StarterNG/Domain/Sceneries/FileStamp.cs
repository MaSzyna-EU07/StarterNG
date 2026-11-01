using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Domain.Sceneries;

/// <summary>A file as it stood, to tell whether it has been changed since.</summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Size)
{
    /// <summary>Null for one that is not there, or cannot be looked at just now.</summary>
    public static FileStamp? Of(IFileSystem files, string path)
    {
        try
        {
            return files.FileExists(path)
                ? new FileStamp(files.GetLastWriteTimeUtc(path), files.GetFileSize(path))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
