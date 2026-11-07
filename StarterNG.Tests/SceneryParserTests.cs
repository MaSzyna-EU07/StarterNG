using StarterNG.Domain.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryParserTests
{
    [Fact]
    public void Directives_become_the_scenery_headline()
    {
        const string scn = """
            //$l Śląsk
            //$n Osobowy do Katowic
            //$d Pierwszy opis
            //$d drugi wiersz
            //$i images/kato.jpg
            """;

        var scenery = TestScenery.Parser().Parse("scenery/kato.scn", scn);

        Assert.Equal("Śląsk", scenery.Group);
        Assert.Equal("Osobowy do Katowic", scenery.Name);
        Assert.Equal("Pierwszy opis\ndrugi wiersz", scenery.Description);
        Assert.Equal("images/kato.jpg", scenery.ImageName);
        Assert.False(scenery.Archival);
        Assert.Equal("kato", scenery.DisplayName);
    }

    [Fact]
    public void The_a_directive_marks_a_scenery_archival()
    {
        var scenery = TestScenery.Parser().Parse("scenery/old.scn", "//$a\n//$n Stary\n");

        Assert.True(scenery.Archival);
    }

    [Fact]
    public void Attachments_take_a_label_from_the_rest_of_the_line()
    {
        const string scn = """
            //$f 01 doc/rozklad.pdf Rozkład jazdy
            //$f doc/mapa.png Mapa
            //$f doc/notes.txt
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        Assert.Collection(scenery.Attachments,
            first =>
            {
                Assert.Equal("doc/rozklad.pdf", first.FilePath);
                Assert.Equal("Rozkład jazdy", first.Label);
            },
            second =>
            {
                Assert.Equal("doc/mapa.png", second.FilePath);
                Assert.Equal("Mapa", second.Label);
            },
            third =>
            {
                Assert.Equal("doc/notes.txt", third.FilePath);
                Assert.Equal("notes.txt", third.Label);
            });
    }

    [Fact]
    public void Optional_includes_are_lifted_out_of_the_template()
    {
        const string scn = """
            include zima.inc end //$optional 1, Sceneria zimowa
            some content
            endoptional
            FirstInit
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        var include = Assert.Single(scenery.Includes);
        Assert.Equal("zima.inc", include.FilePath);
        Assert.Equal("Sceneria zimowa", include.Desc);
        Assert.Equal(1, include.Kind);
        Assert.True(include.Selected);
        Assert.DoesNotContain("zima.inc", scenery.Template);
    }

    [Fact]
    public void Includes_of_kind_two_are_off_until_export_decides()
    {
        const string scn = """
            include rozklad.inc end //$optional 2, Rozkład wbudowany
            x
            endoptional
            """;

        var include = Assert.Single(TestScenery.Parser().Parse("scenery/x.scn", scn).Includes);

        Assert.Equal(2, include.Kind);
        Assert.False(include.Selected);
    }

    [Fact]
    public void Trainsets_are_parsed_and_replaced_by_placeholders()
    {
        const string scn = """
            //$n Test
            trainset pociag tor_1 10 0
            node -1 -1 ep07-001 dynamic PKP/EP07 ep07 ep07.mmd 0 headdriver 0
            endtrainset
            FirstInit
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        var trainset = Assert.Single(scenery.Trainsets);
        Assert.True(trainset.Parsed);
        Assert.Equal("pociag", trainset.Name);
        Assert.Equal("tor_1", trainset.Track);
        Assert.Contains("{{0}}", scenery.Template);
        Assert.DoesNotContain("endtrainset", scenery.Template);
    }

    [Fact]
    public void Vehicles_outside_a_trainset_are_collected_as_loose()
    {
        const string scn = """
            node -1 -1 wagon_1 dynamic PKP/111A 111a 111a.mmd none 0 nobody 0 0 enddynamic
            FirstInit
            """;

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        var vehicle = Assert.Single(scenery.LooseVehicles);
        Assert.Equal("wagon_1", vehicle.Name);
        Assert.Equal("PKP/111A", vehicle.DataFolder);
        Assert.Equal(new[] { "wagon_1" }, scenery.LooseVehicleNames);
        Assert.DoesNotContain("enddynamic", scenery.Template);
    }

    [Fact]
    public void A_malformed_loose_vehicle_is_reported_and_left_in_place()
    {
        const string scn = "node x -1 wagon_1 dynamic PKP/111A 111a 111a.mmd none 0 nobody 0 0 enddynamic\n";

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", scn);

        Assert.Empty(scenery.LooseVehicles);
        Assert.Equal(new[] { "wagon_1" }, scenery.LooseVehicleNames);
        var fault = Assert.Single(scenery.Faults);
        Assert.Equal((SceneryFaultKind.NotANumber, "wagon_1", "x", 1), (fault.Kind, fault.Subject, fault.Token, fault.Line));
        Assert.Contains("enddynamic", scenery.Template);
    }

    [Fact]
    public void Directives_in_a_file_put_in_place_are_not_the_scenery_s_own()
    {
        const string included = """
            //$n Auta
            //$l Inne
            //$d Opis aut
            //$f doc/auta.pdf Auta
            //$i auta.jpg
            //$a
            trainset auto road1 0 50
            endtrainset
            """;
        string text = SceneryIncludes.Inline("//$d Opis scenerii\n//$f doc/mapa.png Mapa\nFirstInit\ninclude auta.scm end\n",
                                             name => (name, included));

        var scenery = TestScenery.Parser().Parse("scenery/x.scn", text);

        Assert.Equal("Opis scenerii", scenery.Description);
        Assert.Equal("doc/mapa.png", Assert.Single(scenery.Attachments).FilePath);
        Assert.Null(scenery.Name);
        Assert.Null(scenery.Group);
        Assert.Null(scenery.ImageName);
        Assert.False(scenery.Archival);
        // The file's own trainset is still listed, and its text still written for a start.
        Assert.Equal("auta.scm", Assert.Single(scenery.Trainsets).SourceFile);
        Assert.Contains("//$d Opis aut", scenery.BuildExportContent(skipDecorTrainsets: false, new StubRandom()));
    }
}
