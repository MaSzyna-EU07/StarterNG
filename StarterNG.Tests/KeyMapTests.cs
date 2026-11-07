using StarterNG.Classes;

namespace StarterNG.Tests;

public class KeyMapTests
{
    /// <summary>The names of the simulator's key table, keytonamemap in input/keyboardinput.cpp.</summary>
    private static readonly HashSet<string> SimulatorKeyNames = new(StringComparer.Ordinal)
    {
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "-", "=",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
        "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "backspace", "[", "]", "\\", ";", "'", "enter", ",", ".", "/", "space", "pause",
        "insert", "delete", "home", "end",
        "num_/", "num_*", "num_-", "num_+", "num_enter", "num_.",
        "num_0", "num_1", "num_2", "num_3", "num_4", "num_5", "num_6", "num_7", "num_8", "num_9",
        "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
        "tab", "esc", "left", "right", "up", "down", "page_up", "page_down"
    };

    [Theory]
    [InlineData("tab", "KbUseMap")]
    [InlineData("f1", "KbUseConsist")]
    [InlineData("f2", "KbUseTimetable")]
    [InlineData("f3", "KbUseScenario")]
    [InlineData("f9", "KbUseLog")]
    [InlineData("f10", "KbUseQuit")]
    public void What_the_simulator_takes_first_blocks_only_without_a_modifier(string token, string text)
    {
        Assert.Equal(new SimulatorKeyUse(text, true), KeyMap.SimulatorUse(token, false, false));
        Assert.Null(KeyMap.SimulatorUse(token, true, false));
        Assert.Null(KeyMap.SimulatorUse(token, false, true));
    }

    [Fact]
    public void Shift_F12_blocks_and_F12_alone_is_free()
    {
        Assert.Equal(new SimulatorKeyUse("KbUseDebugPanel", true), KeyMap.SimulatorUse("f12", true, false));
        Assert.Null(KeyMap.SimulatorUse("f12", false, false));
    }

    [Theory]
    [InlineData("f4", false, false, "KbUseCab")]
    [InlineData("f4", true, false, "KbUseCab")]
    [InlineData("f5", false, true, "KbUseVehicle")]
    [InlineData("f6", false, false, "KbUseDebug")]
    [InlineData("f7", false, true, "KbUseDebug")]
    [InlineData("f11", false, false, "KbUseEditor")]
    [InlineData("3", true, false, "KbUseSceneryEvent")]
    [InlineData("3", false, true, "KbUseCamera")]
    public void What_is_left_to_a_key_a_binding_may_replace(string token, bool shift, bool ctrl, string text)
    {
        Assert.Equal(new SimulatorKeyUse(text, false), KeyMap.SimulatorUse(token, shift, ctrl));
    }

    [Theory]
    [InlineData("3", false, false)]
    [InlineData("a", false, false)]
    [InlineData("a", true, true)]
    [InlineData("f7", true, false)]
    [InlineData("f8", false, false)]
    [InlineData("f11", true, false)]
    [InlineData("num_5", true, false)]
    [InlineData("enter", false, false)]
    public void The_rest_the_simulator_leaves_alone(string token, bool shift, bool ctrl)
    {
        Assert.Null(KeyMap.SimulatorUse(token, shift, ctrl));
    }

    [Fact]
    public void The_bundled_keys_use_nothing_the_simulator_takes_first()
    {
        var config = new KeyboardConfig();
        string path = Path.Combine(AppContext.BaseDirectory, "startercfg", "eu07_input-keyboard.ini");
        config.Parse(File.ReadAllText(path));

        var taken = config.Bindings
            .Where(b => b.IsAssigned && KeyMap.SimulatorUse(b.Key, b.Shift, b.Ctrl) is { Blocks: true })
            .Select(b => b.Command)
            .ToList();

        Assert.Empty(taken);
        Assert.Contains(config.Bindings, b => b.IsAssigned);
    }

    [Fact]
    public void Every_key_drawn_is_one_the_simulator_knows()
    {
        var drawn = KeyMap.MainBlock.Concat(KeyMap.NumpadBlock).Concat(KeyMap.NavBlock)
            .SelectMany(row => row)
            .Select(cap => cap.Token)
            .OfType<string>();

        Assert.Empty(drawn.Where(token => !SimulatorKeyNames.Contains(token)));
    }

    [Theory]
    [InlineData("page_up", "Page Up")]
    [InlineData("page_down", "Page Down")]
    [InlineData("esc", "Esc")]
    [InlineData("num_enter", "Num Enter")]
    public void Key_names_read_as_on_a_keyboard(string token, string expected)
    {
        Assert.Equal(expected, KeyMap.DisplayName(token));
    }

    [Theory]
    [InlineData("a", false, false, "A")]
    [InlineData("a", true, false, "Shift + A")]
    [InlineData("a", false, true, "Ctrl + A")]
    [InlineData("num_enter", true, true, "Ctrl + Shift + Num Enter")]
    [InlineData("none", true, true, "—")]
    [InlineData("NONE", false, true, "—")]
    [InlineData("", true, false, "—")]
    public void A_combination_reads_with_its_modifiers_unless_there_is_no_key(string token, bool shift, bool ctrl, string expected)
    {
        Assert.Equal(expected, KeyMap.ComboText(token, shift, ctrl));
    }
}
