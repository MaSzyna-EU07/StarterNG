using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace StarterNG.Infrastructure;

public static class StatsBar
{
    public static void Fill(Panel host, IReadOnlyList<(string Label, string Value)> fields)
    {
        host.Children.Clear();
        for (int i = 0; i < fields.Count; i++)
        {
            var (label, value) = fields[i];
            host.Children.Add(Figure(label, value, last: i == fields.Count - 1));
        }
    }

    /// <summary>
    /// One text holding the label and the value: under the mouse a reader hears the
    /// text that lies there, and with two it heard "Masa [t]:" without the mass.
    /// </summary>
    public static TextBlock Figure(string label, string value, bool last)
    {
        var name = new Run(label);
        // The label dimmed, as the panel titles' other labels.
        name.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("CarbonFgDim"));

        return new TextBlock
        {
            FontSize = 12,
            // no trailing gap on the last pair - these sit in a panel title,
            // flush with its right edge
            Margin = new Avalonia.Thickness(0, 0, last ? 0 : 22, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Inlines = new InlineCollection
            {
                name,
                new Run(" "),
                new Run(value) { FontWeight = FontWeight.SemiBold },
            },
        };
    }
}
