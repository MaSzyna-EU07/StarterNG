using System.Reflection;
using System.Text;
using StarterNG.Application;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryTrainsetSaveTests
{
    private const string Path = "scenery/test.scn";

    private const string Scn =
        "//$n Bałtyk\r\n" +
        "FirstInit\r\n" +
        "trainset pociag tor_1 200.0 0.0\r\n" +
        "//$o -Opis składu\r\n" +
        "node -1 0 ep07-001 dynamic PKP\\EP07 ep07-001 ep07 0 headdriver 3 0 enddynamic\r\n" +
        "node -1 0 wagon-1 dynamic PKP\\111A b10-1 111a 0 nobody 3 0 enddynamic\r\n" +
        "endtrainset\r\n" +
        "// koniec pliku z polską literą: żółw\r\n";

    private static Encoding Cp1250 => Encoding.GetEncoding(1250);

    private static (InMemoryFileSystem Files, Scenery Scenery, SaveSceneryTrainset Save) Setup(string scn) =>
        Setup(Cp1250.GetBytes(scn));

    private static (InMemoryFileSystem Files, Scenery Scenery, SaveSceneryTrainset Save) Setup(byte[] bytes)
    {
        var files = new InMemoryFileSystem();
        files.WriteAllBytes(Path, bytes);
        var scenery = TestScenery.Parser().Parse(Path, LegacyText.Decode(bytes));
        return (files, scenery, new SaveSceneryTrainset(files, TestLog.Over(files)));
    }

    [Fact]
    public void Reversed_after_the_header_is_read_and_written_back()
    {
        var trainset = new Trainset(
            "trainset a tor 0 0 reversed\n" +
            "node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\n" +
            "endtrainset");

        Assert.True(trainset.Reversed);
        Assert.Single(trainset.Vehicles);
        Assert.StartsWith("trainset a tor 0 0 reversed\n", trainset.GetTrainsetEntry());
    }

    [Fact]
    public void A_trainset_without_reversed_is_not_turned()
    {
        var trainset = new Trainset("trainset a tor 0 0\nendtrainset");

        Assert.False(trainset.Reversed);
        Assert.DoesNotContain("reversed", trainset.GetTrainsetEntry());
    }

    [Fact]
    public void An_untouched_trainset_is_not_modified_however_its_numbers_are_spelled()
    {
        var (_, scenery, _) = Setup(Scn);

        Assert.False(SceneryTrainsetText.IsModified(scenery.Trainsets[0]));
    }

    [Fact]
    public void The_head_stays_as_written_and_the_vehicles_are_rewritten()
    {
        var (_, scenery, _) = Setup(Scn.Replace("200.0 0.0\r\n", "200.0 0.0\r\nreversed\r\n"));
        var trainset = scenery.Trainsets[0];
        trainset.Vehicles.RemoveAt(1);

        string entry = SceneryTrainsetText.Compose(trainset, "\r\n");

        Assert.StartsWith("trainset pociag tor_1 200.0 0.0\r\nreversed\r\n//$o -Opis składu\r\nnode", entry);
        Assert.Contains("ep07-001", entry);
        Assert.DoesNotContain("wagon-1", entry);
        Assert.EndsWith("enddynamic\r\nendtrainset", entry);
        Assert.DoesNotContain("\r\r", entry);
        Assert.DoesNotMatch("[^\r]\n", entry);
    }

    [Fact]
    public void Only_the_speed_of_the_header_follows_the_ready_switch()
    {
        var (_, scenery, _) = Setup(Scn);
        var trainset = scenery.Trainsets[0];
        trainset.ReadyToGo = true;

        string entry = SceneryTrainsetText.Compose(trainset, "\n");

        Assert.StartsWith("trainset pociag tor_1 200.0 0.1\n", entry);
        Assert.True(SceneryTrainsetText.IsModified(trainset));
    }

    [Fact]
    public void Saving_rewrites_the_trainset_alone_and_keeps_the_original_once()
    {
        var (files, scenery, save) = Setup(Scn);
        var trainset = scenery.Trainsets[0];
        trainset.Vehicles.RemoveAt(1);

        var (outcome, _) = save.Execute(scenery, trainset);

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, outcome);
        string written = Cp1250.GetString(files.ReadAllBytes(Path));
        Assert.StartsWith("//$n Bałtyk\r\nFirstInit\r\ntrainset pociag tor_1 200.0 0.0\r\n//$o -Opis składu\r\n", written);
        Assert.DoesNotContain("wagon-1", written);
        Assert.EndsWith("endtrainset\r\n// koniec pliku z polską literą: żółw\r\n", written);
        Assert.Equal(Scn, Cp1250.GetString(files.ReadAllBytes(Path + SaveSceneryTrainset.BackupSuffix)));
        Assert.False(SceneryTrainsetText.IsModified(trainset));

        // A second save finds what the first one wrote; the backup stays the original.
        trainset.Vehicles.Clear();
        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, trainset).Outcome);
        Assert.DoesNotContain("ep07-001", Cp1250.GetString(files.ReadAllBytes(Path)));
        Assert.Equal(Scn, Cp1250.GetString(files.ReadAllBytes(Path + SaveSceneryTrainset.BackupSuffix)));
    }

    [Fact]
    public void Of_two_trainsets_written_alike_the_right_one_is_saved()
    {
        const string block =
            "trainset none tor_1 0 0\r\n" +
            "node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\r\n" +
            "endtrainset\r\n";
        var (files, scenery, save) = Setup(block + "// x\r\n" + block);
        var second = scenery.Trainsets[1];
        second.Vehicles.Clear();

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, second).Outcome);

        string written = Cp1250.GetString(files.ReadAllBytes(Path));
        Assert.StartsWith(block + "// x\r\ntrainset none tor_1 0 0\r\nendtrainset", written);
    }

    [Fact]
    public void A_file_changed_meanwhile_is_left_alone()
    {
        var (files, scenery, save) = Setup(Scn);
        var trainset = scenery.Trainsets[0];
        trainset.Vehicles.Clear();
        files.WithLegacyFile(Path, Scn.Replace("wagon-1", "wagon-2"));

        Assert.Equal(SceneryTrainsetSaveOutcome.NotFound, save.Execute(scenery, trainset).Outcome);
        Assert.Contains("wagon-2", Cp1250.GetString(files.ReadAllBytes(Path)));
        Assert.False(files.FileExists(Path + SaveSceneryTrainset.BackupSuffix));
    }

    private const string Cars =
        "trainset none road1 -20 50\r\n" +
        "node -1 0 auto_1 dynamic road\\man man nl293 0 headdriver 0 0 enddynamic\r\n" +
        "endtrainset\r\n";

    private const string Traffic =
        "node -1 0 nl293_a dynamic road\\man man nl293 \t\tryp_rd110 \t0 headdriver 50 0 enddynamic\r\n";

    private const string WithIncludes =
        "include tory.scm end\r\n" +
        "FirstInit\r\n" +
        "trainset pociag tor_1 0 0\r\n" +
        "node -1 0 ep07-001 dynamic PKP\\EP07 ep07-001 ep07 0 headdriver 3 0 enddynamic\r\n" +
        "endtrainset\r\n" +
        "include tomaszewo/auta.scm end //ruch uliczny\r\n" +
        "include tomaszewo/eventy.ctr end\r\n" +
        "/* include tomaszewo/auta.scm end */\r\n" +
        "include tomaszewo/auta.scm parametr end\r\n";

    private static (string File, string Text)? Files(string name) => name switch
    {
        "tory.scm" => (name, Cars),
        "tomaszewo/auta.scm" => (name, Cars + Traffic),
        "tomaszewo/eventy.ctr" => (name, "event e1 multiple 0 none endevent\r\n"),
        _ => null
    };

    [Fact]
    public void Includes_after_first_init_that_hold_trainsets_are_put_in_place()
    {
        string text = SceneryIncludes.Inline(WithIncludes, Files);

        // Before FirstInit is the scenery itself; without trainsets there is nothing to list.
        Assert.StartsWith("include tory.scm end\r\n", text);
        Assert.Contains("include tomaszewo/eventy.ctr end", text);
        Assert.Contains(SceneryIncludes.BeginMarker + "tomaszewo/auta.scm\r\ntrainset none road1", text);
        // Commented out, or with parameters to put in: left to the simulator.
        Assert.Contains("/* include tomaszewo/auta.scm end */", text);
        Assert.Contains("include tomaszewo/auta.scm parametr end", text);
        Assert.Single(SceneryIncludes.Ranges(text));
    }

    [Fact]
    public void A_trainset_from_an_include_is_listed_with_its_file_and_exported_in_place()
    {
        var scenery = TestScenery.Parser().Parse(Path, SceneryIncludes.Inline(WithIncludes, Files));

        Assert.Equal(2, scenery.Trainsets.Count);
        Assert.Null(scenery.Trainsets[0].SourceFile);
        Assert.Equal("tomaszewo/auta.scm", scenery.Trainsets[1].SourceFile);

        // Loose road traffic in the file stays put, as written; only its name is known.
        Assert.Empty(scenery.LooseVehicles);
        Assert.Contains("nl293_a", scenery.LooseVehicleNames);

        string exported = scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom());
        Assert.Contains("auto_1", exported);
        Assert.Contains(Traffic, exported);
        Assert.True(exported.IndexOf(Traffic, StringComparison.Ordinal) > exported.IndexOf("FirstInit", StringComparison.Ordinal));
        Assert.DoesNotContain("include tomaszewo/auta.scm end //", exported);
    }

    [Fact]
    public void A_trainset_from_an_include_is_saved_into_that_file()
    {
        var files = new InMemoryFileSystem()
            .WithLegacyFile(Path, WithIncludes)
            .WithLegacyFile("scenery/tomaszewo/auta.scm", Cars + "// koniec\r\n");
        var scenery = TestScenery.Parser().Parse(Path, SceneryIncludes.Inline(WithIncludes, Files));
        var log = TestLog.Over(files);
        var car = scenery.Trainsets[1];
        car.Vehicles.Clear();

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, new SaveSceneryTrainset(files, log).Execute(scenery, car).Outcome);

        Assert.Equal("trainset none road1 -20 50\r\nendtrainset\r\n// koniec\r\n",
                     Cp1250.GetString(files.ReadAllBytes("scenery/tomaszewo/auta.scm")));
        Assert.Equal(WithIncludes, Cp1250.GetString(files.ReadAllBytes(Path)));
        Assert.True(files.FileExists("scenery/tomaszewo/auta.scm" + SaveSceneryTrainset.BackupSuffix));
    }

    [Fact]
    public void A_utf8_file_stays_utf8_with_its_mark()
    {
        var (files, scenery, save) = Setup(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Scn)).ToArray());
        scenery.Trainsets[0].Vehicles.RemoveAt(1);

        save.Execute(scenery, scenery.Trainsets[0]);

        byte[] bytes = files.ReadAllBytes(Path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("żółw", Encoding.UTF8.GetString(bytes[3..]));
    }

    private const string V1 = "node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic";
    private const string V2 = "node -1 0 v2 dynamic d s m 0 nobody 3 0 enddynamic";
    private const string V3 = "node -1 0 v3 dynamic d s m 0 nobody 3 0 enddynamic";

    private static string SavedText(string scn, Action<Trainset>? change = null)
    {
        var (files, scenery, save) = Setup(scn);
        change?.Invoke(scenery.Trainsets[0]);

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, scenery.Trainsets[0]).Outcome);

        return Cp1250.GetString(files.ReadAllBytes(Path));
    }

    private static IEnumerable<string> VehicleNames(string scn) =>
        TestScenery.Parser().Parse(Path, scn).Trainsets.Single().Vehicles.Select(v => v.Name);

    private static void Edit(Trainset trainset) => trainset.Vehicles[0].Coupling.Flags = 5;

    [Fact]
    public void A_vehicle_commented_out_stays_in_its_comment()
    {
        string scn = $"FirstInit\ntrainset a t 0 0\n/*\n{V1}\n*/\n{V2}\nendtrainset\n" +
                     "node -1 0 poza dynamic d s m none 0 nobody 0 0 enddynamic\n";

        Assert.Equal(scn, SavedText(scn));

        string edited = SavedText(scn, Edit);
        Assert.Contains($"/*\n{V1}\n*/\n", edited);
        var scenery = TestScenery.Parser().Parse(Path, edited);
        Assert.Equal(new[] { "v2" }, scenery.Trainsets.Single().Vehicles.Select(v => v.Name));
        Assert.Equal(new[] { "poza" }, scenery.LooseVehicleNames);
    }

    [Theory]
    [InlineData("trainset a t 0 0\nNode -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset\n")]
    [InlineData("trainset a t 0 0\nNODE -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset\n")]
    [InlineData("trainset a t 0 0 node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset\n")]
    [InlineData("trainset a t 0 0 /*x*/node -1 0 v1 dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset\n")]
    public void A_vehicle_is_found_however_it_is_written_and_not_written_twice(string scn)
    {
        Assert.Equal(scn, SavedText(scn));

        string edited = SavedText(scn, Edit);
        var vehicle = Assert.Single(TestScenery.Parser().Parse(Path, edited).Trainsets.Single().Vehicles);
        Assert.Equal(5, vehicle.Coupling.Flags);
    }

    [Fact]
    public void A_vehicle_named_as_a_keyword_is_no_keyword()
    {
        string scn = "trainset node node 0 0\nnode -1 0 node dynamic d s m 0 nobody 3 0 enddynamic\nendtrainset\n";

        Assert.Equal(scn, SavedText(scn));
        Assert.Contains("trainset node node 0 0.1\n", SavedText(scn, t => t.ReadyToGo = true));
    }

    private const string Commented =
        "trainset a t 0 0\n" +
        V1 + "\n" +
        "// o v2\n" +
        V2 + " // przy v2\n" +
        "/* o v3 */\n" +
        V3 + "\n" +
        "// koniec\n" +
        "EndTrainset\n";

    [Fact]
    public void A_save_with_no_change_leaves_the_vehicles_and_comments_as_they_were()
    {
        Assert.Equal(Commented, SavedText(Commented));

        // Nor is anything but the speed touched when only the speed changes.
        Assert.Equal(Commented.Replace("t 0 0\n", "t 0 0.1\n"), SavedText(Commented, t => t.ReadyToGo = true));
    }

    [Fact]
    public void Comments_stay_where_they_are_when_a_vehicle_is_edited()
    {
        string edited = SavedText(Commented, t => t.Vehicles[1].Coupling.Flags = 5);

        Assert.Equal(Commented.Replace(V2, V2.Replace("nobody 3", "nobody 5")), edited);
    }

    [Fact]
    public void Comments_stay_when_a_vehicle_is_removed()
    {
        Assert.Equal(
            "trainset a t 0 0\n" + V1 + "\n// o v2\n// przy v2\n/* o v3 */\n" + V3 + "\n// koniec\nEndTrainset\n",
            SavedText(Commented, t => t.Vehicles.RemoveAt(1)));

        Assert.Equal(
            "trainset a t 0 0\n// o v2\n// przy v2\n/* o v3 */\n// koniec\nEndTrainset\n",
            SavedText(Commented, t => t.Vehicles.Clear()));
    }

    [Fact]
    public void Comments_stay_when_a_vehicle_is_added()
    {
        var added = new Dynamic { Name = "nowy", DataFolder = "d", SkinFile = "s", MmdFile = "m" };

        Assert.Equal(
            Commented.Replace(V3, added.ToTrainsetNode() + V3),
            SavedText(Commented, t => t.Vehicles.Insert(2, added)));

        Assert.Equal(
            Commented.Replace("EndTrainset", added.ToTrainsetNode() + "EndTrainset"),
            SavedText(Commented, t => t.Vehicles.Add(added)));
    }

    [Fact]
    public void Comments_stay_when_vehicles_change_places_and_a_moved_one_stays_as_written()
    {
        string moved = "node 500  0 v3 dynamic d s m /*w srodku*/ 0 nobody 3 0 enddynamic";
        string scn = Commented.Replace(V3, moved);

        string saved = SavedText(scn, t => t.Vehicles.Reverse());

        Assert.Equal(new[] { "v3", "v2", "v1" }, VehicleNames(saved));
        foreach (string comment in new[] { "// o v2\n", " // przy v2\n", "/* o v3 */\n", "// koniec\n" })
            Assert.Contains(comment, saved);
        Assert.Contains(moved, saved);
    }

    [Theory]
    [InlineData("trainset a /*tor*/ t 100 0\nendtrainset\n", "trainset a /*tor*/ t 100 0.1\nendtrainset\n")]
    [InlineData("trainset a t /*x*/ 100/*y*/ 0.0 reversed\nendtrainset\n", "trainset a t /*x*/ 100/*y*/ 0.1 reversed\nendtrainset\n")]
    [InlineData("Trainset a // tor\n t\n 100\n 0.0\nendtrainset\n", "Trainset a // tor\n t\n 100\n 0.1\nendtrainset\n")]
    public void The_speed_is_found_past_a_comment_in_the_header(string scn, string expected)
    {
        Assert.Equal(expected, SavedText(scn, t => t.ReadyToGo = true));
    }

    [Fact]
    public void An_optional_include_after_first_init_stays_an_option()
    {
        const string scn = "FirstInit\ninclude zima.inc end //$optional 1, Zima\nx\nendoptional\n";
        string content = $"trainset a t 0 0\n{V1}\nendtrainset\n";

        string text = SceneryIncludes.Inline(scn, name => (name, content));

        Assert.Equal(scn, text);
        var scenery = TestScenery.Parser().Parse(Path, text);
        Assert.Equal("Zima", Assert.Single(scenery.Includes).Desc);
        Assert.Empty(scenery.Trainsets);
    }

    /// <summary>A disk that gives out on whatever is being written; what it does to the file is <see cref="PhysicalFileSystemTests"/>' business.</summary>
    public class FailingDisk : DispatchProxy
    {
        public InMemoryFileSystem Files = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IFileSystem.WriteAllBytesAtomic)
                ? throw new IOException("the disk gave out")
                : method.Invoke(Files, args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_write_that_fails_leaves_the_file_and_its_backup_whole(bool backedUp)
    {
        var (files, scenery, _) = Setup(Scn);
        string backup = Path + SaveSceneryTrainset.BackupSuffix;
        if (backedUp)
            files.WithLegacyFile(backup, Scn);
        var disk = DispatchProxy.Create<IFileSystem, FailingDisk>();
        ((FailingDisk)(object)disk).Files = files;
        var log = TestLog.Over();
        var trainset = scenery.Trainsets[0];
        string before = trainset.RawEntry;
        trainset.Vehicles.RemoveAt(1);

        var (outcome, detail) = new SaveSceneryTrainset(disk, log).Execute(scenery, trainset);

        Assert.Equal((SceneryTrainsetSaveOutcome.Failed, "the disk gave out"), (outcome, detail));
        Assert.Equal(Scn, Cp1250.GetString(files.ReadAllBytes(Path)));
        Assert.Equal(backedUp, files.FileExists(backup));
        Assert.Equal(new[] { Path }.Concat(backedUp ? new[] { backup } : Array.Empty<string>()),
                     files.GetFiles("scenery"));
        Assert.Equal(before, trainset.RawEntry);
    }

    private static (string Scn, SaveSceneryTrainset Save, Scenery Scenery, string Folder) OnDisk(string text)
    {
        string folder = Directory.CreateTempSubdirectory("starter-save-").FullName;
        string scn = System.IO.Path.Combine(folder, "test.scn");
        var files = new PhysicalFileSystem();
        files.WriteAllText(scn, text, LegacyText.CodePage1250);
        var log = TestLog.Over();
        return (scn, new SaveSceneryTrainset(files, log), TestScenery.Parser().Parse(scn, text), folder);
    }

    [Fact]
    public void On_disk_the_new_text_is_written_beside_the_file_and_moved_over_it()
    {
        var (scn, save, scenery, folder) = OnDisk(Scn);
        try
        {
            var trainset = scenery.Trainsets[0];
            trainset.Vehicles.RemoveAt(1);

            // Nothing can be written beside it: the file is not touched either.
            string temp = scn + PhysicalFileSystem.TempSuffix;
            Directory.CreateDirectory(temp);
            Assert.Equal(SceneryTrainsetSaveOutcome.Failed, save.Execute(scenery, trainset).Outcome);
            Assert.Equal(Scn, Cp1250.GetString(File.ReadAllBytes(scn)));
            Assert.True(File.Exists(scn + SaveSceneryTrainset.BackupSuffix));

            Directory.Delete(temp);
            Assert.Equal((SceneryTrainsetSaveOutcome.Saved, (string?)null), save.Execute(scenery, trainset));

            Assert.DoesNotContain("wagon-1", Cp1250.GetString(File.ReadAllBytes(scn)));
            Assert.Equal(Scn, Cp1250.GetString(File.ReadAllBytes(scn + SaveSceneryTrainset.BackupSuffix)));
            Assert.Equal(
                new[] { "test.scn", "test.scn" + SaveSceneryTrainset.BackupSuffix },
                Directory.GetFiles(folder, "test.scn*").Select(file => System.IO.Path.GetFileName(file)).Order());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void On_disk_a_file_that_cannot_be_replaced_stays_and_nothing_is_left_beside_it()
    {
        // Only Windows keeps a file in use from being moved over.
        if (!OperatingSystem.IsWindows())
            return;

        var (scn, save, scenery, folder) = OnDisk(Scn);
        try
        {
            scenery.Trainsets[0].Vehicles.RemoveAt(1);

            using (new FileStream(scn, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(SceneryTrainsetSaveOutcome.Failed, save.Execute(scenery, scenery.Trainsets[0]).Outcome);

            Assert.Equal(Scn, Cp1250.GetString(File.ReadAllBytes(scn)));
            Assert.False(File.Exists(scn + PhysicalFileSystem.TempSuffix));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void On_disk_a_link_stays_a_link_and_its_file_gets_the_text()
    {
        var (scn, save, scenery, folder) = OnDisk(Scn);
        try
        {
            string target = scn + ".wlasciwy";
            File.Move(scn, target);
            try
            {
                File.CreateSymbolicLink(scn, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Windows lets only some users make links.
                return;
            }
            scenery.Trainsets[0].Vehicles.RemoveAt(1);

            Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, scenery.Trainsets[0]).Outcome);

            Assert.NotNull(new FileInfo(scn).LinkTarget);
            Assert.DoesNotContain("wagon-1", Cp1250.GetString(File.ReadAllBytes(target)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static byte[] Bytes(params object[] parts) =>
        parts.SelectMany(part => part as byte[] ?? Encoding.ASCII.GetBytes((string)part)).ToArray();

    [Fact]
    public void Bytes_behind_a_mark_that_are_not_utf8_stay_as_they_were()
    {
        byte[] mark = { 0xEF, 0xBB, 0xBF };
        byte[] polish = { 0xB3, 0xF3, 0xBF };   // "łóż" in code page 1250
        string trainset = $"trainset a t 0 0\n{V1}\nendtrainset\n";
        var (files, scenery, save) = Setup(Bytes(mark, "// ", polish, "\n", trainset, "// ", polish, "\n"));
        scenery.Trainsets[0].Vehicles.Clear();

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, scenery.Trainsets[0]).Outcome);

        Assert.Equal(Bytes(mark, "// ", polish, "\ntrainset a t 0 0\nendtrainset\n// ", polish, "\n"), files.ReadAllBytes(Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_byte_outside_the_vehicles_stays_as_it_was(bool marked)
    {
        byte[] mark = marked ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>();
        byte[] all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        string trainset = $"trainset a t 0 0\n{V1}\nendtrainset\n";
        var (files, scenery, save) = Setup(Bytes(mark, trainset, all));
        scenery.Trainsets[0].Vehicles.Clear();

        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, save.Execute(scenery, scenery.Trainsets[0]).Outcome);

        Assert.Equal(Bytes(mark, "trainset a t 0 0\nendtrainset\n", all), files.ReadAllBytes(Path));
    }

    [Theory]
    // Its own line ends, in a file with the other kind elsewhere.
    [InlineData("// x\r\ntrainset a t 0 0\n" + V1 + "\nendtrainset\n// y\r\n")]
    // Both kinds in the trainset.
    [InlineData("trainset a t 0 0\r\n// x\n" + V1 + "\r\nendtrainset\r\n")]
    public void Mixed_line_ends_stay_as_they_were(string scn)
    {
        Assert.Equal(scn, SavedText(scn));
    }

    [Fact]
    public void What_the_starter_writes_takes_the_line_ends_of_the_trainset()
    {
        string scn = "// x\r\ntrainset a t 0 0\n" + V1 + "\nendtrainset\n// y\r\n";

        Assert.Equal(
            "// x\r\ntrainset a t 0 0\n" + V2 + "\nendtrainset\n// y\r\n",
            SavedText(scn, t => t.Vehicles[0].Name = "v2"));
    }
}
