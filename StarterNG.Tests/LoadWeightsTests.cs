using StarterNG.Classes;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class LoadWeightsTests
{
    private static LoadWeightsRepository Repository(InMemoryFileSystem files)
    {
        var installation = new TestInstallation(files);
        return new LoadWeightsRepository(files, installation.Paths, installation.Log);
    }

    [Fact]
    public void Weights_are_read_from_the_pairs_in_the_file()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("data", "load_weights.txt"),
                      "{\n  coal : 850 # węgiel\n  sand : 1600\n}");

        var table = Repository(files).Table;

        Assert.Equal(850, table.WeightOf("coal"));
        Assert.Equal(1600, table.WeightOf("SAND"));
    }

    [Fact]
    public void An_unknown_cargo_falls_back_to_the_default_weight()
    {
        Assert.Equal(LoadWeightsTable.DefaultWeight,
                     Repository(new InMemoryFileSystem()).Table.WeightOf("nieznany"));
    }

    [Fact]
    public void The_pantograph_pseudo_load_weighs_nothing()
    {
        Assert.Equal(0, new LoadWeightsTable().WeightOf(Dynamic.PantState));
    }
}
