using StarterNG.Classes;

namespace StarterNG.Tests;

public class ConfigFileTests
{
    private static string[] Lines(ConfigFile config) =>
        config.ToText().Split(Environment.NewLine);

    [Fact]
    public void A_file_is_written_with_the_hosts_line_endings_whichever_it_came_with()
    {
        // parser.cpp breaks words on \r as on \n, so the simulator takes either.
        string nl = Environment.NewLine;

        Assert.Equal($"a 1{nl}b 2{nl}", ConfigFile.Parse("a 1\nb 2\n").ToText());
        Assert.Equal($"a 1{nl}b 2{nl}", ConfigFile.Parse("a 1\r\nb 2\r\n").ToText());
    }

    [Fact]
    public void Removing_a_key_comments_every_line_that_sets_it()
    {
        var config = ConfigFile.Parse("width 800\nheight 600\nwidth 1024\nWIDTH 1920\n");

        config.Remove("width");

        Assert.False(config.Has("width"));
        Assert.Equal(new[] { "// width 800", "height 600", "// width 1024", "// WIDTH 1920", "" }, Lines(config));
        // Parsed again, nothing of it comes back.
        Assert.False(ConfigFile.Parse(config.ToText()).Has("width"));
    }

    [Fact]
    public void A_removed_key_keeps_its_comment_and_indent()
    {
        var config = ConfigFile.Parse("  width 800\t// mine\n");

        config.Remove("width");

        Assert.Equal("  // width 800\t// mine", Lines(config)[0]);
    }

    [Fact]
    public void Removing_a_key_leaves_what_only_mentions_it()
    {
        var config = ConfigFile.Parse("// width 800\nwidth.max 900\nwidth 1024\n");

        config.Remove("width");

        Assert.Equal(new[] { "// width 800", "width.max 900", "// width 1024", "" }, Lines(config));
    }

    [Fact]
    public void A_new_key_goes_before_endconfig()
    {
        // Globals.cpp stops reading at endconfig.
        var config = ConfigFile.Parse("width 800\nendconfig\n// notes\n");

        config.Set("height", "600");
        config.Set("fullscreen", "no");

        Assert.Equal(new[] { "width 800", "height 600", "fullscreen no", "endconfig", "// notes", "" }, Lines(config));
    }

    [Theory]
    [InlineData("endconfig")]
    [InlineData("ENDCONFIG")]
    [InlineData("  EndConfig  // the end")]
    public void Endconfig_is_known_as_the_simulator_knows_it(string line)
    {
        // Its parser lower-cases every word.
        var config = ConfigFile.Parse(line + "\n");

        config.Set("width", "800");

        Assert.Equal("width 800", Lines(config)[0]);
    }

    [Fact]
    public void Endconfig_in_a_comment_ends_nothing()
    {
        var config = ConfigFile.Parse("// endconfig\n");

        config.Set("width", "800");

        Assert.Equal(new[] { "// endconfig", "", "width 800" }, Lines(config));
    }

    [Fact]
    public void Keys_found_after_an_insertion_are_still_the_right_lines()
    {
        var config = ConfigFile.Parse("width 800\nendconfig\nlate 1\n");

        config.Set("height", "600");
        config.Set("width", "1024");
        config.Set("height", "720");

        Assert.Equal(new[] { "width 1024", "height 720", "endconfig", "late 1", "" }, Lines(config));
        Assert.Equal("1024", config.GetRaw("width"));
        Assert.Equal("720", config.GetRaw("height"));
        Assert.Equal("1", config.GetRaw("late"));
    }

    [Fact]
    public void A_key_past_endconfig_is_read_but_known_to_be_past_it()
    {
        var config = ConfigFile.Parse("width 800\nendconfig\nheight 600\n");

        Assert.Equal("600", config.GetRaw("height"));
        Assert.True(config.IsPastEnd("height"));
        Assert.False(config.IsPastEnd("width"));
        Assert.False(config.IsPastEnd("endconfig"));
        Assert.False(config.IsPastEnd("absent"));
    }

    [Fact]
    public void A_key_set_past_endconfig_moves_before_it_with_its_comment()
    {
        var config = ConfigFile.Parse("endconfig\n  height 600\t// mine\nlate 1\n");

        config.Set("height", "720");

        Assert.Equal(new[] { "  height 720\t// mine", "endconfig", "late 1", "" }, Lines(config));
        Assert.False(config.IsPastEnd("height"));
        Assert.True(config.IsPastEnd("late"));
    }

    [Fact]
    public void Of_a_key_on_both_sides_of_endconfig_the_line_the_simulator_reads_counts()
    {
        var config = ConfigFile.Parse("width 800\nendconfig\nwidth 1920\n");

        Assert.Equal("800", config.GetRaw("width"));
        Assert.False(config.IsPastEnd("width"));

        config.Set("width", "1024");

        Assert.Equal(new[] { "width 1024", "endconfig", "width 1920", "" }, Lines(config));
    }

    [Fact]
    public void A_key_past_endconfig_is_removed_like_any_other()
    {
        var config = ConfigFile.Parse("width 800\nendconfig\nwidth 1920\nheight 600\n");

        config.Remove("width");
        config.Remove("height");

        Assert.Equal(new[] { "// width 800", "endconfig", "// width 1920", "// height 600", "" }, Lines(config));
    }

    [Fact]
    public void Without_endconfig_a_new_key_goes_at_the_end_as_it_did()
    {
        var config = ConfigFile.Parse("width 800");

        config.Set("height", "600");

        Assert.Equal(new[] { "width 800", "height 600" }, Lines(config));
    }
}
