using StarterNG.Classes;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

// Over the fake disk throughout: PresetStore itself is the player's real file.
public class PresetFileTests
{
    private const string PresetsPath = "/home/user/.config/MaSzyna/starter/userpresets.json";

    private const string ThreePresets = """
        { "Presets": [
          { "Name": "a", "Entry": "trainset a" },
          { "Name": "b", "Entry": "trainset b" },
          { "Name": "c", "Entry": "trainset c" }
        ] }
        """;

    private static (PresetFile Presets, InMemoryFileSystem Files, FileDiagnosticsLog Log) Rig(string? held = null)
    {
        var files = new InMemoryFileSystem();
        if (held is not null)
            files.WithFile(PresetsPath, held);
        var log = TestLog.Over(files);
        return (new PresetFile(files, log, PresetsPath), files, log);
    }

    [Fact]
    public void A_saved_preset_joins_the_ones_in_the_file()
    {
        var (presets, files, _) = Rig(ThreePresets);

        presets.Save("d", "trainset d");

        Assert.Equal(new[] { "a", "b", "c", "d" }, presets.Presets().Select(p => p.Name));
        Assert.False(files.FileExists(PresetsPath + ".unreadable"));
    }

    [Fact]
    public void Saving_under_a_name_there_replaces_that_one()
    {
        var (presets, _, _) = Rig(ThreePresets);

        presets.Save("B", "trainset other");

        Assert.Equal(new[] { "a", "B", "c" }, presets.Presets().Select(p => p.Name));
    }

    [Fact]
    public void A_first_preset_needs_no_file_to_be_there()
    {
        var (presets, files, log) = Rig();

        presets.Save("a", "trainset a");

        Assert.Equal("a", Assert.Single(presets.Presets()).Name);
        Assert.False(files.FileExists(PresetsPath + ".unreadable"));
        Assert.Equal(string.Empty, log.Text);
    }

    [Theory]
    [InlineData("""{ "Presets": [ { "Name": "a", "Entry": "trainset a" }, { "Na""")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "Presets": null }""")]
    public void A_file_cut_short_goes_aside_before_a_save_replaces_it(string held)
    {
        var (presets, files, log) = Rig(held);

        presets.Save("new", "trainset new");

        Assert.Equal(held, files.ReadAllText(PresetsPath + ".unreadable"));
        Assert.Equal("new", Assert.Single(presets.Presets()).Name);
        Assert.Contains(PresetsPath + ".unreadable", log.Text);
    }

    [Fact]
    public void An_earlier_copy_set_aside_is_not_overwritten()
    {
        var (presets, files, _) = Rig("{ cut");
        files.WithFile(PresetsPath + ".unreadable", "older");

        presets.Save("new", "trainset new");

        Assert.Equal("older", files.ReadAllText(PresetsPath + ".unreadable"));
        Assert.Equal("{ cut", files.ReadAllText(PresetsPath + ".unreadable-2"));
    }

    [Fact]
    public void A_locked_file_is_left_alone_when_no_copy_of_it_can_be_made()
    {
        var (presets, files, log) = Rig(ThreePresets);
        files.FailReading(PresetsPath);

        presets.Save("new", "trainset new");

        files.AllowReading(PresetsPath);
        Assert.Equal(ThreePresets, files.ReadAllText(PresetsPath));
        Assert.False(files.FileExists(PresetsPath + ".unreadable"));
        Assert.Contains("saving", log.Text);
    }

    [Fact]
    public void A_file_unlocked_between_the_read_and_the_write_is_still_kept()
    {
        // The list in hand is the empty one from the read that failed: the file has
        // to be kept whatever it looks like by the time it would be replaced.
        var (presets, files, _) = Rig(ThreePresets);
        var unlocking = new UnlockingLog(files, PresetsPath);
        var racing = new PresetFile(files, unlocking, PresetsPath);
        files.FailReading(PresetsPath);

        racing.Save("new", "trainset new");

        Assert.Equal(ThreePresets, files.ReadAllText(PresetsPath + ".unreadable"));
        Assert.Equal("new", Assert.Single(presets.Presets()).Name);
    }

    [Fact]
    public void Deleting_from_an_unreadable_file_leaves_it_as_it_is()
    {
        var (presets, files, _) = Rig("{ cut");

        presets.Delete("a");

        Assert.Equal("{ cut", files.ReadAllText(PresetsPath));
        Assert.False(files.FileExists(PresetsPath + ".unreadable"));
    }

    [Fact]
    public void An_unreadable_file_shows_as_no_presets_and_is_logged()
    {
        var (presets, _, log) = Rig("{ cut");

        Assert.Empty(presets.Presets());
        Assert.Contains("reading", log.Text);
    }

    /// <summary>Lets the file go the moment its read has failed and been logged.</summary>
    private sealed class UnlockingLog : StarterNG.Application.Abstractions.IDiagnosticsLog
    {
        private readonly InMemoryFileSystem _files;
        private readonly string _path;

        public UnlockingLog(InMemoryFileSystem files, string path)
        {
            _files = files;
            _path = path;
        }

        public string LogPath => "";
        public string Text => "";
        public void Log(string message) => _files.AllowReading(_path);
        public void Log(string context, Exception exception) => _files.AllowReading(_path);
        public void Log(IEnumerable<string> messages) => _files.AllowReading(_path);
        public void Clear() { }
        public void Flush() { }
    }
}
