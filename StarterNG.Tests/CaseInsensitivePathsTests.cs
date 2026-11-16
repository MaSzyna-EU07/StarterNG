using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class CaseInsensitivePathsTests
{
    private static InMemoryFileSystem Disk() => new InMemoryFileSystem(caseSensitive: true)
        .WithFile("game/scenery/Braniewo/Base.ctr", "")
        .WithFile("game/scenery/Braniewo/rozklady/TGS3657.txt", "")
        .WithDirectory("game/textures/mini");

    [Theory]
    [InlineData("braniewo/base.ctr", "Braniewo/Base.ctr")]
    [InlineData(@"BRANIEWO\Rozklady\tgs3657.TXT", "Braniewo/rozklady/TGS3657.txt")]
    [InlineData("Braniewo/Base.ctr", "Braniewo/Base.ctr")]
    [InlineData("braniewo//base.ctr", "Braniewo/Base.ctr")]
    [InlineData("/braniewo/base.ctr", "Braniewo/Base.ctr")]
    [InlineData(@"\Braniewo\Base.ctr", "Braniewo/Base.ctr")]
    public void A_name_in_any_case_is_given_as_it_is_on_disk(string name, string onDisk) =>
        Assert.Equal(onDisk, new CaseInsensitivePaths(Disk()).Resolve("game/scenery", name));

    [Theory]
    [InlineData("braniewo/brak.ctr")]
    [InlineData("brak/base.ctr")]
    [InlineData("")]
    public void A_name_nothing_answers_to_is_not_found(string name) =>
        Assert.Null(new CaseInsensitivePaths(Disk()).Resolve("game/scenery", name));

    [Fact]
    public void A_folder_is_found_only_when_one_is_asked_for()
    {
        var names = new CaseInsensitivePaths(Disk());

        Assert.Equal("Braniewo/rozklady", names.Resolve("game/scenery", "braniewo/ROZKLADY", directory: true));
        Assert.Null(names.Resolve("game/scenery", "braniewo/ROZKLADY"));
        Assert.Null(names.Resolve("game/scenery", "braniewo/base.ctr", directory: true));
    }

    [Fact]
    public void The_name_written_as_on_disk_wins_over_one_in_other_case()
    {
        var files = Disk().WithFile("game/scenery/braniewo/base.ctr", "");
        var names = new CaseInsensitivePaths(files);

        Assert.Equal("braniewo/base.ctr", names.Resolve("game/scenery", "braniewo/base.ctr"));
        Assert.Equal("Braniewo/Base.ctr", names.Resolve("game/scenery", "Braniewo/base.CTR"));
    }

    [Fact]
    public void Of_folders_named_alike_the_one_holding_the_file_is_taken()
    {
        var files = Disk().WithFile("game/scenery/braniewo/tory.scm", "");
        var names = new CaseInsensitivePaths(files);

        Assert.Equal("Braniewo/Base.ctr", names.Resolve("game/scenery", "braniewo/base.ctr"));
        Assert.Equal("braniewo/tory.scm", names.Resolve("game/scenery", "Braniewo/Tory.scm"));
    }

    [Fact]
    public void The_full_path_joins_the_folder_and_the_name_on_disk() =>
        Assert.Equal(Path.Combine("game/scenery", "Braniewo", "Base.ctr"),
                     new CaseInsensitivePaths(Disk()).Find("game/scenery", "BRANIEWO/BASE.CTR"));

    [Fact]
    public void A_full_path_is_found_from_its_own_root()
    {
        string scenery = Path.GetFullPath(Path.Combine("game", "scenery"));
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithFile(Path.Combine(scenery, "Braniewo", "Base.ctr"), "");

        Assert.Equal(Path.Combine(scenery, "Braniewo", "Base.ctr"),
                     new CaseInsensitivePaths(files).Find("elsewhere", Path.Combine(scenery, "BRANIEWO", "base.CTR")));
    }

    [Fact]
    public void A_disk_that_ignores_case_gives_the_name_back_as_written()
    {
        var files = new InMemoryFileSystem().WithFile("game/scenery/Braniewo/Base.ctr", "");
        var names = new CaseInsensitivePaths(files);

        Assert.Equal("BRANIEWO/base.CTR", names.Resolve("game/scenery", "BRANIEWO/base.CTR"));
        Assert.Null(names.Resolve("game/scenery", "braniewo/brak.ctr"));
    }

    [Fact]
    public void Kept_listings_hold_until_forgotten()
    {
        var files = Disk();
        var names = new CaseInsensitivePaths(files, keepListings: true);
        Assert.Null(names.Resolve("game/scenery", "braniewo/nowy.scm"));

        files.WithFile("game/scenery/Braniewo/Nowy.scm", "");
        Assert.Null(names.Resolve("game/scenery", "braniewo/nowy.scm"));

        names.Forget();
        Assert.Equal("Braniewo/Nowy.scm", names.Resolve("game/scenery", "braniewo/nowy.scm"));
    }

    [Fact]
    public void Listings_not_kept_see_a_file_added_since()
    {
        var files = Disk();
        var names = new CaseInsensitivePaths(files);
        Assert.Null(names.Resolve("game/scenery", "braniewo/nowy.scm"));

        files.WithFile("game/scenery/Braniewo/Nowy.scm", "");

        Assert.Equal("Braniewo/Nowy.scm", names.Resolve("game/scenery", "braniewo/nowy.scm"));
    }

    [Fact]
    public void A_folder_that_cannot_be_listed_holds_nothing_to_find()
    {
        var files = Disk().DenyListing("game/scenery");

        Assert.Null(new CaseInsensitivePaths(files).Resolve("game/scenery", "braniewo/base.ctr"));
    }
}
