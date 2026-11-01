using StarterNG.Application;
using StarterNG.Classes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

// PresetStore keeps the disk of the first test that reaches it, a fake one as every
// composition root in the tests is built on fakes. So each test has a name to itself.
public class WarehouseEditTests
{
    private const string Text =
        "trainset a tor 0 0\nnode -1 0 v1 dynamic d s m 0 headdriver 3 0 enddynamic\nendtrainset\n";

    private static readonly string Entry = ConsistText.Serialize(new Trainset(Text));

    private readonly WarehouseEdit _edit = new();

    public WarehouseEditTests()
    {
        var installation = new TestInstallation(new InMemoryFileSystem());
        AppServices.Initialize(installation.Paths, installation.Files, installation.Clock, installation.Environment);
    }

    private static TrainsetPreset Preset(string name, string? entry = null) => new() { Name = name, Entry = entry ?? Entry };

    private static TrainsetPreset? Stored(string name) => PresetStore.All().FirstOrDefault(p => p.Name == name);

    [Fact]
    public void Opening_says_what_is_edited_and_closing_that_nothing_is()
    {
        var trainset = _edit.Open(Preset("edit-open"));

        Assert.Same(trainset, _edit.Trainset);
        Assert.Equal("edit-open", _edit.Name);

        _edit.Close();

        Assert.Null(_edit.Trainset);
        Assert.Null(_edit.Name);
    }

    [Fact]
    public void A_consist_opened_again_is_the_same_trainset_so_its_undo_history_outlives_a_switch()
    {
        var first = _edit.Open(Preset("edit-switch"));
        _edit.Open(Preset("edit-switch-other"));

        Assert.Same(first, _edit.Open(Preset("edit-switch")));
    }

    [Fact]
    public void An_entry_written_over_from_elsewhere_is_read_afresh()
    {
        var first = _edit.Open(Preset("edit-over"));

        var second = _edit.Open(Preset("edit-over", ConsistText.Serialize(new Trainset(Text.Replace(" v1 ", " v9 ")))));

        Assert.NotSame(first, second);
        Assert.Equal("v9", second.Vehicles.Single().Name);
    }

    [Fact]
    public void The_name_of_a_consist_goes_with_any_letter_case()
    {
        var first = _edit.Open(Preset("edit-case"));

        Assert.Same(first, _edit.Open(Preset("EDIT-CASE")));
    }

    [Fact]
    public void Nothing_is_written_while_the_consist_stands_as_it_was_read()
    {
        PresetStore.Save("edit-unchanged", Entry);
        _edit.Open(Stored("edit-unchanged")!);
        PresetStore.Delete("edit-unchanged");

        _edit.Save();

        Assert.Null(Stored("edit-unchanged"));
    }

    [Fact]
    public void A_change_is_written_once()
    {
        PresetStore.Save("edit-changed", Entry);
        var trainset = _edit.Open(Stored("edit-changed")!);
        trainset.Vehicles.Add(trainset.Vehicles[0].Clone());

        _edit.Save();

        Assert.Equal(ConsistText.Serialize(trainset), Stored("edit-changed")!.Entry);

        PresetStore.Delete("edit-changed");
        _edit.Save();

        Assert.Null(Stored("edit-changed"));
    }

    [Fact]
    public void Nothing_is_written_with_nothing_open()
    {
        var trainset = _edit.Open(Preset("edit-closed"));
        trainset.Vehicles.Add(trainset.Vehicles[0].Clone());
        _edit.Close();

        _edit.Save();

        Assert.Null(Stored("edit-closed"));
    }

    [Fact]
    public void A_rename_keeps_the_trainset_and_saves_under_the_new_name()
    {
        var trainset = _edit.Open(Preset("edit-old"));

        _edit.Renamed("edit-old", "edit-new");

        Assert.Equal("edit-new", _edit.Name);
        trainset.Vehicles.Add(trainset.Vehicles[0].Clone());
        _edit.Save();
        Assert.Null(Stored("edit-old"));
        Assert.Equal(ConsistText.Serialize(trainset), Stored("edit-new")!.Entry);

        Assert.Same(trainset, _edit.Open(Preset("edit-new", Stored("edit-new")!.Entry)));
    }

    [Fact]
    public void Renaming_another_consist_leaves_the_one_open_alone()
    {
        var other = _edit.Open(Preset("edit-other"));
        var open = _edit.Open(Preset("edit-mine"));

        _edit.Renamed("edit-other", "edit-other-new");

        Assert.Same(open, _edit.Trainset);
        Assert.Equal("edit-mine", _edit.Name);
        Assert.Same(other, _edit.Open(Preset("edit-other-new")));
    }
}
