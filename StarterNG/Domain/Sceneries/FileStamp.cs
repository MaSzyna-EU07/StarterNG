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
            return files.LastWriteAndSize(path) is { } stat ? new FileStamp(stat.LastWriteUtc, stat.Size) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
