using StarterNG.Views;
using Unit = StarterNG.Views.ConsistNarration.Unit;

namespace StarterNG.Tests;

public class ConsistNarrationTests
{
    // Each text shows its key and its arguments, so a test sees which sentence was picked and with what.
    private static string Loc(string key) => key switch
    {
        "ConsistSaidEmpty" => "empty",
        "ConsistSaidAdded" => "added {0} at {1}/{2}",
        "ConsistSaidRemoved" => "removed {0}, {1} left",
        "ConsistSaidTurned" => "turned {0}",
        "ConsistSaidUnturned" => "unturned {0}",
        "ConsistSaidTurnedMany" => "turned {0}",
        "ConsistSaidMoved" => "moved {0} to {1}/{2}",
        "ConsistSaidReordered" => "reordered",
        "ConsistSaidReplaced" => "replaced {0} with {1}",
        "ConsistSaidChangedMany" => "changed {0}, {1} now",
        _ => throw new KeyNotFoundException(key)
    };

    private static Unit[] Units(params string[] labels) =>
        labels.Select(l => new Unit(l.TrimEnd('^'), l.EndsWith('^'))).ToArray();

    private static string? Say(string[] before, string[] after, int? vehicles = null) =>
        ConsistNarration.Describe(Units(before), Units(after), vehicles ?? after.Length, Loc);

    [Fact]
    public void Nothing_changed_says_nothing() =>
        Assert.Null(Say(["EU07", "Bdhpumn"], ["EU07", "Bdhpumn"]));

    [Fact]
    public void An_empty_consist_staying_empty_says_nothing() =>
        Assert.Null(Say([], []));

    [Fact]
    public void Clearing_the_consist_says_it_is_empty() =>
        Assert.Equal("empty", Say(["EU07", "Bdhpumn"], []));

    [Fact]
    public void A_vehicle_added_at_the_end_is_named_with_its_place() =>
        Assert.Equal("added Bdhpumn at 2/2", Say(["EU07"], ["EU07", "Bdhpumn"]));

    [Fact]
    public void A_vehicle_added_in_the_middle_is_named_with_its_place() =>
        Assert.Equal("added WRmnouz at 2/3", Say(["EU07", "Bdhpumn"], ["EU07", "WRmnouz", "Bdhpumn"]));

    [Fact]
    public void Two_vehicles_added_at_once_are_both_named() =>
        Assert.Equal("added A, B at 1/3", Say(["EU07"], ["A", "B", "EU07"]));

    [Fact]
    public void A_removed_vehicle_is_named_with_the_count_left() =>
        Assert.Equal("removed WRmnouz, 2 left", Say(["EU07", "WRmnouz", "Bdhpumn"], ["EU07", "Bdhpumn"]));

    [Fact]
    public void The_count_left_counts_vehicles_not_units() =>
        Assert.Equal("removed EU07, 3 left", Say(["EU07", "EN57"], ["EN57"], vehicles: 3));

    [Fact]
    public void A_vehicle_turned_round_is_named() =>
        Assert.Equal("turned Bdhpumn", Say(["EU07", "Bdhpumn"], ["EU07", "Bdhpumn^"]));

    [Fact]
    public void A_vehicle_turned_back_is_told_apart_from_one_turned() =>
        Assert.Equal("unturned Bdhpumn", Say(["EU07", "Bdhpumn^"], ["EU07", "Bdhpumn"]));

    [Fact]
    public void Several_turned_at_once_are_counted() =>
        Assert.Equal("turned 3", Say(["A", "B", "C"], ["A^", "B^", "C^"]));

    [Fact]
    public void A_vehicle_moved_towards_the_end_is_named_with_its_new_place() =>
        Assert.Equal("moved A to 3/4", Say(["A", "B", "C", "D"], ["B", "C", "A", "D"]));

    [Fact]
    public void A_vehicle_moved_towards_the_front_is_named_with_its_new_place() =>
        Assert.Equal("moved C to 1/4", Say(["A", "B", "C", "D"], ["C", "A", "B", "D"]));

    [Fact]
    public void Neighbours_swapped_are_told_as_one_moved() =>
        Assert.Equal("moved A to 2/2", Say(["A", "B"], ["B", "A"]));

    [Fact]
    public void The_same_vehicles_shuffled_are_told_as_a_new_order() =>
        Assert.Equal("reordered", Say(["A", "B", "C", "D"], ["B", "A", "D", "C"]));

    [Fact]
    public void One_vehicle_swapped_for_another_names_both() =>
        Assert.Equal("replaced B with X", Say(["A", "B", "C"], ["A", "X", "C"]));

    [Fact]
    public void A_bigger_change_counts_the_vehicles_it_touched() =>
        Assert.Equal("changed 2, 3 now", Say(["A", "B", "C"], ["A", "X", "Y"]));

    [Fact]
    public void Same_named_vehicles_next_to_each_other_still_count_one_added() =>
        Assert.Equal("added Bdhpumn at 3/3", Say(["EU07", "Bdhpumn"], ["EU07", "Bdhpumn", "Bdhpumn"]));
}
