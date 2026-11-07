using StarterNG.Domain.Vehicles;
using StarterNG.Tests.Fakes;
using StarterNG.Views;

namespace StarterNG.Tests;

public class VehicleBaseQueryTests
{
    private static VehicleBaseRow Row(string skin, string? operatorName = null, string? revision = null,
                                      string? author = null, string? depot = null, string? photo = null,
                                      string directory = "dynamic/pkp/", string? model = "model")
    {
        return new VehicleBaseRow(new VehicleTexture
        {
            Directory = directory,
            Skinfile = skin,
            Model = model,
            Meta = new VehicleMeta
            {
                Operator = operatorName,
                RevisionDate = revision,
                TextureAuthor = author,
                Depot = depot,
                PhotoAuthor = photo
            }
        });
    }

    private static IReadOnlyList<VehicleTexture> Catalogued(params string[] textures)
    {
        var files = new InMemoryFileSystem();
        foreach (string text in textures)
            files.WithFile(TestInstallation.At("dynamic", "pkp", text[..text.IndexOf('=')], "textures.txt"),
                           text[(text.IndexOf('=') + 1)..]);
        var installation = new TestInstallation(files);
        installation.Vehicles.Load(installation.Library.Vehicles);
        return installation.Library.Vehicles.Textures;
    }

    private static string[] Skins(IEnumerable<VehicleBaseRow> rows) => rows.Select(r => r.Skin).ToArray();

    private static string[] Selected(VehicleBaseFilters filters, params VehicleBaseRow[] rows) =>
        Skins(VehicleBaseQuery.Select(rows, filters, r => r.Skin, descending: false));

    [Fact]
    public void A_question_mark_in_the_credit_line_is_what_its_author_did_not_know()
    {
        var row = Row("a", operatorName: "?", revision: "?", author: "Jan");

        Assert.Equal("", row.Operator);
        Assert.Equal("", row.Revision);
        Assert.Null(row.Date);
        Assert.Equal("", row.DateKey);
        Assert.Equal("Jan", row.Author);
    }

    [Fact]
    public void The_date_key_sorts_as_the_dates_do() =>
        Assert.Equal("20081201", Row("a", revision: "xx.12.2008").DateKey);

    [Fact]
    public void Each_filter_looks_in_its_own_field_ignoring_case_and_spaces_around()
    {
        var a = Row("alfa", operatorName: "PKP", author: "Jan", depot: "Lublin", photo: "Ewa", directory: "dynamic/one/");
        var b = Row("beta", operatorName: "CD", author: "Olaf", depot: "Praha", photo: "Karel", directory: "dynamic/two/");

        Assert.Equal(new[] { "alfa" }, Selected(new VehicleBaseFilters { Name = " ALF " }, a, b));
        Assert.Equal(new[] { "beta" }, Selected(new VehicleBaseFilters { Operator = "cd" }, a, b));
        Assert.Equal(new[] { "alfa" }, Selected(new VehicleBaseFilters { Author = "jan" }, a, b));
        Assert.Equal(new[] { "beta" }, Selected(new VehicleBaseFilters { Station = "prah" }, a, b));
        Assert.Equal(new[] { "alfa" }, Selected(new VehicleBaseFilters { Photo = "EW" }, a, b));
        Assert.Equal(new[] { "beta" }, Selected(new VehicleBaseFilters { Model = "TWO/" }, a, b));
        Assert.Empty(Selected(new VehicleBaseFilters { Name = "alf", Operator = "cd" }, a, b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_filter_with_nothing_typed_lets_everything_through(string? typed) =>
        Assert.Equal(new[] { "a", "b" }, Selected(new VehicleBaseFilters { Name = typed }, Row("a"), Row("b")));

    [Theory]
    [InlineData("2016", null, true)]
    [InlineData("2016-12", null, true)]
    [InlineData("2017", null, false)]
    [InlineData(null, "2016", true)]
    [InlineData(null, "2015", false)]
    [InlineData("2010", "2020", true)]
    public void A_revision_counts_while_its_span_overlaps_what_is_asked(string? from, string? to, bool shown)
    {
        var row = Row("a", revision: "xx.xx.2016");

        var found = Selected(new VehicleBaseFilters { RevisionFrom = from, RevisionTo = to }, row);

        Assert.Equal(shown, found.Length == 1);
    }

    [Fact]
    public void A_month_only_date_overlaps_at_both_of_its_ends()
    {
        var row = Row("a", revision: "xx.06.2008");

        Assert.Single(Selected(new VehicleBaseFilters { RevisionFrom = "30.06.2008" }, row));
        Assert.Single(Selected(new VehicleBaseFilters { RevisionTo = "01.06.2008" }, row));
        Assert.Empty(Selected(new VehicleBaseFilters { RevisionFrom = "01.07.2008" }, row));
        Assert.Empty(Selected(new VehicleBaseFilters { RevisionTo = "31.05.2008" }, row));
    }

    [Fact]
    public void A_revision_asked_for_leaves_out_what_has_no_date_and_what_cannot_be_read_asks_nothing()
    {
        var dated = Row("dated", revision: "2016");
        var undated = Row("undated");

        Assert.Equal(new[] { "dated" }, Selected(new VehicleBaseFilters { RevisionFrom = "2000" }, dated, undated));
        Assert.Equal(new[] { "dated", "undated" },
                     Selected(new VehicleBaseFilters { RevisionFrom = "soon" }, dated, undated));
    }

    [Theory]
    [InlineData(false, new[] { "b", "c", "a", "d" })]
    [InlineData(true, new[] { "c", "b", "a", "d" })]
    public void Blanks_stay_at_the_bottom_whichever_way_a_column_is_sorted_and_the_name_settles_a_tie(
        bool descending, string[] order)
    {
        var rows = new[] { Row("d"), Row("c", operatorName: "Z"), Row("a"), Row("b", operatorName: "M") };

        Assert.Equal(order, Skins(VehicleBaseQuery.Select(rows, new VehicleBaseFilters(), r => r.Operator, descending)));
    }

    [Fact]
    public void The_class_list_counts_a_class_under_one_name_whatever_its_case_in_order_of_the_names()
    {
        var textures = Catalogued("b111=!=b\nb111-01.mat=b111,B111\nb111-02.mat=b222,b111",
                                  "ep07=!=e\nep07-01.mat=ep07,AP07");

        var counts = VehicleBaseQuery.ClassCounts(textures.Select(t => new VehicleBaseRow(t)));

        Assert.Equal(new[] { ("AP07", 1), ("B111", 2) },
                     counts.Select(c => (c.Name.ToUpperInvariant(), c.Count)));
    }

    [Fact]
    public void A_row_with_no_class_is_not_counted() =>
        Assert.Empty(VehicleBaseQuery.ClassCounts(new[] { Row("a") }));

    [Fact]
    public void The_class_picked_has_to_match_whole_while_the_typed_one_only_has_to_appear()
    {
        var textures = Catalogued("b111=!=b\nb111-01.mat=b111,B111");
        var row = new VehicleBaseRow(textures[0]);
        Assert.NotEqual("", row.Class);

        Assert.Single(VehicleBaseQuery.Select(new[] { row }, new VehicleBaseFilters { PickedClass = row.Class.ToUpperInvariant() },
                                              r => r.Skin, descending: false));
        Assert.Empty(VehicleBaseQuery.Select(new[] { row }, new VehicleBaseFilters { PickedClass = row.Class[..1] + "?" },
                                             r => r.Skin, descending: false));
        Assert.Single(VehicleBaseQuery.Select(new[] { row }, new VehicleBaseFilters { Class = row.Class[..1] },
                                              r => r.Skin, descending: false));
    }
}
