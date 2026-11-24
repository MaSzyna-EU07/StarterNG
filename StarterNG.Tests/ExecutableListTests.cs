using StarterNG.Views;

namespace StarterNG.Tests;

public class ExecutableListTests
{
    [Fact]
    public void A_build_copied_in_joins_the_list_and_the_choice_stays()
    {
        var (entries, chosen) = ExecutableList.Refreshed(["eu07_new.exe", "eu07.exe"], "eu07.exe");

        Assert.Equal(new[] { "eu07_new.exe", "eu07.exe" }, entries);
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void Automatic_stays_automatic()
    {
        var (entries, chosen) = ExecutableList.Refreshed(["eu07_new.exe", "eu07.exe"], null);

        Assert.Equal(new[] { "eu07_new.exe", "eu07.exe" }, entries);
        Assert.Equal(-1, chosen);
    }

    [Fact]
    public void A_simulator_picked_from_elsewhere_stays_on_the_list_and_chosen()
    {
        const string picked = @"D:\build\eu07.exe";

        var (entries, chosen) = ExecutableList.Refreshed(["eu07.exe"], picked);

        Assert.Equal(new[] { "eu07.exe", picked }, entries);
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void A_chosen_file_since_removed_stays_chosen_rather_than_switch_silently()
    {
        var (entries, chosen) = ExecutableList.Refreshed(["eu07.exe"], "eu07_old.exe");

        Assert.Equal(new[] { "eu07.exe", "eu07_old.exe" }, entries);
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void The_choice_is_found_whatever_the_case_of_its_name()
    {
        var (entries, chosen) = ExecutableList.Refreshed(["EU07.exe"], "eu07.exe");

        Assert.Equal(new[] { "EU07.exe" }, entries);
        Assert.Equal(0, chosen);
    }
}
