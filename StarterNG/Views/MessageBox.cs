using System;
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
    /// <param name="yesText">A label that says what the answer does, in place of "Yes".</param>
    /// <param name="noText">Likewise in place of "No".</param>
    public static async Task<bool> Show(Window owner, string message, string title, MessageBoxButtons buttons,
                                        string? yesText = null, string? noText = null)
    {
        var win = FixedDialog(title, 420);

        bool yes = false;
        var yesBtn = DialogButton(buttons == MessageBoxButtons.Ok ? App.Loc["Ok"] : yesText ?? App.Loc["Yes"],
                                  "Accent", 80, () => { yes = true; win.Close(); });
        var noBtn = DialogButton(noText ?? App.Loc["No"], "Flat", 80, () => win.Close());
        noBtn.IsVisible = buttons != MessageBoxButtons.Ok;

        win.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                ButtonRow(yesBtn, noBtn)
            }
        };

        await win.ShowDialog(owner);
        return yes;
    }

    /// <summary>
    /// A notice with one action besides closing it, and a box to stop it coming
    /// back, which counts however the window is closed.
    /// </summary>
    public static async Task<(bool Act, bool Silence)> ShowNotice(Window owner, string message, string title,
                                                                 string actionText, string silenceText)
    {
        var win = FixedDialog(title, 460);

        bool act = false;
        var actBtn = DialogButton(actionText, "Accent", null, () => { act = true; win.Close(); });
        var okBtn = DialogButton(App.Loc["Ok"], "Flat", 80, () => win.Close());

        var silence = new CheckBox { Content = silenceText, VerticalAlignment = VerticalAlignment.Center };

        win.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new DockPanel
                {
                    Children =
                    {
                        silence,
                        ButtonRow(actBtn, okBtn)
                    }
                }
            }
        };

        await win.ShowDialog(owner);
        return (act, silence.IsChecked == true);
    }

    /// <summary>A dialog over its owner, as tall as what it holds, not to be resized.</summary>
    internal static Window FixedDialog(string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false
    };

    /// <summary>A dialog's buttons, at its right edge.</summary>
    internal static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        row.Children.AddRange(buttons);
        return row;
    }

    private static Button DialogButton(string content, string cssClass, double? minWidth, Action onClick)
    {
        var button = new Button { Content = content, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        if (minWidth is { } width)
            button.MinWidth = width;
        button.Classes.Add(cssClass);
        button.Click += (_, _) => onClick();
        return button;
    }
}
