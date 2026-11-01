using StarterNG.Domain.Sceneries;

namespace StarterNG.Tests;

public class SceneryFaultTextTests
{
    private static string Wording(string key) => key switch
    {
        "SyntaxLost" => "lost: {0}",
        "SyntaxUnmade" => "unmade: {0}",
        "SyntaxMore" => "and {0} more",
        _ => key + " {0} {1}"
    };

    private static string Format(int limit, params SceneryFault[] faults) =>
        SceneryFaultText.Format(faults, limit, Wording);

    [Fact]
    public void No_faults_read_as_nothing() =>
        Assert.Equal("", Format(10));

    [Fact]
    public void The_place_and_the_fault_stand_in_two_columns_as_wide_as_the_longest_place()
    {
        string text = Format(10,
            new SceneryFault(SceneryFaultKind.VehicleIncomplete, "a.scn", 3, "v1", "x"),
            new SceneryFault(SceneryFaultKind.NotANumber, "long.inc", 120, "v2", "y"));

        Assert.Equal(
            "a.scn:3       SyntaxVehicleIncomplete v1 x\n" +
            "long.inc:120  SyntaxNotANumber v2 y",
            text);
    }

    [Fact]
    public void What_a_fault_has_no_subject_or_token_for_is_filled_in()
    {
        string text = Format(10, new SceneryFault(SceneryFaultKind.StrayEndTrainset, "a.scn", 3));

        Assert.Equal("a.scn:3  SyntaxStrayEndTrainset ?", text);
    }

    [Fact]
    public void What_a_fault_costs_stands_under_it_in_the_second_column()
    {
        string text = Format(10,
            new SceneryFault(SceneryFaultKind.VehicleNotClosed, "a.scn", 3, "v1", "x") { Lost = "v2, v3", Unmade = "b" });

        Assert.Equal(
            "a.scn:3  SyntaxVehicleNotClosed v1 x\n" +
            "         lost: v2, v3\n" +
            "         unmade: b",
            text);
    }

    [Fact]
    public void Past_the_limit_the_faults_are_only_counted_and_do_not_widen_the_column()
    {
        string text = Format(1,
            new SceneryFault(SceneryFaultKind.NotANumber, "a.scn", 3, "v1", "x"),
            new SceneryFault(SceneryFaultKind.NotANumber, "a-longer-name.scn", 4, "v2", "y"),
            new SceneryFault(SceneryFaultKind.NotANumber, "a.scn", 5, "v3", "z"));

        Assert.Equal(
            "a.scn:3  SyntaxNotANumber v1 x\n" +
            "and 2 more",
            text);
    }

    [Fact]
    public void Exactly_as_many_as_the_limit_are_not_counted_as_more()
    {
        string text = Format(1, new SceneryFault(SceneryFaultKind.NotANumber, "a.scn", 3, "v1", "x"));

        Assert.DoesNotContain("more", text);
    }
}
