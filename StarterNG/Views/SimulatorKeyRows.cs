using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using StarterNG.Controls;
using StarterNG.Domain.Settings;

namespace StarterNG.Views;

/// <summary>
/// The rows of the <see cref="SimulatorKeys"/>, one section per group, added to
/// the end of their settings category. A row writes its key only once the user
/// moves it away from what it showed, so a value the control cannot show - a draw
/// range above the slider's end - survives a save untouched.
/// </summary>
internal sealed class SimulatorKeyRows
{
    private sealed record Row(SimulatorKey Key, SettingRow Setting, Control Input)
    {
        public string Shown = string.Empty;
    }

    private readonly List<Row> _rows = new();

    private readonly List<(SettingsSection Section, string HeaderKey)> _sections = new();

    public event Action? Changed;

    /// <summary>Raised on every move of a row, the starter's own setting of it included.</summary>
    public event Action<SimulatorKey>? Moved;

    /// <summary>
    /// Puts each row where the layout has a <see cref="KeySlot"/> for it; the rest
    /// go to sections of their own at the end of their category.
    /// </summary>
    public void Build(Panel categories)
    {
        var slots = categories.GetLogicalDescendants().OfType<KeySlot>()
            .Where(slot => slot.Key is not null)
            .ToDictionary(slot => slot.Key!, StringComparer.OrdinalIgnoreCase);

        foreach (var key in SimulatorKeys.All)
            if (slots.TryGetValue(key.Key, out var slot))
                slot.Child = BuildRow(key);

        foreach (var group in SimulatorKeys.All.Where(k => !slots.ContainsKey(k.Key))
                                               .GroupBy(k => (k.Category, k.Section)))
        {
            var category = categories.Children.OfType<StackPanel>()
                .FirstOrDefault(p => string.Equals(p.Tag as string, group.Key.Category, StringComparison.Ordinal));
            if (category is null)
                continue;

            var rows = new StackPanel();
            foreach (var key in group)
                rows.Children.Add(BuildRow(key));

            var section = new SettingsSection { Content = rows };
            _sections.Add((section, group.Key.Section));
            category.Children.Add(section);
        }

        UpdateTexts();
    }

    private SettingRow BuildRow(SimulatorKey key)
    {
        Control input = key.Kind switch
        {
            SimulatorKeyKind.Toggle => new CheckBox(),
            SimulatorKeyKind.Number => new Slider
            {
                Minimum = key.Minimum,
                Maximum = key.Maximum,
                TickFrequency = key.Step,
                IsSnapToTickEnabled = true
            },
            SimulatorKeyKind.Choice => ChoiceBox(key),
            _ => new TextBox { MinWidth = 200 }
        };

        // Marks the input as one of these rows, which tell about their own changes.
        input.Tag = key;

        var setting = new SettingRow { ConfigKey = key.Key, Content = input };
        var row = new Row(key, setting, input);
        _rows.Add(row);

        switch (input)
        {
            case CheckBox box:
                box.IsCheckedChanged += (_, _) => OnEdited(row);
                break;
            case Slider slider:
                slider.ValueChanged += (_, _) =>
                {
                    UpdateValueText(row);
                    OnEdited(row);
                };
                break;
            case TextBox text:
                text.TextChanged += (_, _) => OnEdited(row);
                break;
            case ComboBox box:
                box.SelectionChanged += (_, _) => OnEdited(row);
                break;
        }

        return setting;
    }

    private static ComboBox ChoiceBox(SimulatorKey key)
    {
        var box = new ComboBox();
        box.Classes.Add("Localized");
        foreach (string word in key.Words ?? Array.Empty<string>())
            box.Items.Add(new ComboBoxItem { Tag = word });
        return box;
    }

    /// <summary>
    /// Only a value other than the one shown counts: a text box reports the text it
    /// was given after the fact, and a slider reports its own clamping.
    /// </summary>
    private void OnEdited(Row row)
    {
        Moved?.Invoke(row.Key);
        if (Current(row) != row.Shown)
            Changed?.Invoke();
    }

    /// <summary>The row of <paramref name="key"/>, wherever it was put.</summary>
    public SettingRow? RowOf(string key) => Find(key)?.Setting;

    private Row? Find(string key) =>
        _rows.FirstOrDefault(row => string.Equals(row.Key.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The number a row holds once it says something: moved by the user, or read
    /// from a file that has the key. Null while it only shows the default, which
    /// the simulator may not share.
    /// </summary>
    public double? NumberIfSet(string key, SimulatorSettings settings)
    {
        var row = Find(key);
        if (row is null || row.Input is not Slider slider)
            return null;
        if (Current(row) == row.Shown && !settings.Extra.ContainsKey(row.Key.Key))
            return null;
        return slider.Value;
    }

    /// <summary>Labels in the current language; called again when it changes.</summary>
    public void UpdateTexts()
    {
        foreach (var (section, headerKey) in _sections)
            section.Header = App.Loc[headerKey];

        foreach (var row in _rows)
        {
            string label = App.Loc[row.Key.LabelKey];
            row.Setting.Description = App.Loc[row.Key.DescriptionKey];

            switch (row.Input)
            {
                case CheckBox box:
                    box.Content = label;
                    break;
                case TextBox text:
                    row.Setting.Label = label;
                    text.PlaceholderText = App.Loc["IniDefault"];
                    break;
                case ComboBox choice:
                    row.Setting.Label = label;
                    foreach (var item in choice.Items.OfType<ComboBoxItem>())
                        item.Content = App.Loc[SimulatorKey.WordKey(item.Tag as string ?? string.Empty)];
                    break;
                default:
                    row.Setting.Label = label;
                    UpdateValueText(row);
                    break;
            }
        }
    }

    public void Apply(SimulatorSettings settings)
    {
        foreach (var row in _rows)
        {
            Put(row, row.Key.RawIn(settings));
            row.Shown = Current(row);
        }
    }

    /// <param name="remember">
    /// Whether what is handed over counts as shown from now on: for the settings a
    /// save writes, not for a copy to try something on.
    /// </param>
    public void Capture(SimulatorSettings settings, bool remember)
    {
        foreach (var row in _rows)
        {
            string now = Current(row);
            if (now == row.Shown)
                continue;

            settings.Extra[row.Key.Key] = now;
            if (remember)
                row.Shown = now;
        }
    }

    /// <summary>
    /// Puts <paramref name="raw"/> in the row of <paramref name="key"/> as an edit
    /// by hand would: what it shows stays what it showed, so a save writes it.
    /// </summary>
    public void Show(string key, string raw)
    {
        if (Find(key) is { } row)
            Put(row, raw);
    }

    /// <summary>Puts the simulator's own value in the row of <paramref name="key"/>, as <see cref="Show"/> does.</summary>
    public void ShowDefault(string key)
    {
        if (Find(key) is { } row)
            Put(row, row.Key.Default);
    }

    private static void Put(Row row, string raw)
    {
        switch (row.Input)
        {
            case CheckBox box:
                box.IsChecked = SimulatorKey.ParseToggle(raw);
                break;
            case Slider slider:
                slider.Value = Math.Clamp(row.Key.ParseNumber(raw), row.Key.Minimum, row.Key.Maximum);
                break;
            case TextBox text:
                text.Text = raw.Trim();
                break;
            case ComboBox choice:
                string word = row.Key.ParseChoice(raw);
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == word);
                break;
        }
    }

    private static string Current(Row row) => row.Input switch
    {
        CheckBox box => SimulatorKey.FormatToggle(box.IsChecked == true),
        Slider slider => row.Key.FormatNumber(slider.Value),
        TextBox text => (text.Text ?? string.Empty).Trim(),
        ComboBox choice => (choice.SelectedItem as ComboBoxItem)?.Tag as string ?? row.Key.Default,
        _ => string.Empty
    };

    private static void UpdateValueText(Row row)
    {
        if (row.Input is not Slider slider)
            return;

        double value = slider.Value;
        row.Setting.ValueText = value == 0 && row.Key.ZeroIsOff
            ? App.Loc["RangeDisabled"]
            : value.ToString(NumberFormat(row.Key.Step), CultureInfo.CurrentCulture) + row.Key.Unit;
    }

    private static string NumberFormat(double step) =>
        step >= 1 ? "0" : step >= 0.1 ? "0.0" : step >= 0.01 ? "0.00" : "0.000";
}
