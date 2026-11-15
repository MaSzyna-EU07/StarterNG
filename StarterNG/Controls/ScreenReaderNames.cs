using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
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

        ComboBox.PlaceholderTextProperty.Changed.AddClassHandler<ComboBox>((c, _) => Give(c, c.PlaceholderText));
        TextBox.PlaceholderTextProperty.Changed.AddClassHandler<TextBox>((t, _) => Give(t, t.PlaceholderText));
    }

    private static void NameButton(Button button) =>
        Give(button, button.Content is string ? null : TextOf(ToolTip.GetTip(button)) ?? TextOf(button.Content));

    /// <summary>
    /// The inputs of a setting row, named by the row's label: a list, a slider or a box
    /// beside the label otherwise had no name at all.
    /// </summary>
    public static void NameInputs(object? content, string? label)
    {
        foreach (var input in Inputs(content))
            Give(input, label);
    }

    private static IEnumerable<Control> Inputs(object? content) => content switch
    {
        ComboBox or Slider or TextBox or NumericUpDown => new[] { (Control)content },
        Panel panel => panel.Children.SelectMany(Inputs),
        Decorator decorator => Inputs(decorator.Child),
        _ => Array.Empty<Control>()
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
