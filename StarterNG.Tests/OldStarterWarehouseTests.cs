using StarterNG.Classes;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

// The old starter's magazyn.ini taken in at a start, over the fake disk.
public class OldStarterWarehouseTests
{
    private const string PresetsPath = "/home/user/.config/MaSzyna/starter/userpresets.json";

    // As the old starter writes it: CRLF, code page 1250, the scenery's path for a name.
    private const string Magazyn =
        "[TRAINSET0=baltyk/baltyk_km/mpj15120]\r\n" +
        "00=node -1 0 EN57AKM-1682RA dynamic pkp\\en57akm_2013_v1 EN57AKM-1682ra akm_2013_ra_male_okna 0 headdriver 255 0 enddynamic\r\n" +
        "01=node -1 0 EN57AKM-1682S dynamic pkp\\en57akm_2013_v1 EN57AKM-1682s akm_2013_s 0 nobody 255 0 enddynamic\r\n" +
        "02=node -1 0 EN57AKM-1682RB dynamic pkp\\en57akm_2013_v1 EN57AKM-1682rb akm_2013_rb_male_okna 0 nobody 0 0 enddynamic\r\n";

    private static (PresetFile Presets, InMemoryFileSystem Files) Rig()
    {
        var files = new InMemoryFileSystem();
        return (new PresetFile(files, TestLog.Over(files), PresetsPath), files);
    }

    private static List<TrainsetPreset> Consist(string name, string vehicle) =>
        OldStarterWarehouse.Read($"[TRAINSET0={name}]\r\n00=node -1 0 {vehicle} dynamic pkp\\x {vehicle} x 0 headdriver 3 0 enddynamic\r\n");

    [Fact]
    public void A_consist_of_the_old_starter_reads_as_a_trainset_with_its_vehicles()
    {
        var consist = Assert.Single(OldStarterWarehouse.Read(Magazyn));

        Assert.Equal("baltyk/baltyk_km/mpj15120", consist.Name);
        Assert.StartsWith("trainset baltyk/baltyk_km/mpj15120 none 0 0\n", consist.Entry);
        Assert.EndsWith("endtrainset\n", consist.Entry);
        var vehicles = ConsistText.VehiclesFrom(consist.Entry);
        Assert.NotNull(vehicles);
        Assert.Equal(new[] { "EN57AKM-1682RA", "EN57AKM-1682S", "EN57AKM-1682RB" }, vehicles!.Select(v => v.Name));
    }

    [Fact]
    public void A_consist_new_in_the_old_starter_joins_the_warehouse()
    {
        var (presets, _) = Rig();
        presets.Save("mine", "trainset mine");

        int added = presets.TakeFromOldStarter(OldStarterWarehouse.Read(Magazyn));

        Assert.Equal(1, added);
        Assert.Equal(new[] { "baltyk/baltyk_km/mpj15120", "mine" }, presets.Presets().Select(p => p.Name));
    }

    [Fact]
    public void A_consist_removed_here_does_not_come_back_at_the_next_start()
    {
        var (presets, _) = Rig();
        var found = OldStarterWarehouse.Read(Magazyn);
        presets.TakeFromOldStarter(found);
        presets.Delete("baltyk/baltyk_km/mpj15120");

        int added = presets.TakeFromOldStarter(found);

        Assert.Equal(0, added);
        Assert.Empty(presets.Presets());
    }

    [Fact]
    public void Nothing_new_leaves_the_file_unwritten()
    {
        var (presets, files) = Rig();
        var found = OldStarterWarehouse.Read(Magazyn);
        presets.TakeFromOldStarter(found);
        string before = files.ReadAllText(PresetsPath);
        files.WithFile(PresetsPath, before + " ");

        presets.TakeFromOldStarter(found);

        Assert.Equal(before + " ", files.ReadAllText(PresetsPath));
    }

    [Fact]
    public void A_consist_imported_by_hand_before_is_not_taken_twice()
    {
        var (presets, _) = Rig();
        var found = OldStarterWarehouse.Read(Magazyn);
        presets.Save(found[0].Name, found[0].Entry);

        int added = presets.TakeFromOldStarter(found);

        Assert.Equal(0, added);
        Assert.Single(presets.Presets());
    }

    [Fact]
    public void A_consist_of_the_same_name_made_here_stays_and_the_old_one_comes_in_beside_it()
    {
        var (presets, _) = Rig();
        presets.Save("osobowy", "trainset osobowy none 0 0\nendtrainset\n");

        presets.TakeFromOldStarter(Consist("osobowy", "SM42"));

        var all = presets.Presets();
        Assert.Equal(new[] { "osobowy", "osobowy" + PresetFile.OldStarterSuffix }, all.Select(p => p.Name));
        Assert.Equal("trainset osobowy none 0 0\nendtrainset\n", all[0].Entry);
        Assert.Contains("SM42", all[1].Entry);
    }

    [Fact]
    public void A_consist_changed_in_the_old_starter_and_untouched_here_is_replaced()
    {
        var (presets, _) = Rig();
        presets.TakeFromOldStarter(Consist("osobowy", "SM42"));

        presets.TakeFromOldStarter(Consist("osobowy", "SU45"));

        var only = Assert.Single(presets.Presets());
        Assert.Equal("osobowy", only.Name);
        Assert.Contains("SU45", only.Entry);
    }

    [Fact]
    public void A_warehouse_file_that_cannot_be_read_is_left_as_it_is()
    {
        var (presets, files) = Rig();
        files.WithFile(PresetsPath, "{ cut");

        int added = presets.TakeFromOldStarter(OldStarterWarehouse.Read(Magazyn));

        Assert.Equal(0, added);
        Assert.Equal("{ cut", files.ReadAllText(PresetsPath));
        Assert.False(files.FileExists(PresetsPath + ".unreadable"));
    }
}
