using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StarterNG.Views;

public enum MessageBoxButtons
{
    YesNo,
    Ok
}

public static class MessageBox
{
    public static async Task<bool> Show(Window owner, string message, string title, MessageBoxButtons buttons)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool yes = false;
        var yesBtn = new Button { Content = App.Loc["Yes"], MinWidth = 80, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        yesBtn.Classes.Add("Accent");
        yesBtn.Click += (_, _) => { yes = true; win.Close(); };

        var noBtn = new Button { Content = App.Loc["No"], MinWidth = 80, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        noBtn.Classes.Add("Flat");
        noBtn.Click += (_, _) => win.Close();

        if (buttons == MessageBoxButtons.Ok)
        {
            yesBtn.Content = App.Loc["Ok"];
            noBtn.IsVisible = false;
        }

        win.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yesBtn, noBtn }
                }
            }
        };

        await win.ShowDialog(owner);
        return yes;
    }
}
