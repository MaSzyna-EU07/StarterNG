using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarterNG.Views;

public sealed class TextureRandomizerDialog
{
    public sealed record Options(bool WithoutArchival, int RevisionTolerance, int RevYear);

    private readonly Cursor _hand;

    public TextureRandomizerDialog(Cursor hand) => _hand = hand;

    public async Task<Options?> ShowAsync(Window owner)
    {
        var win = Build();
        bool? ok = await win.ShowDialog<bool?>(owner);
        return ok == true ? win.Tag as Options : null;
    }

    private Window Build()
    {
        static Control Docked(Control c, Dock side)
        {
            DockPanel.SetDock(c, side);
            c.Margin = new Thickness(8, 0, 0, 0);
            c.VerticalAlignment = VerticalAlignment.Center;
            return c;
        }

        static TextBlock Caption(string text) => new()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var revDiff = new CheckBox
        {
            Content = Caption(App.Loc["RandomizeTexturesRevDiff"]),
            IsChecked = true,
            FontSize = 12
        };
        var age = new NumericUpDown
        {
            Minimum = 2, Maximum = 8, Value = 3, Width = 110,
            FormatString = "0", ShowButtonSpinner = true
        };
        var vsCurrent = new RadioButton
        {
            Content = Caption(App.Loc["RandomizeTexturesVsCurrent"]),
            IsChecked = true,
            FontSize = 12,
            GroupName = "revBase"
        };
        var vsYear = new RadioButton
        {
            Content = Caption(App.Loc["RandomizeTexturesVsYear"]),
            FontSize = 12,
            GroupName = "revBase"
        };
        var year = new NumericUpDown
        {
            Minimum = 1950, Maximum = 2050, Value = DateTime.Now.Year, Width = 150,
            FormatString = "0", ShowButtonSpinner = true, IsEnabled = false
        };
        var noArch = new CheckBox
        {
            Content = Caption(App.Loc["RandomizeTexturesNoArchival"]),
            IsChecked = true,
            FontSize = 12
        };

        revDiff.IsCheckedChanged += (_, _) => age.IsEnabled = revDiff.IsChecked == true;
        vsYear.IsCheckedChanged += (_, _) => year.IsEnabled = vsYear.IsChecked == true;

        var win = new Window
        {
            Title = App.Loc["RandomizeTexturesTitle"],
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var rulesBtn = new Button
        {
            Content = App.Loc["RandomizeTexturesRules"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = _hand
        };
        rulesBtn.Classes.Add("Basic");
        rulesBtn.Click += async (_, _) => await new RulesWindow().ShowDialog(win);

        var ok = new Button
        {
            Content = App.Loc["RandomizeTexturesApply"],
            MinWidth = 90,
            Cursor = _hand
        };
        ok.Classes.Add("Accent");
        ok.Click += (_, _) =>
        {
            win.Tag = new Options(
                WithoutArchival: noArch.IsChecked == true,
                RevisionTolerance: revDiff.IsChecked == true ? (int)(age.Value ?? 3) : -1,
                RevYear: vsYear.IsChecked == true ? (int)(year.Value ?? DateTime.Now.Year) : -1);
            win.Close(true);
        };

        var cancel = new Button { Content = App.Loc["Cancel"], MinWidth = 90, Cursor = _hand };
        cancel.Classes.Add("Flat");
        cancel.Click += (_, _) => win.Close(false);

        win.Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = App.Loc["RandomizeTexturesTitle"],
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },

                new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        Docked(age, Dock.Right),
                        revDiff
                    }
                },
                vsCurrent,
                new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        Docked(year, Dock.Right),
                        vsYear
                    }
                },
                noArch,
                rulesBtn,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel }
                }
            }
        };
        return win;
    }
}
