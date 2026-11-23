using System.Reflection;
using StarterNG.Application.Abstractions;
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

    [Theory]
    [InlineData("td/teren.txtf")]
    [InlineData("td/Teren.btf")]
    public void An_sbt_in_the_way_of_terrain_files_that_are_there_goes(string onDisk)
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile(Scenery("td.scn"), "terrain td/teren.txtf endterrain\nFirstInit\n")
            .WithFile(Scenery(onDisk), "x")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Single(Build(files).CleanUpOnce(1)!.Deleted);
        Assert.False(files.FileExists(Scenery("td.sbt")));
    }

    [Fact]
    public void An_sbt_standing_in_for_a_missing_terrain_file_stays()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "terrain td/a.txtf td/b.txtf endterrain\nFirstInit\n")
            .WithFile(Scenery("td/a.txtf"), "x")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Equal(("td.sbt", "td/b.txtf"), Assert.Single(Build(files).CleanUpOnce(1)!.Kept));
    }

    [Fact]
    public void An_sbt_named_beside_terrain_files_keeps_the_scenery_s_own()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "terrain td/a.txtf wspolny.sbt endterrain\nFirstInit\n")
            .WithFile(Scenery("td/a.txtf"), "x")
            .WithFile(Scenery("wspolny.sbt"), "EU07SBT2")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Empty(cleanup.Deleted);
        Assert.Contains(("td.sbt", "terrain wspolny.sbt"), cleanup.Kept);
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

    // With an .sbt there the simulator passes over the triangles and lines of every file, not
    // only of those named *_ter.scm: a scenery file that is gone took its shapes with it.
    [Theory]
    [InlineData("td/teren.scm", true)]
    [InlineData("td/teren.scm", false)]
    [InlineData("TD\\Czesc.SCN", false)]
    public void A_missing_scenery_file_keeps_the_sbt_whatever_its_name(string include, bool present)
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), $"include td/base.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("td/base.scm"), $"include {include} 1 2 end\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");
        if (present)
            files.WithLegacyFile(Scenery(include), "node -1 0 none triangles grass 0 0 0 endtri\n");

        var cleanup = Build(files).CleanUpOnce(1)!;

        if (present)
            Assert.Equal("td.sbt", Assert.Single(cleanup.Deleted));
        else
            Assert.Equal(("td.sbt", include), Assert.Single(cleanup.Kept));
        Assert.Equal(present, !files.FileExists(Scenery("td.sbt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_terrain_file_that_only_lists_others_is_read_for_them(bool present)
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "include td/citygml_ter.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("td/citygml_ter.scm"), "include td/citygml/n-34.scm end\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");
        if (present)
            files.WithFile(Scenery("td/citygml/n-34.scm"), "node");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Equal(present, cleanup.Deleted.Count == 1);
        if (!present)
            Assert.Equal(("td.sbt", "td/citygml/n-34.scm"), Assert.Single(cleanup.Kept));
    }

    [Fact]
    public void A_large_terrain_file_is_not_opened()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "include td/td_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("td/td_ter.scm"), new string(' ', 1024 * 1024) + "include td/gone.scm end\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Single(Build(files).CleanUpOnce(1)!.Deleted);
    }

    // "terrain" starts a directive only where the simulator looks for one. As the name of a
    // node or of a texture it starts nothing, and the includes after it count as any other.
    [Theory]
    [InlineData("node -1 0 terrain triangles grass 0 0 0 endtri")]
    [InlineData("node -1 0 none triangles terrain 0 0 0 endtri")]
    public void A_word_reading_terrain_hides_no_include_after_it(string node)
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), $"{node}\ninclude td/gone_ter.scm end\ninclude td/more.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("td/more.scm"), "include td/gone.scm end\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Equal(("td.sbt", "td/gone_ter.scm"), Assert.Single(Build(files).CleanUpOnce(1)!.Kept));

        files.WithFile(Scenery("td/gone_ter.scm"), "node");

        var again = Build(files).CleanUpOnce(2)!;

        Assert.Equal(("td.sbt", "td/gone.scm"), Assert.Single(again.Kept));
    }

    [Fact]
    public void A_word_reading_terrain_alone_does_not_keep_the_sbt()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"),
                            "node -1 0 terrain triangles grass 0 0 0 endtri\ninclude td/td_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("td/td_ter.scm"), "node")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Single(Build(files).CleanUpOnce(1)!.Deleted);
    }

    [Fact]
    public void A_terrain_directive_is_still_read_after_a_word_reading_terrain()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"),
                            "node -1 0 terrain triangles grass 0 0 0 endtri\nterrain td/a.txtf endterrain\nFirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Equal(("td.sbt", "td/a.txtf"), Assert.Single(Build(files).CleanUpOnce(1)!.Kept));
    }

    // An .sbt serves every scenery naming it in a terrain directive, not only the one it is
    // named after. With it gone such a scenery is read from its own text, until the simulator
    // writes the file again on a load of the scenery it belongs to.
    [Theory]
    [InlineData("wspolny.sbt")]
    [InlineData("Wspolny.SBT")]
    [InlineData("$wspolny.sbt")]
    public void An_sbt_another_scenery_cannot_do_without_stays(string named)
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("wspolny.scn"), "include wspolny/wspolny_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("wspolny/wspolny_ter.scm"), "node")
            .WithFile(Scenery("wspolny.sbt"), "EU07SBT2")
            .WithLegacyFile(Scenery("nocny.scn"), $"include nocny/base.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("nocny/base.scm"), $"terrain {named} endterrain\ninclude nmt/nmt100_ter.scm end\n");
        var sbt = Build(files);

        Assert.Equal("nmt/nmt100_ter.scm (nocny.scn)", sbt.MissingOriginal(Scenery("wspolny.scn")));

        var cleanup = sbt.CleanUpOnce(1)!;

        Assert.Empty(cleanup.Deleted);
        Assert.Equal(("wspolny.sbt", "nmt/nmt100_ter.scm (nocny.scn)"), Assert.Single(cleanup.Kept));
        Assert.True(files.FileExists(Scenery("wspolny.sbt")));
    }

    [Fact]
    public void An_sbt_another_scenery_can_do_without_goes()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("wspolny.scn"), "include wspolny/wspolny_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("wspolny/wspolny_ter.scm"), "node")
            .WithFile(Scenery("wspolny.sbt"), "EU07SBT2")
            .WithLegacyFile(Scenery("nocny.scn"),
                            "terrain wspolny.sbt endterrain\ninclude wspolny/wspolny_ter.scm end\nFirstInit\n");
        var sbt = Build(files);

        Assert.Null(sbt.MissingOriginal(Scenery("wspolny.scn")));
        Assert.Equal("wspolny.sbt", Assert.Single(sbt.CleanUpOnce(1)!.Deleted));
    }

    [Fact]
    public void An_sbt_of_the_same_name_in_another_folder_is_another_file()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("teren.scn"), "FirstInit\n")
            .WithFile(Scenery("teren.sbt"), "EU07SBT2")
            .WithFile(Scenery("l107/teren.sbt"), "EU07SBT2")
            .WithLegacyFile(Scenery("lupkow.scn"),
                            "terrain l107/teren.sbt endterrain\ninclude nmt/nmt100_ter.scm end\nFirstInit\n");

        var cleanup = Build(files).CleanUpOnce(1)!;

        Assert.Equal("teren.sbt", Assert.Single(cleanup.Deleted));
        Assert.True(files.FileExists(Scenery("l107/teren.sbt")));
    }

    // The simulator drops the $ a scenery editor puts ahead of the name of a changed scenery,
    // and reads and writes the .sbt of the one it was made from.
    [Fact]
    public void An_sbt_also_serves_the_scenery_named_with_a_dollar_ahead()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("td.scn"), "FirstInit\n")
            .WithLegacyFile(Scenery("$td.scn"), "include td/nowy_ter.scm end\nFirstInit\n")
            .WithFile(Scenery("td.sbt"), "EU07SBT2");

        Assert.Equal(("td.sbt", "td/nowy_ter.scm ($td.scn)"), Assert.Single(Build(files).CleanUpOnce(1)!.Kept));

        files.WithFile(Scenery("td/nowy_ter.scm"), "node");

        Assert.Single(Build(files).CleanUpOnce(2)!.Deleted);
    }

    [Fact]
    public void A_file_many_sceneries_include_is_read_once()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Scenery("dzien.scn"), "include td/base.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("noc.scn"), "include td/base.scm end\nFirstInit\n")
            .WithLegacyFile(Scenery("td/base.scm"), "include td/td_ter.scm end\n")
            .WithFile(Scenery("td/td_ter.scm"), "node")
            .WithFile(Scenery("dzien.sbt"), "EU07SBT2")
            .WithFile(Scenery("noc.sbt"), "EU07SBT2");
        var counted = DispatchProxy.Create<IFileSystem, ReadCounter>();
        var counter = (ReadCounter)(object)counted;
        counter.Files = files;
        var sbt = new BinaryTerrainFiles(counted, new GamePaths("game"), TestLog.Over(files), Record);

        Assert.Equal(2, sbt.CleanUpOnce(1)!.Deleted.Count);
        Assert.Single(counter.Read, path => Path.GetFileName(path) == "base.scm");
    }

    public class ReadCounter : DispatchProxy
    {
        public IFileSystem Files { get; set; } = null!;

        public List<string> Read { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IFileSystem.ReadAllBytes))
                Read.Add((string)args![0]!);
            return targetMethod.Invoke(Files, args);
        }
    }
}
