using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;

namespace StarterNG.Infrastructure;

public sealed class DropGap
{
    private readonly Border _gap;
    private int _index = -1;

    public DropGap(int milliseconds = 130)
    {
        _gap = new Border
        {
            Width = 0,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Layoutable.WidthProperty,
                    Duration = TimeSpan.FromMilliseconds(milliseconds),
                    Easing = new CubicEaseOut()
                }
            }
        };
    }

    public bool IsGap(Control control) => ReferenceEquals(control, _gap);

    public void Show(Panel panel, int childIndex, double width)
    {
        int target = Math.Clamp(childIndex, 0, ChildCountWithoutGap(panel));

        if (_index != target)
        {
            panel.Children.Remove(_gap);
            panel.Children.Insert(Math.Clamp(target, 0, panel.Children.Count), _gap);
            _index = target;
        }

        _gap.Width = Math.Max(width, 8);
    }

    public void Hide(Panel panel)
    {
        if (_index < 0) return;

        _gap.Width = 0;
        panel.Children.Remove(_gap);
        _index = -1;
    }

    public void Forget() => _index = -1;

    private int ChildCountWithoutGap(Panel panel)
    {
        int count = panel.Children.Count;
        foreach (var child in panel.Children)
            if (ReferenceEquals(child, _gap))
                return count - 1;
        return count;
    }
}
