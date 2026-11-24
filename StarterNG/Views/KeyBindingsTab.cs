using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Classes;
using StarterNG.Controls;
using StarterNG.Infrastructure;

using KeyBinding = StarterNG.Classes.KeyBinding;

namespace StarterNG.Views;

/// <summary>
/// The key bindings of the settings screen: the list of commands, the keyboard they
/// sit on, and the wait for a keypress.
/// </summary>
internal sealed class KeyBindingsTab(Grid host, TextBox search)
{
    /// <summary>Raised on an edit by hand.</summary>
    public event Action? Changed;

    private StackPanel? _bindingListPanel;
    private StackPanel? _keyboardPanel;

    /// <summary>
    /// True while a cab-control binding is waiting for a keypress. The window's own
    /// shortcuts stand down for the duration, otherwise Ctrl+N would never reach here.
    /// </summary>
    public bool IsCapturing => _capturing is not null;

    private KeyBinding? _capturing;
    private Button? _capturingButton;

    // The row buttons of each binding on the list, so focus goes back to the row it
    // left when the list is drawn anew after an edit.
    private readonly Dictionary<KeyBinding, (Button Combo, Button Clear)> _rowButtons = new();

    // The keys of the drawn keyboard that can be bound, in reading order, with the block
    // and the row each lies in: the arrow keys walk them as they lie.
    private readonly List<(Border Cap, int Block, int Row)> _caps = new();

    private readonly Dictionary<string, Grid> _keyCells = new(StringComparer.OrdinalIgnoreCase);

    private bool _flyoutLoading;

    private static readonly IBrush KeyUnassignedBrush = new SolidColorBrush(Color.Parse("#2A3036"));
    private static readonly IBrush KeyFillerBrush = new SolidColorBrush(Color.Parse("#21262B"));
    private static readonly IBrush KeyPlainBrush = new SolidColorBrush(Color.Parse("#2E9E1F"));
    private static readonly IBrush KeyShiftBrush = new SolidColorBrush(Color.Parse("#C9A227"));
    private static readonly IBrush KeyCtrlBrush = new SolidColorBrush(Color.Parse("#2D7FD3"));
    private static readonly IBrush KeyShiftCtrlBrush = new SolidColorBrush(Color.Parse("#C8372D"));
    private static readonly IBrush KeyBorderBrush = new SolidColorBrush(Color.Parse("#3A424A"));
    private static readonly IBrush FgBrush = new SolidColorBrush(Color.Parse("#E6E8EA"));
    private static readonly IBrush FgDimBrush = new SolidColorBrush(Color.Parse("#9098A0"));
    private static readonly IBrush ConflictBrush = new SolidColorBrush(Color.Parse("#E06C5A"));
    private static readonly IBrush KeyFocusBrush = new SolidColorBrush(Color.Parse("#41C400"));

    // The four combinations a key can be on. After the brushes: static fields start in text order.
    private static readonly (bool Shift, bool Ctrl, string SlotTextKey, IBrush Colour)[] ModifierSlots =
    {
        (false, false, "BindNoMod", KeyPlainBrush),
        (true, false, "BindShift", KeyShiftBrush),
        (false, true, "BindCtrl", KeyCtrlBrush),
        (true, true, "BindShiftCtrl", KeyShiftCtrlBrush),
    };

    private const double KeyUnit = 30;
    private const double KeyHeight = 34;
    private const double KeyGap = 4;

    public void Build()
    {
        CancelCapture();
        host.Children.Clear();

        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        var topBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        topBar.Children.Add(new TextBlock
        {
            Text = App.Loc["BindingsHint"],
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FgDimBrush
        });
        var restoreBtn = new Button { Content = App.Loc["RestoreDefaults"] };
        restoreBtn.Classes.Add("Flat");
        restoreBtn.Click += (_, _) =>
        {
            CancelCapture();
            KeyboardConfig.Instance.LoadDefaults();
            BindingsEdited();
        };
        topBar.Children.Add(restoreBtn);
        head.Children.Add(topBar);

        _numpadText = new TextBlock
        {
            Foreground = FgBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var numpadShow = new Button
        {
            Content = App.Loc["BindingsNumpadShow"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        numpadShow.Classes.Add("Flat");
        numpadShow.Click += (_, _) => search.Text = KeyMap.NumpadPrefix;
        Grid.SetColumn(numpadShow, 1);
        _numpadNote = new Border
        {
            BorderBrush = ConflictBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 4),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8,
                Children = { _numpadText, numpadShow }
            }
        };
        head.Children.Add(_numpadNote);
        Grid.SetRow(head, 0);
        host.Children.Add(head);

        _bindingListPanel = new StackPanel { Spacing = 3 };
        var listScroll = new ScrollViewer
        {
            Content = _bindingListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(listScroll, 1);
        host.Children.Add(listScroll);

        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(BuildLegend());
        _keyboardPanel = new StackPanel();
        bottom.Children.Add(new ScrollViewer
        {
            Content = _keyboardPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Grid.SetRow(bottom, 2);
        host.Children.Add(bottom);

        RedrawBindings();
    }

    /// <summary>The bindings as the file has them, and no key waited for.</summary>
    public void Reload()
    {
        CancelCapture();
        KeyboardConfig.Instance.Load();
        RedrawBindings();
    }

    private void BindingsEdited(KeyBinding? focusRow = null)
    {
        KeyboardConfig.Instance.Dirty = true;
        Changed?.Invoke();
        RedrawBindings();
        if (focusRow is not null)
            FocusRow(focusRow);
    }

    /// <summary>
    /// Puts focus back on a binding's row once the list is drawn anew: it went with the
    /// old row, and a keyboard user was left nowhere.
    /// </summary>
    private void FocusRow(KeyBinding binding)
    {
        if (!_rowButtons.TryGetValue(binding, out var row))
            return;

        // The key button, which says what the binding is now.
        row.Combo.Focus(NavigationMethod.Tab);
        row.Combo.BringIntoView();
    }

    private void RedrawBindings()
    {
        RebuildList();
        RebuildKeyboard();
    }

    /// <summary>How many bindings the list shows, after the search.</summary>
    public int ShownCount => _rowButtons.Count;

    public void RebuildList()
    {
        if (_bindingListPanel is null)
            return;

        _bindingListPanel.Children.Clear();
        _rowButtons.Clear();
        // The sidebar search box serves this list too - see Settings.ApplyFilter.
        string filter = search.Text?.Trim() ?? string.Empty;
        var conflicts = KeyboardConfig.Instance.ConflictingCombos();

        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (filter.Length > 0 && !MatchesFilter(b, filter))
                continue;
            _bindingListPanel.Children.Add(BuildBindingRow(b, conflicts));
        }

        UpdateNumpadNote();
    }

    private Border? _numpadNote;
    private TextBlock? _numpadText;

    /// <summary>
    /// Said whenever a binding uses the numpad: neither Windows nor Linux tells whether a
    /// keyboard has one, a keyboard without it reports the same as any other.
    /// </summary>
    private void UpdateNumpadNote()
    {
        if (_numpadNote is null || _numpadText is null)
            return;

        int onNumpad = KeyboardConfig.Instance.BindingsOnNumpad();
        _numpadNote.IsVisible = onNumpad > 0;
        _numpadText.Text = string.Format(App.Loc["BindingsNumpadNote"], onNumpad);
    }

    private static bool MatchesFilter(KeyBinding b, string filter) =>
        TextMatch.Contains(b.Command, filter) ||
        TextMatch.Contains(b.Key, filter) ||
        TextMatch.Contains(KeyboardConfig.Instance.DescriptionOf(b, TranslatedCommand), filter);

    // Polish reads the bundled key file, which has the descriptions with Polish letters.
    private static string TranslatedCommand(string command) =>
        string.Equals(App.Loc.CurrentLangCode, "pl", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : App.Loc.OptOrEnglish("Cmd." + command);

    private Control BuildBindingRow(KeyBinding b, HashSet<string> conflicts)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock
        {
            Text = CommandLabel(b),
            Foreground = FgBrush,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        label.Children.Add(new TextBlock
        {
            Text = b.Command,
            Foreground = FgDimBrush,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var simulatorUse = b.IsAssigned ? KeyMap.SimulatorUse(b.Key, b.Shift, b.Ctrl) : null;
        bool conflict = (b.IsAssigned && conflicts.Contains(b.ComboKey)) || simulatorUse is not null;
        var comboButton = new Button
        {
            Content = _capturing == b ? App.Loc["PressKey"] : ComboText(b),
            MinWidth = 140,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = conflict ? ConflictBrush : FgBrush
        };
        comboButton.Classes.Add("Flat");
        if (simulatorUse is { } use)
            ToolTip.SetTip(comboButton, SimulatorUseText(b.Key, b.Shift, b.Ctrl, use));
        else if (conflict)
            ToolTip.SetTip(comboButton, App.Loc["ConflictTooltip"]);
        NameCombo(comboButton, b, comboButton.Content as string);
        comboButton.Click += (_, _) => StartCapture(b, comboButton);
        Grid.SetColumn(comboButton, 1);
        grid.Children.Add(comboButton);

        var clearButton = new Button
        {
            Content = "✕",
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        clearButton.Classes.Add("Basic");
        ToolTip.SetTip(clearButton, App.Loc["ClearBinding"]);
        // Every row has one: the reader is told whose it is.
        AutomationProperties.SetName(clearButton, ScreenReaderNames.PickerText(App.Loc["ClearBinding"], CommandLabel(b)));
        clearButton.Click += (_, _) =>
        {
            CancelCapture();
            b.Clear();
            BindingsEdited(b);
        };
        _rowButtons[b] = (comboButton, clearButton);
        Grid.SetColumn(clearButton, 2);
        grid.Children.Add(clearButton);

        return new Border
        {
            Background = KeyUnassignedBrush,
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 4),
            Child = grid
        };
    }

    private void StartCapture(KeyBinding b, Button button)
    {

        if (_capturing is not null && _capturingButton is not null)
            Show(_capturingButton, _capturing, ComboText(_capturing));

        _capturing = b;
        _capturingButton = button;
        button.Focus();
        Show(button, b, App.Loc["PressKey"], say: true);
    }

    private void CancelCapture()
    {
        if (_capturing is not null && _capturingButton is not null)
            Show(_capturingButton, _capturing, ComboText(_capturing), say: _capturingButton.IsKeyboardFocusWithin);
        _capturing = null;
        _capturingButton = null;
    }

    /// <summary>
    /// Writes on a binding's button what it shows and names it after its command too, so
    /// a reader hears "Odluźniacz: Num 4" and not a key alone. Said aloud when the change
    /// happens under the focus, where nothing else would tell of it.
    /// </summary>
    private static void Show(Button button, KeyBinding b, string text, bool say = false)
    {
        button.Content = text;
        NameCombo(button, b, text);
        if (say)
            Announcer.Say(button, text);
    }

    private static void NameCombo(Button button, KeyBinding b, string? shown) =>
        AutomationProperties.SetName(button, ScreenReaderNames.PickerText(
            CommandLabel(b), b.IsAssigned || shown != ComboText(b) ? shown : App.Loc["KbUnassigned"]));

    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is null)
            return;

        if (e.Key == Key.Escape)
        {
            CancelCapture();
            e.Handled = true;
            return;
        }

        if (KeyMap.IsModifierKey(e.Key))
            return;

        string? token = KeyMap.FromInput(e.Key, e.PhysicalKey);
        if (token is null)
        {
            e.Handled = true;
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (KeyMap.SimulatorUse(token, shift, ctrl) is { Blocks: true } taken)
        {
            // Bound here it would never fire: the wait for a key goes on.
            if (_capturingButton is not null)
                Show(_capturingButton, _capturing, string.Format(App.Loc["KbTaken"], App.Loc[taken.TextKey]), say: true);
            e.Handled = true;
            return;
        }

        // As the key flyout does: the combination moves here, not doubled up.
        var bound = _capturing;
        KeyboardConfig.Instance.Put(bound, token, shift, ctrl);
        e.Handled = true;

        _capturing = null;
        _capturingButton = null;
        BindingsEdited(bound);
    }

    private void RebuildKeyboard()
    {
        if (_keyboardPanel is null)
            return;

        _keyCells.Clear();
        _caps.Clear();
        _keyboardPanel.Children.Clear();
        // Tab reaches the keyboard once; the arrow keys walk it from there.
        KeyboardNavigation.SetTabNavigation(_keyboardPanel, KeyboardNavigationMode.Once);
        var states = ComputeKeyStates();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row.Children.Add(BuildKeyBlock(KeyMap.MainBlock, states, 0));
        row.Children.Add(BuildKeyBlock(KeyMap.NavBlock, states, 1));
        row.Children.Add(BuildKeyBlock(KeyMap.NumpadBlock, states, 2));
        _keyboardPanel.Children.Add(row);
    }

    private void UpdateKeyboardColors()
    {
        var states = ComputeKeyStates();
        foreach (var (token, content) in _keyCells)
        {
            if (content.Children.Count > 0)
                content.Children.RemoveAt(0);
            content.Children.Insert(0, BuildKeyBackground(token, states));
            states.TryGetValue(token, out var state);
            string tip = BuildKeyTooltip(token, state);
            ToolTip.SetTip(content, tip);
            if (content.Parent is Border cap)
                NameCap(cap, tip);
        }
    }

    private StackPanel BuildKeyBlock(KeyCap[][] rows, Dictionary<string, KeyState> states, int blockIndex)
    {
        var block = new StackPanel { Spacing = KeyGap, VerticalAlignment = VerticalAlignment.Top };
        for (int r = 0; r < rows.Length; r++)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = KeyGap };
            foreach (var cap in rows[r])
                rowPanel.Children.Add(BuildKeyCap(cap, states, blockIndex, r));
            block.Children.Add(rowPanel);
        }
        return block;
    }

    private Border BuildKeyCap(KeyCap cap, Dictionary<string, KeyState> states, int blockIndex, int rowIndex)
    {
        double width = cap.Width * KeyUnit + (cap.Width - 1) * KeyGap;
        var content = new Grid { Width = width, Height = KeyHeight };

        var keyBorder = new Border
        {
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child = content
        };

        if (cap.Token is null)
        {

            content.Children.Add(new Border { Background = KeyFillerBrush });
            content.Children.Add(new TextBlock
            {
                Text = cap.Label,
                Foreground = FgDimBrush,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            keyBorder.Opacity = 0.55;
            if (!string.IsNullOrEmpty(cap.Label))
                ToolTip.SetTip(keyBorder, App.Loc[cap.Label switch
                {
                    "Shift" or "Ctrl" => "KbModifier",
                    "Alt" => "KbAlt",
                    _ => "KbNotInSimulator"
                }]);
            return keyBorder;
        }

        string token = cap.Token;
        states.TryGetValue(token.ToLowerInvariant(), out var st);
        content.Children.Add(BuildKeyBackground(token, states));
        content.Children.Add(new TextBlock
        {
            Text = cap.Label,
            Foreground = FgBrush,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        string tip = BuildKeyTooltip(token, st);
        ToolTip.SetTip(content, tip);

        keyBorder.Cursor = new Cursor(StandardCursorType.Hand);
        keyBorder.PointerPressed += (_, _) => ShowKeyFlyout(token, keyBorder);
        // Reached and opened from the keyboard as well, and read as the button it is.
        keyBorder.Focusable = true;
        keyBorder.Classes.Add("KeyCap");
        AutomationProperties.SetControlTypeOverride(keyBorder, AutomationControlType.Button);
        AutomationProperties.SetIsControlElementOverride(keyBorder, true);
        NameCap(keyBorder, tip);
        keyBorder.KeyDown += (_, e) => OnCapKey(keyBorder, token, e);
        // The ring the theme gives focus, drawn here: the key clips anything outside it.
        keyBorder.GotFocus += (_, _) => keyBorder.BorderBrush = KeyFocusBrush;
        keyBorder.LostFocus += (_, _) => keyBorder.BorderBrush = KeyBorderBrush;
        _caps.Add((keyBorder, blockIndex, rowIndex));
        _keyCells[token] = content;
        return keyBorder;
    }

    // The tooltip says the key and what is on it, one thing a line; read as one sentence.
    private static void NameCap(Border cap, string tip) =>
        AutomationProperties.SetName(cap, tip.Replace("\n", ", "));

    private void OnCapKey(Border cap, string token, KeyEventArgs e)
    {
        int at = _caps.FindIndex(c => ReferenceEquals(c.Cap, cap));
        if (at < 0)
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            ShowKeyFlyout(token, cap);
            e.Handled = true;
            return;
        }

        Border? next;
        switch (e.Key)
        {
            case Key.Left: next = at > 0 ? _caps[at - 1].Cap : null; break;
            case Key.Right: next = at < _caps.Count - 1 ? _caps[at + 1].Cap : null; break;
            case Key.Home: next = _caps[0].Cap; break;
            case Key.End: next = _caps[^1].Cap; break;
            case Key.Up: next = CapNextRow(at, -1); break;
            case Key.Down: next = CapNextRow(at, 1); break;
            default: return;
        }

        next?.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    /// <summary>The key in the next row up or down of the same block that lies nearest over or under this one.</summary>
    private Border? CapNextRow(int at, int step)
    {
        var (cap, block, row) = _caps[at];
        if (_keyboardPanel is null || cap.TranslatePoint(new Point(cap.Bounds.Width / 2, 0), _keyboardPanel) is not { } from)
            return null;

        Border? best = null;
        double nearest = double.MaxValue;
        foreach (var (other, otherBlock, otherRow) in _caps)
        {
            if (otherBlock != block || otherRow != row + step
                || other.TranslatePoint(new Point(other.Bounds.Width / 2, 0), _keyboardPanel) is not { } to)
                continue;

            double distance = Math.Abs(to.X - from.X);
            if (distance < nearest)
            {
                nearest = distance;
                best = other;
            }
        }
        return best;
    }

    private Control BuildKeyBackground(string token, Dictionary<string, KeyState> states)
    {
        states.TryGetValue(token.ToLowerInvariant(), out var state);
        var colours = new List<IBrush>();
        for (int i = 0; i < ModifierSlots.Length; i++)
            if (state is not null && state.SlotOn[i])
                colours.Add(ModifierSlots[i].Colour);

        if (colours.Count == 0)
            return new Border { Background = KeyUnassignedBrush };

        var stripes = new UniformGrid { Rows = 1, Columns = colours.Count };
        foreach (var c in colours)
            stripes.Children.Add(new Border { Background = c });
        return stripes;
    }

    private string BuildKeyTooltip(string token, KeyState? state)
    {
        string head = KeyMap.DisplayName(token);
        var lines = new List<string>();
        if (state is null || state.Tips.Count == 0)
        {
            lines.Add($"{head}: {App.Loc["KbUnassigned"]}");
        }
        else
        {
            lines.Add(head);
            lines.AddRange(state.Tips);
        }
        lines.AddRange(SimulatorNotes(token));
        return string.Join("\n", lines);
    }

    private static string SimulatorUseText(string token, bool shift, bool ctrl, SimulatorKeyUse use) =>
        string.Format(App.Loc[use.Blocks ? "KbSimBlocks" : "KbSimReplaces"],
                      KeyMap.ComboText(token, shift, ctrl),
                      App.Loc[use.TextKey]);

    /// <summary>What the simulator does with this key by itself, once per thing it does.</summary>
    private static List<string> SimulatorNotes(string token)
    {
        var notes = new List<string>();
        var seen = new HashSet<SimulatorKeyUse>();
        foreach (var (shift, ctrl, _, _) in ModifierSlots)
            if (KeyMap.SimulatorUse(token, shift, ctrl) is { } use && seen.Add(use))
                notes.Add(SimulatorUseText(token, shift, ctrl, use));
        return notes;
    }

    private void ShowKeyFlyout(string token, Control anchor)
    {
        CancelCapture();
        var commands = KeyboardConfig.Instance.Bindings;

        var items = new List<string> { App.Loc["BindNone"] };
        foreach (var c in commands)
            items.Add(CommandLabel(c));

        var combos = new ComboBox[ModifierSlots.Length];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };

        for (int i = 0; i < ModifierSlots.Length; i++)
        {
            var (shift, ctrl, labelKey, colour) = ModifierSlots[i];

            // The swatch ties the row to the key colours on the keyboard above.
            var lbl = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 12, 4),
                Children =
                {
                    new Border
                    {
                        Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                        Background = colour, VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock { Text = App.Loc[labelKey], Foreground = FgBrush, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            Grid.SetRow(lbl, i);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var cb = new ComboBox
            {
                ItemsSource = items,
                MinWidth = 280,
                MaxDropDownHeight = 320,
                Margin = new Thickness(0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            // Its label is a swatch and a text in a panel, which a reader does not take for one.
            AutomationProperties.SetName(cb, KeyMap.ComboText(token, shift, ctrl));
            if (KeyMap.SimulatorUse(token, shift, ctrl) is { } use)
            {
                ToolTip.SetTip(cb, SimulatorUseText(token, shift, ctrl, use));
                // What a hand-edited file put there can still be taken off.
                cb.IsEnabled = !use.Blocks || KeyboardConfig.Instance.BindingAt(token, shift, ctrl) is not null;
            }
            cb.SelectionChanged += (_, _) =>
            {
                if (_flyoutLoading)
                    return;
                AssignSlot(token, shift, ctrl, cb.SelectedIndex, commands);
                SyncFlyoutCombos(token, combos);
                UpdateKeyboardColors();
                RebuildList();
            };
            Grid.SetRow(cb, i);
            Grid.SetColumn(cb, 1);
            grid.Children.Add(cb);
            combos[i] = cb;
        }

        SyncFlyoutCombos(token, combos);

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12), MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{App.Loc["BindKeyTitle"]} {KeyMap.DisplayName(token)}",
            FontWeight = FontWeight.SemiBold,
            Foreground = FgBrush
        });
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["BindKeyHint"],
            Foreground = FgDimBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(grid);
        foreach (string note in SimulatorNotes(token))
            panel.Children.Add(new TextBlock
            {
                Text = note,
                Foreground = ConflictBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            });

        var flyout = new Flyout { Content = panel };
        flyout.ShowAt(anchor);
    }

    private void SyncFlyoutCombos(string token, ComboBox[] combos)
    {
        var commands = KeyboardConfig.Instance.Bindings;
        _flyoutLoading = true;
        for (int i = 0; i < combos.Length; i++)
        {
            var cmd = KeyboardConfig.Instance.BindingAt(token, ModifierSlots[i].Shift, ModifierSlots[i].Ctrl);
            combos[i].SelectedIndex = cmd is null ? 0 : commands.IndexOf(cmd) + 1;
        }
        _flyoutLoading = false;
    }

    private void AssignSlot(string token, bool shift, bool ctrl, int selectedIndex, List<KeyBinding> commands)
    {
        KeyBinding? chosen = selectedIndex <= 0 ? null : commands[selectedIndex - 1];
        KeyboardConfig.Instance.Put(chosen, token, shift, ctrl);
        KeyboardConfig.Instance.Dirty = true;
        Changed?.Invoke();
    }

    private Control BuildLegend()
    {
        var legend = new WrapPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(LegendItem(KeyUnassignedBrush, App.Loc["KbUnassigned"]));
        legend.Children.Add(LegendItem(KeyPlainBrush, App.Loc["KbAssigned"]));
        legend.Children.Add(LegendItem(KeyShiftBrush, App.Loc["KbShift"]));
        legend.Children.Add(LegendItem(KeyCtrlBrush, App.Loc["KbCtrl"]));
        legend.Children.Add(LegendItem(KeyShiftCtrlBrush, App.Loc["KbShiftCtrl"]));
        return legend;
    }

    private Control LegendItem(IBrush brush, string text)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            Background = brush,
            BorderBrush = KeyBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock { Text = text, Foreground = FgDimBrush, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    private sealed class KeyState
    {
        /// <summary>Which of the <see cref="ModifierSlots"/> have a binding.</summary>
        public readonly bool[] SlotOn = new bool[ModifierSlots.Length];
        public readonly List<string> Tips = new();
    }

    private static Dictionary<string, KeyState> ComputeKeyStates()
    {
        var map = new Dictionary<string, KeyState>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in KeyboardConfig.Instance.Bindings)
        {
            if (!b.IsAssigned)
                continue;
            string key = b.Key.ToLowerInvariant();
            if (!map.TryGetValue(key, out var state))
                map[key] = state = new KeyState();

            state.SlotOn[Array.FindIndex(ModifierSlots, slot => slot.Shift == b.Shift && slot.Ctrl == b.Ctrl)] = true;

            state.Tips.Add($"{ComboText(b)} — {CommandLabel(b)}");
        }
        return map;
    }

    private static string ComboText(KeyBinding b) => KeyMap.ComboText(b.Key, b.Shift, b.Ctrl);

    private static string CommandLabel(KeyBinding b) =>
        KeyboardConfig.Instance.DescriptionOf(b, TranslatedCommand) is { Length: > 0 } description
            ? Capitalize(description)
            : b.Command;

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
