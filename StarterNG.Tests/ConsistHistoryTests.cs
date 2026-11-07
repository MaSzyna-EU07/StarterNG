using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Tests;

public class ConsistHistoryTests
{
    private static Dynamic Car(string name) => new()
    {
        Name = name, DataFolder = "pkp", SkinFile = name, MmdFile = name, PathName = ""
    };

    private static Trainset With(params string[] names) =>
        new("trainset none none 0 0\nendtrainset") { Vehicles = names.Select(Car).ToList() };

    private static string[] Names(IEnumerable<Dynamic> vehicles) => vehicles.Select(v => v.Name).ToArray();

    [Fact]
    public void A_redraw_without_a_change_leaves_nothing_to_undo()
    {
        var history = new ConsistHistory();
        var trainset = With("a", "b");

        history.Record(trainset, 0);
        history.Record(trainset, 1);

        Assert.False(history.CanUndo(trainset));
    }

    [Fact]
    public void Undo_goes_back_one_change_and_redo_forward_again()
    {
        var history = new ConsistHistory();
        var trainset = With("a");
        history.Record(trainset, 0);
        trainset.Vehicles.Add(Car("b"));
        history.Record(trainset, 1);

        var back = history.Undo(trainset, 1);

        Assert.Equal(new[] { "a" }, Names(back!.Vehicles));
        Assert.True(history.CanRedo(trainset));

        trainset.Vehicles = back.Vehicles.ToList();
        history.Record(trainset, 0, rebase: true);
        var forward = history.Redo(trainset, 0);

        Assert.Equal(new[] { "a", "b" }, Names(forward!.Vehicles));
        Assert.Equal(1, forward.Selected);
    }

    [Fact]
    public void An_edit_made_in_place_is_still_undone()
    {
        // The vehicles are shared with the editor, so a change to one of them must
        // not reach the state kept for undo.
        var history = new ConsistHistory();
        var trainset = With("a");
        history.Record(trainset, 0);

        trainset.Vehicles[0].DriverType = eDriverType.Passenger;
        var back = history.Undo(trainset, 0);

        Assert.NotEqual(eDriverType.Passenger, back!.Vehicles[0].DriverType);
    }

    [Fact]
    public void A_new_change_drops_what_could_be_redone()
    {
        var history = new ConsistHistory();
        var trainset = With("a");
        history.Record(trainset, 0);
        trainset.Vehicles.Add(Car("b"));
        history.Record(trainset, 1);
        trainset.Vehicles = history.Undo(trainset, 1)!.Vehicles.ToList();
        history.Record(trainset, 0, rebase: true);

        trainset.Vehicles.Add(Car("c"));
        history.Record(trainset, 1);

        Assert.False(history.CanRedo(trainset));
    }

    [Fact]
    public void Each_trainset_has_its_own_history()
    {
        var history = new ConsistHistory();
        var first = With("a");
        var second = With("x");
        history.Record(first, 0);
        first.Vehicles.Add(Car("b"));
        history.Record(first, 1);
        history.Record(second, 0);

        Assert.True(history.CanUndo(first));
        Assert.False(history.CanUndo(second));
    }
}
