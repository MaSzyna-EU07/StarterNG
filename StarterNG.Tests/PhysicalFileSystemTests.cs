using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Tests;

public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("starterng-disk-").FullName;
    private readonly PhysicalFileSystem _files = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void An_atomic_write_makes_or_replaces_the_file_and_leaves_nothing_beside_it()
    {
        string path = Path.Combine(_root, "file.json");

        _files.WriteAllTextAtomic(path, "first");
        _files.WriteAllTextAtomic(path, "zażółć");

        Assert.Equal("zażółć", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
    }

    [Fact]
    public void An_atomic_write_that_cannot_finish_leaves_what_stood_there()
    {
        string path = Path.Combine(_root, "file.json");
        File.WriteAllText(path, "what stood there");
        // Nowhere to put the new text on its way in.
        Directory.CreateDirectory(path + PhysicalFileSystem.TempSuffix);

        Assert.ThrowsAny<Exception>(() => _files.WriteAllTextAtomic(path, "half"));

        Assert.Equal("what stood there", File.ReadAllText(path));
    }
}
