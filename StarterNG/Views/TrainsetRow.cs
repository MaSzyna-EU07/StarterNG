using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Application;
using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Views;

/// <summary>
/// A consist's row in a list of them: its vehicles as minis, each stretch of vehicles
/// alike as one picture with its name and count under it, so the consist reads at a glance.
/// </summary>
internal static class TrainsetRow
{
    /// <summary>The height the minis are made at: shown at it they stay sharp.</summary>
    private const int MiniHeight = 30;

    private static readonly MiniTextures Minis = new();

    /// <summary>Room under the captions for the scroll bar of a consist wider than the list.</summary>
    private const double ScrollBarLane = 10;

    private static readonly IBrush AiTagBack = new SolidColorBrush(Color.Parse("#9098A0"));
    private static readonly IBrush AiTagText = new SolidColorBrush(Color.Parse("#15171B"));

    public static Control Build(Trainset trainset)
    {
        string label = TrainsetDisplay.ListLabel(trainset);

        Control row;
        if (TrainsetDisplay.IsEmpty(trainset))
        {
            row = new TextBlock
            {
                Text = label,
                FontStyle = FontStyle.Italic,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            bool ai = TrainsetDisplay.IsAi(trainset);
            var panel = new DockPanel { Margin = new Thickness(0, 3), LastChildFill = true };
            if (ai)
            {
                var tag = AiTag();
                tag.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(tag, Dock.Right);
                panel.Children.Add(tag);
            }
            // A consist longer than the list is wide scrolls sideways in its own row. The wheel
            // still scrolls the list: only shift with the wheel, or a touchpad, moves the strip.
            panel.Children.Add(ScrollingStrip(trainset.Vehicles, ai));
            row = panel;
        }

        ToolTip.SetTip(row, label);
        AutomationProperties.SetName(row, label);
        return row;
    }

    /// <summary>
    /// The vehicles' minis with their captions, scrolling sideways when wider than the room
    /// they get - here and for the warehouse's consists.
    /// </summary>
    public static Control ScrollingStrip(IReadOnlyList<Dynamic> vehicles, bool dimmed = false)
    {
        var strip = Strip(vehicles, dimmed);
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = strip
        };
        // The bar lies over the content; a consist that scrolls gets a lane under its
        // captions for it, so the bar does not cross them. One that fits stays as low as it was.
        scroller.ScrollChanged += (_, _) =>
        {
            double below = scroller.Extent.Width > scroller.Viewport.Width + 0.5 ? ScrollBarLane : 0;
            if (strip.Margin.Bottom != below)
                strip.Margin = new Thickness(0, 0, 0, below);
        };
        return scroller;
    }

    /// <summary>
    /// The minis, one next to the other with nothing between, as the vehicles stand coupled.
    /// </summary>
    private static Control Strip(IReadOnlyList<Dynamic> vehicles, bool dimmed)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Opacity = dimmed ? 0.6 : 1 };

        var runs = ConsistRuns.Of(vehicles, (v, i) => $"{MiniName(v)}|{Caption(v, i)}");
        int place = 0;
        foreach (var run in runs)
        {
            strip.Children.Add(RunView(run, place));
            place += run.Count;
        }
        return strip;
    }

    private static Control RunView(ConsistRun run, int place)
    {
        var v = run.First;
        var bmp = Minis.Get(MiniName(v), MiniHeight);

        Control picture;
        double width;
        if (bmp is not null)
        {
            width = bmp.Size.Height > 0 ? bmp.Size.Width * MiniHeight / bmp.Size.Height : MiniHeight;
            // Fill at a width worked out here, not Uniform: rounding at a scale such as 125 %
            // left a line of the background between two vehicles.
            picture = MiniTextures.Sharp(new Image
            {
                Source = bmp,
                Width = width,
                Height = MiniHeight,
                Stretch = Stretch.Fill
            });
        }
        else
        {
            picture = MiniTextures.MissingVisual(v.SkinFile, MiniHeight);
            width = picture.Width;
        }

        string caption = Caption(v, place);
        if (run.Count > 1)
            caption += $" ×{run.Count}";

        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Width = width,
            Children =
            {
                picture,
                new TextBlock
                {
                    Text = caption,
                    FontSize = 11,
                    Padding = new Thickness(3, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                }
            }
        };
    }

    private static Control AiTag() =>
        new Border
        {
            Background = AiTagBack,
            Padding = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "AI",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = AiTagText
            }
        };

    /// <summary>A vehicle leading the consist under its own name, the rest under their class - as the composition text has them.</summary>
    private static string Caption(Dynamic v, int place) =>
        TrainsetDisplay.VehicleLabel(v, head: place == 0 && TrainsetDisplay.IsPowered(v));

    /// <summary>The mini the consist strip shows for the vehicle.</summary>
    private static string? MiniName(Dynamic v)
    {
        string? mini = AppServices.Current.Library.Vehicles.MiniFor(v.DataFolder, v.SkinFile, v.MmdFile) ?? v.SkinFile;
        if (!AppServices.Current.MiniTextures.Has(mini) && AppServices.Current.MiniTextures.Has(v.SkinFile))
            mini = v.SkinFile;
        return mini;
    }
}
