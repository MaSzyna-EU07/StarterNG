using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class BinaryTerrainFilesTests
{
    private const string Record = "user/sbt-cleanup.txt";

    private static BinaryTerrainFiles Build(InMemoryFileSystem files, string root = "game") =>
        new(files, new GamePaths(root), TestLog.Over(files), Record);

    private static string Scenery(string name) => $"game/scenery/{name}";

    [Fact]
    public void An_sbt_whose_terrain_files_are_all_there_goes()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("kato.scn"), "include kato/kato_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("kato/kato_ter.scm"), "node")
            .WithFile(Scenery("kato.sbt"), "EU07SBT2");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Equal("kato.sbt", Assert.Single(cleanup.Deleted));
        Assert.False(files.FileExists(Scenery("kato.sbt")));
    }

    [Fact]
    public void An_sbt_standing_in_for_missing_terrain_files_stays()
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile(Scenery("Lk2_Sandbox.scn"), "include nmt/nmt100_mazowieckie_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("lk2_sandbox.sbt"), "EU07SBT2");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Empty(cleanup.Deleted);
        Assert.Equal(("lk2_sandbox.sbt", "nmt/nmt100_mazowieckie_ter.scm"), Assert.Single(cleanup.Kept));
        Assert.True(files.FileExists(Scenery("lk2_sandbox.sbt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Terrain_included_through_another_file_is_looked_for_too(bool present)
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "include td/base.inc end\nFirstInit\n")
            .WithLegacyFile(Scenery("td/base.inc"), "include td/td_ter.scm 1 2 end\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");
        if (present)
            files.WithFile(Scenery("td/td_ter.scm"), "node");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Equal(present, cleanup.Deleted.Count == 1);
        Assert.Equal(present, !files.FileExists(Scenery("td.sbt")));
    }

    [Fact]
    public void A_missing_include_other_than_terrain_does_not_keep_the_sbt()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("tomaszewo.scn"), "include tomaszewo/modele.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("tomaszewo/modele.scm"),
                            "include;przejazdy_kombud\\1t12m_0rog_2zar_1x_1dzw.inc;p_czernica15;0;0;0;end\n")
            .WithFile(Scenery("tomaszewo.sbt"), "EU07SBT2");

        Assert.Single(Build(files).CleanUpOnce(1)!.Deleted);
    }

    [Fact]
    public void An_include_commented_out_needs_no_file()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "// include gone_ter.scm end\n/* include gone.inc end */\nFirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Single(Build(files).CleanUpOnce(1)!.Deleted);
    }

    [Fact]
    public void A_scenery_bringing_terrain_by_a_terrain_directive_keeps_its_sbt()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "terrain td.sbt endterrain\nFirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Equal("td.sbt", Assert.Single(Build(files).CleanUpOnce(1)!.Kept).File);
    }

    [Fact]
    public void An_sbt_of_no_scenery_stays()
    {
        var files = new InMemoryFileSystem()
            .WithFile(Scenery("old.sbt"), "EU07SBT2");

        Assert.Equal(("old.sbt", "old.scn"), Assert.Single(Build(files).CleanUpOnce(1)!.Kept));
    }

    [Fact]
    public void Each_generation_clears_an_installation_once()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "FirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");
        var sbt = Build(files);

        Assert.NotNull(sbt.CleanUpOnce(1));
        files.WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Null(sbt.CleanUpOnce(1));
        Assert.True(files.FileExists(Scenery("td.sbt")));

        Assert.Single(sbt.CleanUpOnce(2)!.Deleted);
    }

    [Fact]
    public void Generation_zero_clears_nothing()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "FirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Null(Build(files).CleanUpOnce(0));
        Assert.True(files.FileExists(Scenery("td.sbt")));
    }

    [Fact]
    public void Another_installation_has_a_cleanup_of_its_own()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "FirstInit\n")
            .WithLegacyFile("other/scenery/td.scn", "FirstInit\n")
            .WithFile("other/scenery/td.sbt", "EU07SBT2");

        Assert.NotNull(Build(files).CleanUpOnce(1));

        Assert.Single(Build(files, "other").CleanUpOnce(1)!.Deleted);
    }

    [Fact]
    public void One_scenery_s_sbt_is_found_whatever_its_case_and_deleted()
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile(Scenery("Kato.scn"), "FirstInit\n")
            .WithFile(Scenery("kato.sbt"), "EU07SBT2");
        var sbt = Build(files);

        Assert.Equal(Path.Combine("game", "scenery", "kato.sbt"), sbt.Of(Scenery("Kato.scn")));
        Assert.Null(sbt.MissingOriginal(Scenery("Kato.scn")));
        Assert.True(sbt.Delete(Scenery("Kato.scn")));
        Assert.Null(sbt.Of(Scenery("Kato.scn")));
    }
}
