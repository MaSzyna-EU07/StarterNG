using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Tests;

public sealed class FolderChangesTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "folder-changes-" + Guid.NewGuid().ToString("N"));

    public FolderChangesTests() => Directory.CreateDirectory(Path.Combine(_root, "sub"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static async Task<bool> Reported(FolderChanges changes, Action change)
    {
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changes.Changed += () => told.TrySetResult();
        change();
        return await Task.WhenAny(told.Task, Task.Delay(Patience)) == told.Task;
    }

    [Fact]
    public async Task A_file_written_below_the_folder_is_reported()
    {
        string file = Path.Combine(_root, "sub", "a.scm");
        File.WriteAllText(file, "old");
        using var changes = new FolderChanges(_root);

        Assert.True(changes.Watching);
        Assert.True(await Reported(changes, () => File.WriteAllText(file, "new text")));
    }

    [Fact]
    public async Task A_save_through_a_renamed_temp_file_is_reported()
    {
        string file = Path.Combine(_root, "test.scn");
        File.WriteAllText(file, "old");
        using var changes = new FolderChanges(_root);

        Assert.True(await Reported(changes, () =>
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, "new");
            File.Move(temp, file, overwrite: true);
        }));
    }

    [Fact]
    public async Task A_file_removed_is_reported()
    {
        string file = Path.Combine(_root, "gone.scn");
        File.WriteAllText(file, "x");
        using var changes = new FolderChanges(_root);

        Assert.True(await Reported(changes, () => File.Delete(file)));
    }

    [Fact]
    public void A_missing_folder_is_not_watched_and_does_not_throw()
    {
        using var changes = new FolderChanges(Path.Combine(_root, "nowhere"));

        Assert.False(changes.Watching);
    }
}
