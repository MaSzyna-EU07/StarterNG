using Avalonia.Controls;
using StarterNG.Infrastructure;

namespace StarterNG.Tests;

[Collection(AvaloniaControls.Name)]
public sealed class FolderLooksTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "folder-looks-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new();
    private readonly Window _window;
    private int _looks;

    public FolderLooksTests()
    {
        Directory.CreateDirectory(_root);
        HeadlessDrawing.Ensure();
        _window = new Window();
    }

    public void Dispose()
    {
        _window.Close();
        Directory.Delete(_root, recursive: true);
    }

    private FolderLooks Opened(string folder)
    {
        var looks = new FolderLooks(_window, () => folder, () =>
        {
            _looks++;
            return Task.CompletedTask;
        }, _time);
        _window.Show();
        Controls.Settle();
        _looks = 0;
        return looks;
    }

    private ManualTimer Quiet => _time.Timers.Single(t => t.Period == Timeout.InfiniteTimeSpan);
    private ManualTimer Often => _time.Timers.Single(t => t.Period != Timeout.InfiniteTimeSpan);

    // Waited for without leaving the thread: the window belongs to it.
    private static void Until(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, Patience), "waited too long");

    [Fact]
    public void Nothing_is_watched_before_the_window_opens()
    {
        _ = new FolderLooks(_window, () => _root, () => Task.CompletedTask, _time);

        Assert.Empty(_time.Timers);
    }

    [Fact]
    public void A_watched_folder_is_looked_at_anyway_seldom()
    {
        var looks = Opened(_root);

        Assert.True(looks.Watching);
        Assert.Equal(FolderLooks.Watched, Often.Due);
        Assert.Equal(FolderLooks.Watched, Often.Period);
        Assert.Equal(Timeout.InfiniteTimeSpan, Quiet.Due);
    }

    [Fact]
    public void A_folder_the_system_reports_nothing_of_is_looked_at_often()
    {
        var looks = Opened(Path.Combine(_root, "nowhere"));

        Assert.False(looks.Watching);
        Assert.Equal(FolderLooks.Unwatched, Often.Period);
    }

    [Fact]
    public void A_tick_looks_on_the_ui_thread_once_it_gets_there()
    {
        Opened(_root);

        Often.Fire();
        Assert.Equal(0, _looks);
        Controls.Settle();

        Assert.Equal(1, _looks);
    }

    [Fact]
    public void A_change_is_looked_at_once_the_reports_stop()
    {
        Opened(_root);

        File.WriteAllText(Path.Combine(_root, "test.scn"), "new");
        Until(() => Quiet.Due == FolderLooks.Quiet);
        Controls.Settle();
        Assert.Equal(0, _looks);

        Quiet.Fire();
        Controls.Settle();

        Assert.Equal(1, _looks);
    }

    [Fact]
    public void Coming_back_to_the_window_looks()
    {
        Opened(_root);

        _window.Activate();
        Controls.Settle();

        Assert.Equal(1, _looks);
    }

    [Fact]
    public void Asked_it_looks()
    {
        var looks = Opened(_root);

        Assert.True(looks.LookAsync().IsCompletedSuccessfully);

        Assert.Equal(1, _looks);
    }

    [Fact]
    public void Closed_it_stops_the_timers()
    {
        Opened(_root);

        _window.Close();

        Assert.Equal(2, _time.Timers.Count);
        Assert.All(_time.Timers, t => Assert.True(t.Disposed));
    }

    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();

        public IReadOnlyList<ManualTimer> Timers
        {
            get
            {
                lock (_timers)
                    return _timers.ToList();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(() => callback(state), dueTime, period);
            lock (_timers)
                _timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly Action _tick;
        private long _dueTicks;

        public ManualTimer(Action tick, TimeSpan due, TimeSpan period)
        {
            _tick = tick;
            _dueTicks = due.Ticks;
            Period = period;
        }

        public TimeSpan Due => TimeSpan.FromTicks(Interlocked.Read(ref _dueTicks));
        public TimeSpan Period { get; private set; }
        public bool Disposed { get; private set; }

        public void Fire() => _tick();

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(ManualTimer));
            Interlocked.Exchange(ref _dueTicks, dueTime.Ticks);
            Period = period;
            return true;
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
