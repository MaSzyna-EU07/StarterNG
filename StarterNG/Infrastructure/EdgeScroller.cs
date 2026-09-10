using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StarterNG.Infrastructure;

public sealed class EdgeScroller
{
    private const double Zone = 56;
    private const double MaxStep = 18;

    private readonly DispatcherTimer _timer;
    private ScrollViewer? _target;
    private double _step;

    public EdgeScroller()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Step();
    }

    public void Update(ScrollViewer scroll, double pointerX)
    {
        double width = scroll.Viewport.Width;

        _step = pointerX < Zone
            ? -MaxStep * Math.Clamp((Zone - pointerX) / Zone, 0, 1)
            : pointerX > width - Zone
                ? MaxStep * Math.Clamp((pointerX - (width - Zone)) / Zone, 0, 1)
                : 0;

        _target = scroll;

        if (_step == 0)
            _timer.Stop();
        else if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _target = null;
        _step = 0;
    }

    private void Step()
    {
        if (_target is null || _step == 0)
        {
            _timer.Stop();
            return;
        }

        double max = Math.Max(0, _target.Extent.Width - _target.Viewport.Width);
        double x = Math.Clamp(_target.Offset.X + _step, 0, max);
        if (Math.Abs(x - _target.Offset.X) < 0.01)
            return;

        _target.Offset = new Vector(x, _target.Offset.Y);
    }
}
