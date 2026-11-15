using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace StarterNG.Views;

/// <summary>A menu entry that runs something when clicked, as nearly all of them do.</summary>
internal static class MenuItems
{
    /// <param name="header">The text, or a control where a row holds more than that.</param>
    public static MenuItem Item(object header, Action run, bool enabled = true, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGesture = gesture };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>
    /// One of a set of entries of which one is chosen, marked as a radio entry. A dot
    /// written before the text was read out as "black circle" and told no state.
    /// </summary>
    public static MenuItem Choice(string header, bool chosen, Action run)
    {
        var item = Item(header, run);
        item.ToggleType = MenuItemToggleType.Radio;
        item.IsChecked = chosen;
        return item;
    }

    /// <summary>The entry at the end of a menu or a submenu.</summary>
    public static MenuItem AddItem(this ItemsControl menu, object header, Action run, bool enabled = true,
                                   KeyGesture? gesture = null)
    {
        var item = Item(header, run, enabled, gesture);
        menu.Items.Add(item);
        return item;
    }
}
