using StarterNG.Domain.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class ScenerySyntaxTests
{
    private static (SceneryFaultKind, string, int, string?, string?) Seen(SceneryFault fault) =>
        (fault.Kind, fault.File, fault.Line, fault.Subject, fault.Token);

    [Fact]
    public void A_trainset_commented_out_is_no_trainset()
    {
        const string scn = """
            /* trainset stary tor_1 0 0
            node -1 0 st dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset */
            // trainset dla gracza:
            trainset pociag tor_1 10 0
            node -1 0 ep07-001 dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic//koniec
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        var trainset = Assert.Single(scenery.Trainsets);
        Assert.Equal("pociag", trainset.Name);
        Assert.Single(trainset.Vehicles);
        Assert.Empty(scenery.Faults);
        Assert.Contains("trainset stary", scenery.Template);
    }

    [Fact]
    public void A_trainset_without_its_end_is_reported_and_the_next_one_read_on_its_own()
    {
        const string scn = """
            FirstInit
            trainset pierwszy tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            trainset drugi tor_2 10 0
            node -1 0 b dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        Assert.Equal((SceneryFaultKind.TrainsetNotClosed, "x.scn", 2, "pierwszy", null),
                     Seen(Assert.Single(scenery.Faults)));
        var trainset = Assert.Single(scenery.Trainsets);
        Assert.Equal("drugi", trainset.Name);
        Assert.Equal("b", Assert.Single(trainset.Vehicles).Name);
        // The unended one is the simulator's to read as written.
        Assert.Contains("trainset pierwszy", scenery.Template);
    }

    [Fact]
    public void A_vehicle_without_its_end_is_reported_yet_its_trainset_listed()
    {
        const string scn = """
            trainset pociag tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3
            node -1 0 b dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.VehicleNotClosed, "x.scn", 2, "a", null), Seen(fault));
        // The simulator goes on to the next enddynamic, taking b for a's.
        Assert.Equal("b", fault.Lost);
        Assert.Equal(new[] { "a", "b" }, Assert.Single(scenery.Trainsets).Vehicles.Select(v => v.Name));
    }

    [Fact]
    public void A_vehicle_with_no_load_count_takes_the_next_one_with_it()
    {
        const string scn = """
            trainset pociag tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 enddynamic
            node -1 0 b dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        // Its enddynamic read for the count, "node" for a destination, b's for its end.
        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.LoadCountMissing, 2, "a", "b"), (fault.Kind, fault.Line, fault.Subject, fault.Lost));
        Assert.Equal(new[] { "a", "b" }, Assert.Single(scenery.Trainsets).Vehicles.Select(v => v.Name));
    }

    [Fact]
    public void A_load_with_no_type_runs_on_into_the_next_trainset()
    {
        const string scn = """
            trainset pierwszy tor_1 10 0
            node -1 0 a dynamic PKP/412Z 412z 412z 0 nobody 3 40 enddynamic
            endtrainset
            trainset drugi tor_2 10 0
            node -1 0 b dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            node -1 0 c dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        // The second endtrainset is what ends the first trainset then.
        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.LoadTypeMissing, 2, "a", "40", "endtrainset, trainset drugi, b"),
                     (fault.Kind, fault.Line, fault.Subject, fault.Token, fault.Lost));
        Assert.Equal(new[] { "pierwszy", "drugi" }, scenery.Trainsets.Select(t => t.Name));
    }

    [Theory]
    [InlineData("0 none", "none")]
    [InlineData("0 pantstate", "pantstate")]
    [InlineData("0 cel Katowice", null)]
    [InlineData("3 pantstate", null)]
    [InlineData("12 wegiel", null)]
    public void One_word_after_the_load_is_taken_for_a_destination_named_enddynamic(string load, string? word)
    {
        string scn = $"""
            trainset pociag tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 {load} enddynamic
            node -1 0 b dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            endtrainset
            """;

        var faults = TestScenery.Parser().Parse("scenery/x.scn", scn).Faults;

        if (word is null)
            Assert.Empty(faults);
        else
            Assert.Equal((SceneryFaultKind.WordBeforeEnd, word, "b"), (faults.Single().Kind, faults.Single().Token, faults.Single().Lost));
    }

    [Fact]
    public void A_header_written_twice_is_told_as_such()
    {
        const string scn = """
            trainset baltyk\mod\exe45151 chojnice301 40 0.1
            //$o -Skład ozdobny
            trainset baltyk\mod\exe45151 chojnice301 40 0.1
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        Assert.Equal((SceneryFaultKind.TrainsetHeaderRepeated, "x.scn", 1, "baltyk\\mod\\exe45151", null),
                     Seen(Assert.Single(scenery.Faults)));
        Assert.Single(scenery.Trainsets);
    }

    [Fact]
    public void A_trainset_whose_header_is_swallowed_is_told_at_the_fault_that_began_it()
    {
        const string scn = """
            trainset pierwszy tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            //trainset bez_naglowka tor_2 0 0
            node -1 0 b dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            trainset trzeci tor_3 0 0
            node -1 0 c1 dynamic PKP/EU07 eu07 eu07 0 headdriver 3 0 enddynamic
            node -1 0 c2 dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            node -1 0 c3 dynamic PKP/111A 111a 111a 0 nobody 3 0 enddynamic
            endtrainset
            trainset czwarty tor_4 0 0
            node -1 0 d dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        // c2 with no trainset takes c3, and the endtrainset is left alone - all of
        // it from b, where the fault is.
        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.VehicleOutsideTrainset, 5, "endtrainset, trainset trzeci, c1", "trzeci"),
                     (fault.Kind, fault.Line, fault.Lost, fault.Unmade));
        Assert.Equal(new[] { "pierwszy", "trzeci", "czwarty" }, scenery.Trainsets.Select(t => t.Name));
    }

    [Fact]
    public void Text_where_a_number_belongs_is_named()
    {
        const string scn = """
            trainset pociag tor_1 1O 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0,5 headdriver 3 0 enddynamic
            endtrainset
            """;

        var faults = TestScenery.Parser().Parse("scenery/x.scn", scn).Faults;

        Assert.Equal(new[]
        {
            (SceneryFaultKind.NotANumber, "x.scn", 1, (string?)"pociag", (string?)"1O"),
            (SceneryFaultKind.NotANumber, "x.scn", 2, "a", "0,5")
        }, faults.Select(Seen));
    }

    [Fact]
    public void A_track_name_split_by_a_space_is_named_whole()
    {
        // "Pawonków#L" with its "ó" lost to a space.
        const string scn = """
            trainset none Pawonk w#L 200.0 0.0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            endtrainset
            """;

        var faults = TestScenery.Parser().Parse("scenery/x.scn", scn).Faults;

        Assert.Equal((SceneryFaultKind.TrackNameSplit, "x.scn", 1, "none", "Pawonk w#L"), Seen(Assert.Single(faults)));
    }

    [Fact]
    public void Vehicles_left_without_their_trainset_header_are_told_so()
    {
        const string scn = """
            //trainset 2003r\64040 Staniszcze_Male_po#2 -50 0.1
            node -1 0 EN57-1542ra dynamic pkp\en57_v2 EN57-1542ra EN57RA 0 headdriver 255 0 enddynamic
            endtrainset
            node -1 0 auto dynamic road\ford transit transit droga-b 0 headdriver 25.0 0 enddynamic
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        // On no track the simulator has, it drops the vehicle and goes on to the
        // next enddynamic - past the endtrainset, to the car's.
        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.VehicleOutsideTrainset, 2, "endtrainset, auto"), (fault.Kind, fault.Line, fault.Lost));
        Assert.Equal("auto", Assert.Single(scenery.LooseVehicles).Name);
    }

    [Fact]
    public void Structure_faults_are_found_where_the_simulator_reports_them()
    {
        const string scn = """
            endtrainset
            trainset pusty tor_1 10 0
            endtrainset trainset pociag tor_1 10 0
            node -1 0 a dynamic PKP/EP07 ep07 ep07 0 headdriver enddynamic
            node -1 0 b dynamic PKP/EP07 ep07 ep07 0 headdriver 3 0 enddynamic
            reversed
            endtrainset
            include dekoracje.scm // bez end
            include auta.scm end
            /* bez końca
            """;

        var faults = TestScenery.Parser().Parse("scenery/x.scn", scn).Faults.Select(f => (f.Kind, f.Line)).ToList();

        Assert.Equal(new[]
        {
            (SceneryFaultKind.StrayEndTrainset, 1),
            (SceneryFaultKind.VehicleIncomplete, 4),
            (SceneryFaultKind.ReversedMisplaced, 6),
            (SceneryFaultKind.IncludeNotClosed, 8),
            (SceneryFaultKind.UnclosedComment, 10)
        }, faults);
    }

    [Fact]
    public void A_trainset_with_no_vehicles_is_listed_to_be_filled_in_the_depot()
    {
        const string scn = """
            trainset td/rozklad tdo_n1734 0.0 0.1
            //$o Skład gotowy na żeberku.
            endtrainset
            include koniec.scm
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn, _ => new IncludeLookup(true));

        Assert.Empty(scenery.Faults);
        Assert.Equal("td/rozklad", Assert.Single(scenery.Trainsets).Name);
    }

    [Fact]
    public void Reversed_may_follow_the_assignments_ahead_of_the_vehicles()
    {
        const string scn = """
            Trainset pociag tor_1 10 0
            assignment pl "Pociąg" endassignment
            reversed
            Node -1 0 a dynamic PKP/EP07 ep07 ep07 0 HeadDriver 3 0 EndDynamic
            EndTrainset
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        Assert.Empty(scenery.Faults);
        var trainset = Assert.Single(scenery.Trainsets);
        Assert.True(trainset.Reversed);
        Assert.Equal(StarterNG.Classes.eDriverType.Headdriver, Assert.Single(trainset.Vehicles).DriverType);
    }

    [Fact]
    public void A_fault_in_an_included_file_is_placed_in_that_file()
    {
        string text = SceneryIncludes.Inline(
            "FirstInit\ninclude tomaszewo/auta.scm end\ntrainset zly tor 0\nendtrainset\n",
            name => (name, "// auta\ntrainset auto road1 0 50\nnode -1 0 a1 dynamic car car car x headdriver 3 0 enddynamic\nendtrainset"));

        var faults = TestScenery.Parser().Parse("scenery/x.scn", text).Faults.Select(Seen);

        Assert.Equal(new[]
        {
            (SceneryFaultKind.NotANumber, "tomaszewo/auta.scm", 3, (string?)"a1", (string?)"x"),
            (SceneryFaultKind.TrainsetIncomplete, "x.scn", 3, "zly", null)
        }, faults);
    }

    [Fact]
    public void Missing_includes_are_reported_but_optional_ones_and_templates_are_not()
    {
        const string scn = """
            include tory.scm end
            include brak.scm end // komentarz
            include budynek.inc (p1) 0 0 end
            include $(p1)_dom.inc end
            include opcja.scm end //$optional 2,Opcja
            endoptional
            """;

        var faults = TestScenery.Parser().Parse("scenery/x.scn", scn, name => new IncludeLookup(name is "tory.scm" or "budynek.inc")).Faults;

        Assert.Equal((SceneryFaultKind.MissingInclude, "x.scn", 2, null, "brak.scm"), Seen(Assert.Single(faults)));
    }

    [Fact]
    public void An_include_named_in_another_case_is_found_as_the_simulator_finds_it()
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile("game/scenery/x.scn", "FirstInit\ninclude Tomaszewo/Auta.SCM end\n")
            .WithLegacyFile("game/scenery/tomaszewo/auta.scm",
                            "trainset auto road1 0 50\nnode -1 0 a1 dynamic car car car 0 headdriver 3 0 enddynamic\nendtrainset\n");
        var repository = TestScenery.Repository(files);

        var scenery = repository.Load("game/scenery/x.scn")!;

        Assert.Empty(scenery.Faults);
        Assert.Equal("tomaszewo/auta.scm", Assert.Single(scenery.Trainsets).SourceFile);
    }

    [Fact]
    public void An_include_on_disk_only_in_other_letter_case_is_what_linux_misses()
    {
        // The simulator folds the name to lower case and opens it so.
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile("game/scenery/braniewo_szeroki.scn",
                            "include braniewo/base.ctr end\ninclude Braniewo/Tory.SCM end\n")
            .WithFile("game/scenery/Braniewo/Base.ctr", "")
            .WithFile("game/scenery/braniewo/tory.scm", "");
        var repository = TestScenery.Repository(files);

        var faults = repository.Load("game/scenery/braniewo_szeroki.scn")!.Faults;

        Assert.Equal((SceneryFaultKind.IncludeLetterCase, "braniewo_szeroki.scn", 1, "Braniewo/Base.ctr", "braniewo/base.ctr"),
                     Seen(Assert.Single(faults)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_missing_terrain_file_is_no_fault_while_the_scenery_has_its_own_sbt(bool sbt)
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithLegacyFile("game/scenery/Lk2_Sandbox.scn", "include nmt/nmt100_mazowieckie_ter.scm end\nFirstInit\n");
        if (sbt)
            files.WithFile("game/scenery/lk2_sandbox.sbt", "EU07");
        var repository = TestScenery.Repository(files);

        var scenery = repository.Load("game/scenery/Lk2_Sandbox.scn")!;

        Assert.Equal(sbt, scenery.HasCompanionTimetable);
        Assert.Equal(sbt ? 0 : 1, scenery.Faults.Count);
    }
}
