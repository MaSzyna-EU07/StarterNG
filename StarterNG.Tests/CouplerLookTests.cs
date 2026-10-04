using StarterNG.Classes;
using StarterNG.Views;

namespace StarterNG.Tests;

public class CouplerLookTests
{
    [Theory]
    [InlineData("0", CouplerState.Free)]
    [InlineData("3", CouplerState.Coupled)]
    [InlineData("7", CouplerState.MultipleUnit)]
    [InlineData("131", CouplerState.Permanent)]
    [InlineData("-3", CouplerState.Permanent)]
    [InlineData("0.BP", CouplerState.Free)]
    public void A_coupler_between_vehicles_reads_by_what_it_connects(string code, CouplerState expected) =>
        Assert.Equal(expected, CouplerLook.StateOf(Coupling.Parse(code), trailing: false));

    [Theory]
    [InlineData("0", CouplerState.EndSignals)]
    [InlineData("0.BP", CouplerState.EndSignals)]
    [InlineData("3", CouplerState.NoEndSignals)]
    [InlineData("-7", CouplerState.NoEndSignals)]
    public void The_back_of_the_train_gets_end_signals_only_at_0(string code, CouplerState expected) =>
        Assert.Equal(expected, CouplerLook.StateOf(Coupling.Parse(code), trailing: true));
}
