using StarterNG.Infrastructure.Vehicles;

namespace StarterNG.Tests;

public class TexturesTxtParserTests
{
    private static readonly TexturesTxtParser Parser = new();

    [Fact]
    public void A_livery_line_yields_a_texture_with_its_model_and_thumbnail()
    {
        var entry = Parser.Parse("pkp/ep07/", new[] { "ep07-135.mat=ep07.e3d,ep07" });

        var texture = Assert.Single(entry!.Textures);
        Assert.Equal("ep07-135", texture.Skinfile);
        Assert.Equal("pkp/ep07/", texture.Directory);
        Assert.Equal("ep07.e3d", texture.Model);
        Assert.Equal("ep07", texture.MiniRef);
        Assert.False(texture.Wreck);
    }

    [Fact]
    public void The_trailing_comment_is_read_as_the_credit_line()
    {
        var entry = Parser.Parse("pkp/ep07/", new[]
        {
            "ep07-135.mat=ep07.e3d,ep07 // 1.0,EP07-135,PKP Intercity,Warszawa,2019-04-01,Wrocław,Autor,Fotograf"
        });

        var meta = Assert.Single(entry!.Textures).Meta;
        Assert.NotNull(meta);
        Assert.Equal("EP07-135", meta!.Vehicle);
        Assert.Equal("PKP Intercity", meta.Operator);
        Assert.Equal("Warszawa", meta.Depot);
        Assert.Equal("Autor", meta.TextureAuthor);
        Assert.Equal("Fotograf", meta.PhotoAuthor);
    }

    [Fact]
    public void Wreck_liveries_are_marked_so_the_depot_can_hide_them()
    {
        var entry = Parser.Parse("pkp/ep07/", new[] { "ep07wreck.mat=ep07.e3d,ep07" });

        Assert.True(Assert.Single(entry!.Textures).Wreck);
    }

    [Fact]
    public void The_dollar_a_marker_archives_everything_after_it()
    {
        var entry = Parser.Parse("pkp/ep07/", new[]
        {
            "ep07-135.mat=ep07.e3d,ep07",
            "$a",
            "ep07-old.mat=ep07.e3d,ep07old"
        });

        var groups = entry!.Groups;
        Assert.Contains(groups, group => group is { Mini: "ep07", Archived: false });
        Assert.Contains(groups, group => group is { Mini: "ep07old", Archived: true });
    }

    [Fact]
    public void A_caret_opens_a_fixed_set_of_the_following_vehicles()
    {
        var entry = Parser.Parse("pkp/en57/", new[]
        {
            "^3",
            "en57-a.mat=en57.e3d,en57",
            "en57-b.mat=en57.e3d,en57",
            "en57-c.mat=en57.e3d,en57"
        });

        var set = Assert.Single(entry!.Sets);
        Assert.Equal(3, set.Count);
        Assert.Equal(3, set.TextureRefs.Count);
    }

    [Fact]
    public void Extra_equals_segments_become_alternative_models()
    {
        var entry = Parser.Parse("pkp/111a/", new[] { "111a.mat=111a.e3d,111a=111ab.e3d,111ab" });

        var texture = Assert.Single(entry!.Textures);
        Assert.Equal("111a.e3d", texture.Model);
        Assert.Equal("111ab.e3d", Assert.Single(texture.Aliases).Model);
    }

    [Fact]
    public void Comments_headers_and_blank_lines_are_ignored()
    {
        var entry = Parser.Parse("pkp/ep07/", new[] { "", "# nagłówek", "@ coś", "// komentarz", "bez znaku równości" });

        Assert.Null(entry);
    }

    [Fact]
    public void The_category_marker_applies_to_the_liveries_after_it()
    {
        var entry = Parser.Parse("pkp/ep07/", new[] { "!=e", "ep07-135.mat=ep07.e3d,ep07" });

        Assert.Equal("e", Assert.Single(entry!.Groups).Category);
    }
}
