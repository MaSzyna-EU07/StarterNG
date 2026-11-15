using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;

namespace StarterNG.Controls;

/// <summary>
/// The names a screen reader says for controls that show no plain text of their own.
/// Without one it said the type of whatever was inside: an icon button was read as
/// "Material.Icons.Avalonia.MaterialIcon", a scenery in the tree as nothing at all.
/// An icon button is named by its tooltip, a list or tree entry by the text it shows,
/// a field by the hint written in it. A name given on purpose is left alone.
/// </summary>
public static class ScreenReaderNames
{
    // What was given here last, so a later change replaces it but nothing else.
    private static readonly ConditionalWeakTable<StyledElement, string> Given = new();

    public static void Install()
    {
        ContentControl.ContentProperty.Changed.AddClassHandler<Button>((b, _) => NameButton(b));
        ToolTip.TipProperty.Changed.AddClassHandler<Button>((b, _) => NameButton(b));

        ContentControl.ContentProperty.Changed.AddClassHandler<ListBoxItem>((i, _) => Give(i, TextOf(i.Content)));
        HeaderedItemsControl.HeaderProperty.Changed.AddClassHandler<TreeViewItem>((i, _) => Give(i, TextOf(i.Header)));

        HeaderedContentControl.HeaderProperty.Changed.AddClassHandler<HeaderedContentControl>((c, _) => NameGroup(c));

        ComboBox.PlaceholderTextProperty.Changed.AddClassHandler<ComboBox>((c, _) => Give(c, c.PlaceholderText));
        TextBox.PlaceholderTextProperty.Changed.AddClassHandler<TextBox>((t, _) => Give(t, t.PlaceholderText));

        Control.LoadedEvent.AddClassHandler<ComboBox>((c, _) => NameByLabel(c));
        Control.LoadedEvent.AddClassHandler<TextBox>((c, _) => NameByLabel(c));
        Control.LoadedEvent.AddClassHandler<Slider>((c, _) => NameByLabel(c));
        Control.LoadedEvent.AddClassHandler<NumericUpDown>((c, _) => NameByLabel(c));
        Control.LoadedEvent.AddClassHandler<DatePicker>((c, _) => NameByLabel(c));
        Control.LoadedEvent.AddClassHandler<TimePicker>((c, _) => NameByLabel(c));

        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DatePicker>((p, e) => KeepPickerButton(p, e));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TimePicker>((p, e) => KeepPickerButton(p, e));
        DatePicker.SelectedDateProperty.Changed.AddClassHandler<DatePicker>((p, _) => NamePicker(p));
        TimePicker.SelectedTimeProperty.Changed.AddClassHandler<TimePicker>((p, _) => NamePicker(p));
        AutomationProperties.NameProperty.Changed.AddClassHandler<DatePicker>((p, _) => NamePicker(p));
        AutomationProperties.NameProperty.Changed.AddClassHandler<TimePicker>((p, _) => NamePicker(p));
    }

    // A date or time picker is reached through the button inside it, which was read as
    // "Avalonia.Controls.Grid": its texts are filled in only after the name is looked for.
    private static readonly ConditionalWeakTable<TemplatedControl, Button> PickerButtons = new();

    private static void KeepPickerButton(TemplatedControl picker, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<Button>("PART_FlyoutButton") is not { } button)
            return;

        PickerButtons.AddOrUpdate(picker, button);
        NamePicker(picker);
    }

    private static void NamePicker(TemplatedControl picker)
    {
        if (!PickerButtons.TryGetValue(picker, out var button))
            return;

        string? value = picker switch
        {
            DatePicker date => DateText(date.SelectedDate, date.YearVisible),
            TimePicker time => TimeText(time.SelectedTime),
            _ => null
        };
        AutomationProperties.SetName(button, PickerText(AutomationProperties.GetName(picker), value));
    }

    /// <summary>The picker's label and what it holds, as the button inside it is read.</summary>
    public static string? PickerText(string? label, string? value) =>
        string.IsNullOrEmpty(label) ? value
        : string.IsNullOrEmpty(value) ? label
        : $"{label}: {value}";

    /// <summary>Day and month in words, the year too when the picker shows one.</summary>
    public static string? DateText(DateTimeOffset? date, bool withYear) =>
        date?.ToString(withYear ? "D" : "M", CultureInfo.CurrentCulture);

    public static string? TimeText(TimeSpan? time) =>
        time?.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    // The labels already followed, so a label read again is not followed twice.
    private static readonly ConditionalWeakTable<TextBlock, Control> Followed = new();

    /// <summary>Longer than this, a text beside a field is a description, not its label.</summary>
    private const int LabelLength = 60;

    /// <summary>
    /// A field named by the label a sighted reader sees beside it, and named again when
    /// the label changes with the language. A name given otherwise is left alone.
    /// </summary>
    public static void NameByLabel(Control input)
    {
        if (LabelOf(input) is not { } label)
            return;

        Give(input, TextOf(label));
        if (Followed.TryAdd(label, input))
            label.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBlock.TextProperty)
                    Give(input, TextOf(label));
            };
    }

    /// <summary>
    /// The text that labels a field: to its left in the same grid row, above it in the
    /// same grid column, or just before it in any other panel. Null when there is none.
    /// </summary>
    public static TextBlock? LabelOf(Control input)
    {
        if (input.Parent is not Panel panel)
            return null;

        var labels = panel.Children.OfType<TextBlock>().Where(IsLabel).ToList();
        if (panel is Grid)
        {
            int row = Grid.GetRow(input), column = Grid.GetColumn(input);
            return labels.Where(t => Grid.GetRow(t) == row && Grid.GetColumn(t) < column)
                       .OrderByDescending(Grid.GetColumn).FirstOrDefault()
                   ?? labels.FirstOrDefault(t => Grid.GetColumn(t) == column && Grid.GetRow(t) == row - 1);
        }

        int at = panel.Children.IndexOf(input);
        return at > 0 && panel.Children[at - 1] is TextBlock before && labels.Contains(before) ? before : null;
    }

    private static bool IsLabel(TextBlock block) =>
        block.IsVisible && TextOf(block) is { Length: <= LabelLength };

    /// <summary>
    /// A panel with a title, such as Scenerie or Skład, made a named group: the reader
    /// says where focus went in, where before the panel was not there for it at all.
    /// Tabs and expanders, which are panels with a title too, speak for themselves.
    /// </summary>
    private static void NameGroup(HeaderedContentControl panel)
    {
        if (panel.GetType() != typeof(HeaderedContentControl) && panel is not SettingsSection)
            return;

        string? name = TextOf(panel.Header);
        Give(panel, name);
        AutomationProperties.SetControlTypeOverride(panel, name is null ? null : AutomationControlType.Group);
        // Content as well as control: NVDA passes over a group that is not both.
        AutomationProperties.SetAccessibilityView(panel, name is null ? AccessibilityView.Default : AccessibilityView.Content);
    }

    private static void NameButton(Button button) =>
        Give(button, button.Content is string ? null : TextOf(ToolTip.GetTip(button)) ?? TextOf(button.Content));

    // The descriptions given here, so a later one replaces it but nothing else.
    private static readonly ConditionalWeakTable<StyledElement, string> Described = new();

    /// <summary>
    /// The inputs of a setting row, described by the row's explanation and the note on
    /// why the simulator would pass it over: a reader hears them after the name, where
    /// a sighted one sees them under it.
    /// </summary>
    public static void DescribeInputs(object? content, params string?[] notes)
    {
        string? text = Join(notes) is { Length: > 0 } joined ? joined : null;
        foreach (var input in DescribedInputs(content))
            Describe(input, text);
    }

    private static void Describe(StyledElement target, string? text)
    {
        string? current = AutomationProperties.GetHelpText(target);
        bool ours = Described.TryGetValue(target, out string? given) && current == given;
        if (!string.IsNullOrEmpty(current) && !ours)
            return;

        if (string.IsNullOrEmpty(text))
        {
            if (ours)
                target.ClearValue(AutomationProperties.HelpTextProperty);
            Described.Remove(target);
            return;
        }

        AutomationProperties.SetHelpText(target, text);
        Described.AddOrUpdate(target, text);
    }

    /// <summary>
    /// The inputs of a setting row, named by the row's label: a list, a slider or a box
    /// beside the label otherwise had no name at all.
    /// </summary>
    public static void NameInputs(object? content, string? label)
    {
        foreach (var input in Inputs(content))
            Give(input, label);
    }

    /// <summary>
    /// The value a setting row shows, given to the slider in it, so the reader hears
    /// "100%" or "Wyłączone" and not where the knob stands.
    /// </summary>
    public static void TellValue(object? content, string? valueText)
    {
        foreach (var slider in Inputs(content).OfType<ReadableSlider>())
            slider.ValueText = valueText;
    }

    private static IEnumerable<Control> Inputs(object? content) => content switch
    {
        ComboBox or Slider or TextBox or NumericUpDown => new[] { (Control)content },
        Panel panel => panel.Children.SelectMany(Inputs),
        Decorator decorator => Inputs(decorator.Child),
        _ => Array.Empty<Control>()
    };

    // A check box says its own text, so it is described but never named by the row.
    private static IEnumerable<Control> DescribedInputs(object? content) => content switch
    {
        CheckBox box => new[] { (Control)box },
        Panel panel => panel.Children.SelectMany(DescribedInputs),
        Decorator decorator => DescribedInputs(decorator.Child),
        _ => Inputs(content)
    };

    /// <summary>The text a reader would see on it, or null when it shows none.</summary>
    public static string? TextOf(object? content)
    {
        string? text = content switch
        {
            string s => s,
            TextBlock block => block.Inlines is { Count: > 0 } inlines ? TextOf(inlines) : block.Text,
            ContentControl inner => TextOf(inner.Content),
            Decorator decorator => TextOf(decorator.Child),
            Panel panel => Join(panel.Children.Where(c => c.IsVisible).Select(TextOf)),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string TextOf(InlineCollection inlines) =>
        string.Concat(inlines.Select(i => i switch
        {
            Run run => run.Text,
            LineBreak => " ",
            Span span => TextOf(span.Inlines),
            _ => null
        }));

    private static string Join(IEnumerable<string?> parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static void Give(StyledElement target, string? name)
    {
        string? current = AutomationProperties.GetName(target);
        bool ours = Given.TryGetValue(target, out string? given) && current == given;
        if (!string.IsNullOrEmpty(current) && !ours)
            return;

        if (string.IsNullOrEmpty(name))
        {
            if (ours)
                target.ClearValue(AutomationProperties.NameProperty);
            Given.Remove(target);
            return;
        }

        AutomationProperties.SetName(target, name);
        Given.AddOrUpdate(target, name);
    }
}
