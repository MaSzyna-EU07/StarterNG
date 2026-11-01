using StarterNG.Classes;

namespace StarterNG.Tests;

public class KeyboardConfigTests
{
    [Fact]
    public void A_description_without_polish_letters_matches_the_bundled_one() =>
        Assert.Equal("zwiekszenie nastawnika glownego", KeyboardConfig.StripPolish("zwiększenie nastawnika głównego"));

    private static KeyBinding Parse(string line)
    {
        var config = new KeyboardConfig();
        config.Parse(line);
        return Assert.Single(config.Bindings);
    }

    [Fact]
    public void A_binding_without_a_description_shows_the_translated_one()
    {
        var binding = Parse("hornhighactivate a");

        string text = new KeyboardConfig().DescriptionOf(binding, _ => "high tone horn");

        Assert.Equal("high tone horn", text);
    }

    [Fact]
    public void A_description_someone_wrote_wins_over_the_translation()
    {
        var binding = Parse("hornhighactivate a // my own horn");

        string text = new KeyboardConfig().DescriptionOf(binding, _ => "high tone horn");

        Assert.Equal("my own horn", text);
    }

    [Fact]
    public void Without_a_translation_the_file_description_stays()
    {
        var binding = Parse("hornhighactivate a // syrena wysoka");

        Assert.Equal("syrena wysoka", new KeyboardConfig().DescriptionOf(binding, _ => string.Empty));
    }

    // ── Which binding sits where ─────────────────────────────────────────────

    private static KeyboardConfig Config(params string[] lines)
    {
        var config = new KeyboardConfig();
        config.Parse(string.Join("\n", lines));
        return config;
    }

    [Fact]
    public void A_cleared_binding_is_on_no_key_and_no_modifier()
    {
        var binding = Parse("hornhighactivate shift ctrl a");

        binding.Clear();

        Assert.False(binding.IsAssigned);
        Assert.False(binding.Shift);
        Assert.False(binding.Ctrl);
        Assert.Equal("none", binding.Key);
    }

    [Theory]
    [InlineData("a", true, false, true)]
    [InlineData("A", true, false, true)]
    [InlineData("a", false, false, false)]
    [InlineData("a", true, true, false)]
    [InlineData("b", true, false, false)]
    public void A_binding_is_on_its_key_with_exactly_its_modifiers(string token, bool shift, bool ctrl, bool on) =>
        Assert.Equal(on, Parse("hornhighactivate shift a").IsOn(token, shift, ctrl));

    [Fact]
    public void An_unassigned_binding_is_on_nothing_not_even_the_word_none() =>
        Assert.False(Parse("hornhighactivate none").IsOn("none", false, false));

    [Fact]
    public void The_binding_at_a_combination_is_found_by_key_and_modifiers()
    {
        var config = Config("plain a", "shifted shift a");

        Assert.Equal("plain", config.BindingAt("A", false, false)?.Command);
        Assert.Equal("shifted", config.BindingAt("a", true, false)?.Command);
        Assert.Null(config.BindingAt("a", false, true));
    }

    [Fact]
    public void A_combination_put_on_a_binding_moves_there_from_the_one_that_had_it()
    {
        var config = Config("first ctrl a", "second b", "third a");

        config.Put(config.Bindings[1], "a", shift: false, ctrl: true);

        Assert.False(config.Bindings[0].IsAssigned);
        Assert.False(config.Bindings[0].Ctrl);
        Assert.True(config.Bindings[1].IsOn("a", false, true));
        // The same key with other modifiers is another combination.
        Assert.True(config.Bindings[2].IsOn("a", false, false));
    }

    [Fact]
    public void Every_binding_doubled_up_on_a_combination_leaves_it_but_the_one_put_there()
    {
        var config = Config("first a", "second a", "third a");

        config.Put(config.Bindings[1], "a", shift: false, ctrl: false);

        Assert.False(config.Bindings[0].IsAssigned);
        Assert.True(config.Bindings[1].IsOn("a", false, false));
        Assert.False(config.Bindings[2].IsAssigned);
    }

    [Fact]
    public void Nothing_put_on_a_combination_frees_it()
    {
        var config = Config("first shift a", "second b");

        config.Put(null, "a", shift: true, ctrl: false);

        Assert.False(config.Bindings[0].IsAssigned);
        Assert.True(config.Bindings[1].IsOn("b", false, false));
    }

    [Fact]
    public void A_combination_put_back_on_the_binding_that_has_it_stays()
    {
        var config = Config("first a");

        config.Put(config.Bindings[0], "a", shift: false, ctrl: false);

        Assert.True(config.Bindings[0].IsOn("a", false, false));
    }

    [Fact]
    public void Two_bindings_on_a_combination_conflict_however_the_key_is_spelled()
    {
        var config = Config("first shift a", "second shift A", "third a", "fourth ctrl a", "fifth none", "sixth none");

        var conflicts = config.ConflictingCombos();

        Assert.Equal(config.Bindings[0].ComboKey, Assert.Single(conflicts));
        Assert.Contains(config.Bindings[1].ComboKey, conflicts);
        Assert.DoesNotContain(config.Bindings[2].ComboKey, conflicts);
    }

    [Fact]
    public void Only_bindings_on_a_key_of_the_numpad_count_as_on_it()
    {
        var config = Config("first num_1", "second shift NUM_enter", "third a", "fourth none");

        Assert.Equal(2, config.BindingsOnNumpad());
    }
}
