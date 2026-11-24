using StarterNG.Application;

namespace StarterNG.Tests;

public class OneAtATimeTests
{
    private static async Task Until(Func<bool> condition)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < giveUp, "waited too long");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Asked_once_it_runs_once()
    {
        int runs = 0;
        var once = new OneAtATime(() => { runs++; return Task.CompletedTask; });

        await once.RunAsync();

        Assert.Equal(1, runs);
        Assert.False(once.IsRunning);
    }

    [Fact]
    public async Task Asked_while_running_it_runs_once_more_after_not_alongside()
    {
        int runs = 0, together = 0, most = 0;
        var gates = new Queue<TaskCompletionSource>();
        var once = new OneAtATime(async () =>
        {
            runs++;
            most = Math.Max(most, ++together);
            var gate = new TaskCompletionSource();
            gates.Enqueue(gate);
            await gate.Task;
            together--;
        });

        var first = once.RunAsync();
        // Three requests during the first run make one more run, not three.
        await once.RunAsync();
        await once.RunAsync();
        await once.RunAsync();
        Assert.Equal(1, runs);

        gates.Dequeue().SetResult();
        await Until(() => Volatile.Read(ref runs) == 2);
        await Until(() => gates.Count == 1);
        gates.Dequeue().SetResult();
        await first;

        Assert.Equal(2, runs);
        Assert.Equal(1, most);
        Assert.False(once.IsRunning);
    }

    [Fact]
    public async Task A_failed_run_does_not_keep_it_busy()
    {
        bool fail = true;
        int runs = 0;
        var once = new OneAtATime(() =>
        {
            runs++;
            return fail ? Task.FromException(new IOException("locked")) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(once.RunAsync);
        fail = false;
        await once.RunAsync();

        Assert.Equal(2, runs);
        Assert.False(once.IsRunning);
    }
}
