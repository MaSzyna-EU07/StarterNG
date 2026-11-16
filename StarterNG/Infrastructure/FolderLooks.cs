using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using StarterNG.Application;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure;

/// <summary>
/// Looks at a folder's files for a window, one look at a time on the UI thread: when the system
/// reports a change, once the burst a save makes has passed; when the window comes back to the
/// front; when asked; and now and then anyway, for the drives and systems where the reports do
/// not come - often where none come at all. From the window's opening to its closing.
/// </summary>
public sealed class FolderLooks
{
    /// <summary>How long the reports have to stop for: a save comes as several of them.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);

    /// <summary>How often it looks anyway where the system reports the changes.</summary>
    public static readonly TimeSpan Watched = TimeSpan.FromSeconds(30);

    /// <summary>How often it looks where the system reports nothing.</summary>
    public static readonly TimeSpan Unwatched = TimeSpan.FromSeconds(2);

    private readonly Func<string> _folder;
    private readonly OneAtATime _look;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private bool _closed;
    private ITimer? _quiet;
    private ITimer? _often;
    private FolderChanges? _changes;

    /// <param name="window">Looked for from its opening to its closing.</param>
    /// <param name="folder">The folder, as it is when the window opens.</param>
    /// <param name="look">The look itself; it knows when it is no time to look.</param>
    /// <param name="time">The clock of the timers, the system's when not given.</param>
    public FolderLooks(Window window, Func<string> folder, Func<Task> look, TimeProvider? time = null)
    {
        _folder = folder;
        _look = new OneAtATime(look);
        _time = time ?? TimeProvider.System;
        window.Opened += (_, _) => Watch();
        window.Activated += async (_, _) => await LookAsync();
        window.Closed += (_, _) => Close();
    }

    /// <summary>Whether the system reports the folder's changes; not before the window opens.</summary>
    public bool Watching => _changes?.Watching == true;

    /// <summary>Looks now, or once more after the look that runs.</summary>
    public Task LookAsync() => _look.RunAsync();

    private void Watch()
    {
        var changes = new FolderChanges(_folder());
        var often = changes.Watching ? Watched : Unwatched;
        lock (_gate)
        {
            _changes = changes;
            _quiet = _time.CreateTimer(_ => LookLater(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _often = _time.CreateTimer(_ => LookLater(), null, often, often);
        }
        changes.Changed += PutOff;
    }

    // Each report puts the look off again, so a save of many files is one look after it. The
    // reports come on threads of the pool, and may come still as the window closes.
    private void PutOff()
    {
        lock (_gate)
        {
            if (!_closed)
                _quiet?.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    // The timers tick on threads of the pool; the look belongs on the UI thread.
    private void LookLater() => Dispatcher.UIThread.Post(() => _ = LookAsync());

    private void Close()
    {
        FolderChanges? changes;
        lock (_gate)
        {
            _closed = true;
            _quiet?.Dispose();
            _often?.Dispose();
            changes = _changes;
        }
        changes?.Dispose();
    }
}
