using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Eu07.BlackBox.Views;

/// <summary>
/// The pieces the window is built of, in one plain look: flat panels side by side, flat
/// buttons, a table of figures and tabs underlined.
/// </summary>
internal static class UiKit
{
    /// <summary>The gutter between the panels.</summary>
    public const double Gap = 6;
    public static readonly CornerRadius Small = new(2);

    /// <summary>
    /// The theme's accent (the slider, the picked lines, the focus) in the orange of the viewer
    /// instead of the system colour, for the window given and everything in it.
    /// </summary>
    public static void Accent(Control control)
    {
        // As the starter's App.axaml sets them, for the viewer on its own.
        (string Key, string Colour)[] accents =
        {
            ("SystemAccentColor", "#177f00"), ("SystemAccentColorDark1", "#136B00"), ("SystemAccentColorDark2", "#0F5600"),
            ("SystemAccentColorDark3", "#0B4200"), ("SystemAccentColorLight1", "#2A9A0F"), ("SystemAccentColorLight2", "#41C400"),
            ("SystemAccentColorLight3", "#5FD42A")
        };
        foreach (var (key, colour) in accents)
            control.Resources[key] = Color.Parse(colour);
        control.Resources["AccentFillColorDefaultBrush"] = Palette.AccentFill;
        control.Resources["AccentFillColorSecondaryBrush"] = Palette.AccentFillHover;
        control.Resources["SystemControlHighlightListAccentLowBrush"] = Palette.AccentFill;
        control.Resources["SystemControlHighlightListAccentMediumBrush"] = Palette.AccentFillHover;
        control.Resources["ListBoxItemBackgroundSelected"] = Palette.AccentFill;
        control.Resources["ListBoxItemBackgroundSelectedPointerOver"] = Palette.AccentFillHover;
        control.Resources["ListBoxItemBackgroundSelectedPressed"] = Palette.AccentFill;
        control.Resources["ListBoxItemBackgroundPointerOver"] = Palette.CardHover;
    }

    /// <summary>A panel with its title on a bar, as the group boxes of the starter; what goes right of the title, if anything.</summary>
    public static Border Group(string title, Control content, Control? tag = null)
    {
        var bar = new DockPanel();
        if (tag is not null)
        {
            DockPanel.SetDock(tag, Dock.Right);
            tag.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(tag);
        }
        bar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        var header = new Border
        {
            Background = Palette.Header,
            BorderBrush = Palette.CardBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 3),
            MinHeight = 24,
            Child = bar
        };
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(content);
        return Frame(panel);
    }

    /// <summary>A panel in the frame of the starter's group boxes, without a title.</summary>
    public static Border Frame(Control content) => new()
    {
        Background = Palette.Panel,
        BorderBrush = Palette.CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = Small,
        ClipToBounds = true,
        Child = content
    };

    /// <summary>A flat panel the views sit on.</summary>
    public static Border Card(Control content, Thickness? padding = null) => new()
    {
        Background = Palette.Card,
        Padding = padding ?? new Thickness(0),
        ClipToBounds = true,
        Child = content
    };

    /// <summary>A button of the toolbar: text on the bar, a lighter ground under the pointer.</summary>
    public static Button Button(string text, string? tip = null)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 2),
            MinHeight = 25,
            CornerRadius = Small,
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (tip is not null)
            ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>The button of playing.</summary>
    public static Button PlayButton()
    {
        var button = new Button
        {
            Width = 32,
            Height = 25,
            CornerRadius = Small,
            Background = Palette.AccentFill,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        // As the starter's start button: its green, brighter under the pointer.
        Hover(button, Palette.Accent, Brushes.White);
        return button;
    }

    /// <summary>The glyph of play or pause.</summary>
    public static Control PlayGlyph(bool playing)
    {
        var geometry = playing
            ? Geometry.Parse("M0,0 H4 V12 H0 Z M7,0 H11 V12 H7 Z")
            : Geometry.Parse("M1,0 L11,6 L1,12 Z");
        return new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Fill = Brushes.White,
            Width = 12,
            Height = 12,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>A toggle of the toolbar: on, its text takes the accent.</summary>
    public static ToggleButton Toggle(string text, string? tip = null)
    {
        var toggle = new ToggleButton
        {
            Content = text,
            Padding = new Thickness(10, 2),
            MinHeight = 25,
            CornerRadius = Small,
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            toggle.Resources["ToggleButtonBackgroundChecked" + state] = state == "PointerOver" ? Palette.AccentFillHover : Palette.AccentFill;
            toggle.Resources["ToggleButtonForegroundChecked" + state] = Brushes.White;
        }
        if (tip is not null)
            ToolTip.SetTip(toggle, tip);
        return toggle;
    }

    // The theme takes its own colours under the pointer and when pressed, unless the control gives them.
    private static void Hover(Control control, IBrush background, IBrush foreground)
    {
        foreach (var kind in new[] { "Button", "ToggleButton" })
        {
            control.Resources[kind + "BackgroundPointerOver"] = background;
            control.Resources[kind + "BackgroundPressed"] = background;
            control.Resources[kind + "ForegroundPointerOver"] = foreground;
            control.Resources[kind + "ForegroundPressed"] = foreground;
            control.Resources[kind + "BorderBrushPointerOver"] = Brushes.Transparent;
            control.Resources[kind + "BorderBrushPressed"] = Brushes.Transparent;
        }
    }

    /// <summary>A line of the table of figures: what it is on the left, its value on the right, a note under it.</summary>
    public sealed class Tile : Border
    {
        private readonly TextBlock _value = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.FaintText, TextTrimming = TextTrimming.CharacterEllipsis };

        public Tile(string caption)
        {
            BorderBrush = Palette.Grid;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Padding = new Thickness(8, 4);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            var name = new TextBlock { Text = caption, FontSize = 12, Foreground = Palette.DimText, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_value, 1);
            Grid.SetRow(_sub, 1);
            Grid.SetColumnSpan(_sub, 2);
            grid.Children.Add(name);
            grid.Children.Add(_value);
            grid.Children.Add(_sub);
            Child = grid;
        }

        public void Show(string value, string? sub = null, IBrush? brush = null)
        {
            _value.Text = value;
            _value.Foreground = brush ?? Palette.Text;
            _sub.Text = sub ?? "";
            _sub.IsVisible = !string.IsNullOrEmpty(sub);
        }
    }

    /// <summary>Tabs side by side as the starter's: the one on on the panel's ground, with a bar of the accent on top.</summary>
    public sealed class Segmented : Border
    {
        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
        private int _selected = -1;

        public event Action<int>? SelectedChanged;

        public Segmented(params string[] choices)
        {
            Background = Palette.Background;
            BorderBrush = Palette.CardBorder;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Child = _row;
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = choices[i],
                    Padding = new Thickness(14, 3),
                    MinHeight = 24,
                    CornerRadius = new CornerRadius(0),
                    FontSize = 12,
                    BorderThickness = new Thickness(0, 2, 0, 0)
                };
                button.Resources["ButtonBackgroundPointerOver"] = Palette.CardHover;
                button.Resources["ButtonBackgroundPressed"] = Palette.CardHover;
                button.Resources["ButtonForegroundPointerOver"] = Palette.Text;
                button.Resources["ButtonForegroundPressed"] = Palette.Text;
                button.Click += (_, _) => Selected = index;
                _row.Children.Add(button);
            }
            Selected = 0;
        }

        public int Selected
        {
            get => _selected;
            set
            {
                if (value == _selected)
                    return;
                _selected = value;
                for (int i = 0; i < _row.Children.Count; i++)
                {
                    var button = (Button)_row.Children[i];
                    bool on = i == value;
                    var line = on ? Palette.Accent : Brushes.Transparent;
                    button.Background = on ? Palette.Panel : Brushes.Transparent;
                    button.BorderBrush = line;
                    button.Resources["ButtonBorderBrushPointerOver"] = line;
                    button.Resources["ButtonBorderBrushPressed"] = line;
                    button.Foreground = on ? Palette.Text : Palette.DimText;
                }
                SelectedChanged?.Invoke(value);
            }
        }
    }
}
