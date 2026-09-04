using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
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
            var pair = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                // no trailing gap on the last pair - these sit in a panel title,
                // flush with its right edge
                Margin = new Avalonia.Thickness(0, 0, i == fields.Count - 1 ? 0 : 22, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            pair.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center
            });
            pair.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            host.Children.Add(pair);
        }
    }
}
