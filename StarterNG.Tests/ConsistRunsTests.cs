using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Tests;

public class ConsistRunsTests
{
    private static List<Dynamic> Vehicles(params string[] skins) =>
        new Trainset(string.Concat(
            "trainset a tor 0 0\n",
            string.Concat(skins.Select((s, i) => $"node -1 0 v{i} dynamic d {s} m 0 nobody 3 0 enddynamic\n")),
            "endtrainset")).Vehicles;

    private static List<(string Skin, int Count)> Runs(List<Dynamic> vehicles, Func<Dynamic, int, string?> keyOf) =>
        ConsistRuns.Of(vehicles, keyOf).Select(r => (r.First.SkinFile!, r.Count)).ToList();

    [Fact]
    public void Vehicles_alike_next_to_each_other_make_one_run()
    {
        var vehicles = Vehicles("eu07", "412w", "412w", "412w", "eaos", "eaos");

        Assert.Equal(
            [("eu07", 1), ("412w", 3), ("eaos", 2)],
            Runs(vehicles, (v, _) => v.SkinFile));
    }

    [Fact]
    public void Vehicles_alike_apart_stay_apart()
    {
        var vehicles = Vehicles("412w", "eaos", "412w");

        Assert.Equal(
            [("412w", 1), ("eaos", 1), ("412w", 1)],
            Runs(vehicles, (v, _) => v.SkinFile));
    }

    [Fact]
    public void Alike_goes_by_the_key_whatever_its_case()
    {
        var vehicles = Vehicles("412W", "412w");

        Assert.Equal([("412W", 2)], Runs(vehicles, (v, _) => v.SkinFile));
    }

    [Fact]
    public void A_vehicle_with_no_key_stands_alone()
    {
        var vehicles = Vehicles("x", "x", "x");

        Assert.Equal(
            [("x", 1), ("x", 1), ("x", 1)],
            Runs(vehicles, (_, _) => null));
    }

    [Fact]
    public void The_place_in_the_consist_can_set_a_vehicle_apart()
    {
        var vehicles = Vehicles("sm42", "sm42", "sm42");

        Assert.Equal(
            [("sm42", 1), ("sm42", 2)],
            Runs(vehicles, (v, i) => i == 0 ? "head" : v.SkinFile));
    }

    [Fact]
    public void No_vehicles_no_runs() =>
        Assert.Empty(ConsistRuns.Of([], (v, _) => v.SkinFile));
}
