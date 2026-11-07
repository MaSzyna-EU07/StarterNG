using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;

namespace StarterNG.Classes;

public sealed class KeyCap
{
    public string Label = string.Empty;
    public string? Token;
    public double Width = 1.0;

    public KeyCap(string label, string? token = null, double width = 1.0)
    {
        Label = label;
        Token = token;
        Width = width;
    }
}

/// <summary>What the simulator does with a key combination on its own.</summary>
/// <param name="TextKey">What that is, as a translation key.</param>
/// <param name="Blocks">
/// It gets there ahead of the key file, so a binding here would never fire.
/// Otherwise it is what is left to the key when no binding takes it.
/// </param>
public readonly record struct SimulatorKeyUse(string TextKey, bool Blocks);

public static class KeyMap
{
    public const string NumpadPrefix = "num_";

    /// <summary>
    /// Read from the simulator's code: ui_layer::on_key and driver_ui::on_key are asked first and only
    /// without a modifier, driver_mode::on_key takes Shift+F12 itself, then comes the key
    /// file, and driver_mode::OnKeyDown gets what that leaves.
    /// </summary>
    public static SimulatorKeyUse? SimulatorUse(string token, bool shift, bool ctrl)
    {
        bool plain = !shift && !ctrl;
        switch (token.ToLowerInvariant())
        {
            case "tab" when plain: return new SimulatorKeyUse("KbUseMap", true);
            case "f1" when plain: return new SimulatorKeyUse("KbUseConsist", true);
            case "f2" when plain: return new SimulatorKeyUse("KbUseTimetable", true);
            case "f3" when plain: return new SimulatorKeyUse("KbUseScenario", true);
            case "f9" when plain: return new SimulatorKeyUse("KbUseLog", true);
            case "f10" when plain: return new SimulatorKeyUse("KbUseQuit", true);
            case "f12" when shift: return new SimulatorKeyUse("KbUseDebugPanel", true);

            case "f4": return new SimulatorKeyUse("KbUseCab", false);
            case "f5": return new SimulatorKeyUse("KbUseVehicle", false);
            case "f6": return new SimulatorKeyUse("KbUseDebug", false);
            case "f7" when plain || ctrl: return new SimulatorKeyUse("KbUseDebug", false);
            case "f11" when plain: return new SimulatorKeyUse("KbUseEditor", false);
            case [>= '0' and <= '9'] when shift: return new SimulatorKeyUse("KbUseSceneryEvent", false);
            case [>= '0' and <= '9'] when ctrl: return new SimulatorKeyUse("KbUseCamera", false);
            default: return null;
        }
    }

    public static string? FromInput(Key key, PhysicalKey physical)
    {

        switch (physical)
        {
            case PhysicalKey.NumPad0: return "num_0";
            case PhysicalKey.NumPad1: return "num_1";
            case PhysicalKey.NumPad2: return "num_2";
            case PhysicalKey.NumPad3: return "num_3";
            case PhysicalKey.NumPad4: return "num_4";
            case PhysicalKey.NumPad5: return "num_5";
            case PhysicalKey.NumPad6: return "num_6";
            case PhysicalKey.NumPad7: return "num_7";
            case PhysicalKey.NumPad8: return "num_8";
            case PhysicalKey.NumPad9: return "num_9";
            case PhysicalKey.NumPadAdd: return "num_+";
            case PhysicalKey.NumPadSubtract: return "num_-";
            case PhysicalKey.NumPadMultiply: return "num_*";
            case PhysicalKey.NumPadDivide: return "num_/";
            case PhysicalKey.NumPadDecimal: return "num_.";
            case PhysicalKey.NumPadEnter: return "num_enter";
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "num_" + (char)('0' + (key - Key.NumPad0));

        if (key >= Key.A && key <= Key.Z)
            return ((char)('a' + (key - Key.A))).ToString();

        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        return key switch
        {
            Key.Add => "num_+",
            Key.Subtract => "num_-",
            Key.Multiply => "num_*",
            Key.Divide => "num_/",
            Key.Decimal => "num_.",
            Key.Space => "space",
            Key.Home => "home",
            Key.End => "end",
            Key.Insert => "insert",
            Key.Delete => "delete",
            Key.Back => "backspace",
            Key.Pause => "pause",
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemBackslash or Key.OemPipe => "\\",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Enter => "enter",
            Key.Tab => "tab",
            Key.PageUp => "page_up",
            Key.PageDown => "page_down",
            Key.Left => "left",
            Key.Right => "right",
            Key.Up => "up",
            Key.Down => "down",
            Key.F1 => "f1", Key.F2 => "f2", Key.F3 => "f3", Key.F4 => "f4",
            Key.F5 => "f5", Key.F6 => "f6", Key.F7 => "f7", Key.F8 => "f8",
            Key.F9 => "f9", Key.F10 => "f10", Key.F11 => "f11", Key.F12 => "f12",
            _ => null
        };
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftShift or Key.RightShift or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    /// <summary>A key combination as it reads on screen; a dash for no key, whatever the modifiers.</summary>
    public static string ComboText(string? token, bool shift, bool ctrl)
    {
        if (IsNoKey(token))
            return "—";
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        parts.Add(DisplayName(token));
        return string.Join(" + ", parts);
    }

    private static bool IsNoKey([NotNullWhen(false)] string? token) =>
        string.IsNullOrEmpty(token) || token.Equals("none", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string? token)
    {
        if (IsNoKey(token))
            return "—";

        if (token.StartsWith(NumpadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string rest = token.Substring(NumpadPrefix.Length);
            return rest switch
            {
                "enter" => "Num Enter",
                "+" => "Num +",
                "-" => "Num -",
                "*" => "Num *",
                "/" => "Num /",
                "." => "Num .",
                _ => "Num " + rest
            };
        }

        return token switch
        {
            "space" => "Space",
            "home" => "Home",
            "end" => "End",
            "insert" => "Insert",
            "delete" => "Delete",
            "page_up" => "Page Up",
            "page_down" => "Page Down",
            "esc" => "Esc",
            "backspace" => "Backspace",
            "pause" => "Pause",
            "\\" => "\\",
            _ when token.Length == 1 => token.ToUpperInvariant(),
            _ => char.ToUpperInvariant(token[0]) + token.Substring(1)
        };
    }

    public static readonly KeyCap[][] MainBlock =
    {

        new[]
        {
            new KeyCap("Esc", "esc", 1.4),
            new KeyCap("F1", "f1"), new KeyCap("F2", "f2"), new KeyCap("F3", "f3"), new KeyCap("F4", "f4"),
            new KeyCap("F5", "f5"), new KeyCap("F6", "f6"), new KeyCap("F7", "f7"), new KeyCap("F8", "f8"),
            new KeyCap("F9", "f9"), new KeyCap("F10", "f10"), new KeyCap("F11", "f11"), new KeyCap("F12", "f12"),
        },
        new[]
        {
            new KeyCap("`", null),
            new KeyCap("1", "1"), new KeyCap("2", "2"), new KeyCap("3", "3"), new KeyCap("4", "4"),
            new KeyCap("5", "5"), new KeyCap("6", "6"), new KeyCap("7", "7"), new KeyCap("8", "8"),
            new KeyCap("9", "9"), new KeyCap("0", "0"),
            new KeyCap("-", "-"), new KeyCap("=", "="),
            new KeyCap("Backspace", "backspace", 2.0),
        },
        new[]
        {
            new KeyCap("Tab", "tab", 1.5),
            new KeyCap("Q", "q"), new KeyCap("W", "w"), new KeyCap("E", "e"), new KeyCap("R", "r"),
            new KeyCap("T", "t"), new KeyCap("Y", "y"), new KeyCap("U", "u"), new KeyCap("I", "i"),
            new KeyCap("O", "o"), new KeyCap("P", "p"),
            new KeyCap("[", "["), new KeyCap("]", "]"),
            new KeyCap("\\", "\\", 1.5),
        },
        new[]
        {
            new KeyCap("Caps", null, 1.8),
            new KeyCap("A", "a"), new KeyCap("S", "s"), new KeyCap("D", "d"), new KeyCap("F", "f"),
            new KeyCap("G", "g"), new KeyCap("H", "h"), new KeyCap("J", "j"), new KeyCap("K", "k"),
            new KeyCap("L", "l"),
            new KeyCap(";", ";"), new KeyCap("'", "'"),
            new KeyCap("Enter", "enter", 2.2),
        },
        new[]
        {
            new KeyCap("Shift", null, 2.3),
            new KeyCap("Z", "z"), new KeyCap("X", "x"), new KeyCap("C", "c"), new KeyCap("V", "v"),
            new KeyCap("B", "b"), new KeyCap("N", "n"), new KeyCap("M", "m"),
            new KeyCap(",", ","), new KeyCap(".", "."), new KeyCap("/", "/"),
            new KeyCap("Shift", null, 2.7),
        },
        new[]
        {
            new KeyCap("Ctrl", null, 1.5),
            new KeyCap("Win", null, 1.3),
            new KeyCap("Alt", null, 1.3),
            new KeyCap("Space", "space", 6.0),
            new KeyCap("Alt", null, 1.3),
            new KeyCap("Ctrl", null, 1.5),
        },
    };

    public static readonly KeyCap[][] NumpadBlock =
    {
        new[]
        {
            new KeyCap("Num", null), new KeyCap("/", "num_/"),
            new KeyCap("*", "num_*"), new KeyCap("-", "num_-"),
        },
        new[]
        {
            new KeyCap("7", "num_7"), new KeyCap("8", "num_8"),
            new KeyCap("9", "num_9"), new KeyCap("+", "num_+"),
        },
        new[]
        {
            new KeyCap("4", "num_4"), new KeyCap("5", "num_5"),
            new KeyCap("6", "num_6"), new KeyCap("", null),
        },
        new[]
        {
            new KeyCap("1", "num_1"), new KeyCap("2", "num_2"),
            new KeyCap("3", "num_3"), new KeyCap("Ent", "num_enter"),
        },
        new[]
        {
            new KeyCap("0", "num_0", 2.0), new KeyCap(".", "num_."), new KeyCap("", null),
        },
    };

    public static readonly KeyCap[][] NavBlock =
    {

        new[]
        {
            new KeyCap("Ins", "insert"), new KeyCap("Home", "home"), new KeyCap("PgUp", "page_up"),
        },
        new[]
        {
            new KeyCap("Del", "delete"), new KeyCap("End", "end"), new KeyCap("PgDn", "page_down"),
        },
        new[]
        {
            new KeyCap("", null), new KeyCap("↑", "up"), new KeyCap("", null),
        },
        new[]
        {
            new KeyCap("←", "left"), new KeyCap("↓", "down"), new KeyCap("→", "right"),
        },
    };
}
