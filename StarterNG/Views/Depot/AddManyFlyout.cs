using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarterNG.Views;

/// <summary>Asks how many times to add a vehicle, for the browser and the consist alike.</summary>
internal static class AddManyFlyout
{
    public const int Max = 100;

    public static void Show(Control anchor, PlacementMode placement, Action<int> add)
    {
        var count = new NumericUpDown
        {
            Minimum = 1, Maximum = Max, Increment = 1, FormatString = "0", Value = 2, MinWidth = 120
        };
        var confirm = new Button { Content = App.Loc["AddVehicle"], HorizontalAlignment = HorizontalAlignment.Right };
        confirm.Classes.Add("Flat");
        confirm.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 160 };
        panel.Children.Add(new TextBlock { Text = App.Loc["AddManyCount"], FontWeight = FontWeight.Bold, FontSize = 12 });
        panel.Children.Add(count);
        panel.Children.Add(confirm);

        var flyout = new Flyout { Content = panel, Placement = placement };
        void Commit()
        {
            flyout.Hide();
            add(Math.Clamp((int)(count.Value ?? 1), 1, Max));
        }

        confirm.Click += (_, _) => Commit();
        count.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Commit();
        };
        flyout.ShowAt(anchor);
        count.Focus();
    }
}
