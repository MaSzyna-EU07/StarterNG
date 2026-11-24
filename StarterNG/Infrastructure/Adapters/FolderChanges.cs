using System;
using System.IO;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// Tells when anything in a folder or below it is written, made, removed or renamed, as
/// the system reports it. Only a hint that it is time to look: a save comes as several
/// reports, and on a network drive, under Wine or past the system's limit of watches it
/// may come as none, so whoever listens still looks now and then on its own.
/// </summary>
public sealed class FolderChanges : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    /// <summary>Something changed, or the reports were lost and anything may have: on a thread of the pool.</summary>
    public event Action? Changed;

    /// <param name="folder">Watched with everything below it; a missing one is not watched.</param>
    public FolderChanges(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite | NotifyFilters.Size,
                // Unpacking an update changes hundreds of files at once.
                InternalBufferSize = 64 * 1024
            };
            watcher.Changed += (_, _) => Changed?.Invoke();
            watcher.Created += (_, _) => Changed?.Invoke();
            watcher.Deleted += (_, _) => Changed?.Invoke();
            watcher.Renamed += (_, _) => Changed?.Invoke();
            // Too many at once for the buffer: which ones is lost, so all are looked at.
            watcher.Error += (_, _) => Changed?.Invoke();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or PlatformNotSupportedException)
        {
            // Out of watches on Linux, or a drive that gives none: the slow look alone stays.
        }
    }

    /// <summary>Whether the system reports changes here at all.</summary>
    public bool Watching => _watcher is not null;

    public void Dispose() => _watcher?.Dispose();
}
