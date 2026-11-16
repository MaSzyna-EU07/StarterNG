using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StarterNG.Controls;

/// <summary>
/// Says how many entries a search box left once typing pauses: the list filled itself
/// out of sight, and a screen reader heard only the letters typed. Nothing is said
/// while focus is elsewhere or the box is empty.
/// </summary>
public sealed class SearchResultsTeller
{
    /// <summary>The pause after the last key that counts as done typing.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(900);

    private readonly TextBox _box;
    private readonly Func<int> _count;
    private readonly DispatcherTimer _timer = new() { Interval = Pause };

    public SearchResultsTeller(TextBox box, Func<int> count)
    {
        _box = box;
        _count = count;
        _timer.Tick += (_, _) => Tell();
    }

    /// <summary>The text changed: the count is said after the pause, once.</summary>
    public void Changed()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void Tell()
    {
        _timer.Stop();
        if (!_box.IsKeyboardFocusWithin || string.IsNullOrWhiteSpace(_box.Text))
            return;

        Announcer.Say(_box, string.Format(AppLanguage.Text("SearchFound"), _count()));
    }
}
