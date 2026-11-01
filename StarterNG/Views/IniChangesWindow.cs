using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using StarterNG.Application;
using StarterNG.Infrastructure;

namespace StarterNG.Views;

/// <summary>What a save would change in eu07.ini, key by key, before it is made.</summary>
internal static class IniChangesWindow
{
    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#9098A0"));
    private static readonly IBrush New = new SolidColorBrush(Color.Parse("#41C400"));

    /// <returns>Whether the save was asked for.</returns>
    public static async Task<bool> Show(Window owner, string path, IReadOnlyList<IniChange> changes,
                                        bool keyboardChanged)
    {
        var win = MessageBox.FixedDialog(App.Loc["IniChangesTitle"], 680);

        var list = new StackPanel { Spacing = 2 };
        AddGroup(list, App.Loc["IniChangesChanged"], changes.Where(c => c.Before is not null && c.After is not null));
        AddGroup(list, App.Loc["IniChangesAdded"], changes.Where(c => c.Before is null));
        AddGroup(list, App.Loc["IniChangesRemoved"], changes.Where(c => c.After is null));

        var body = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = path, Foreground = Dim, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis
        });
        body.Children.Add(changes.Count == 0
            ? new TextBlock { Text = App.Loc["IniChangesNone"], TextWrapping = TextWrapping.Wrap }
            : new ScrollViewer { MaxHeight = 440, Content = list });
        if (keyboardChanged)
            body.Children.Add(new TextBlock { Text = App.Loc["IniChangesKeyboard"], TextWrapping = TextWrapping.Wrap });

        bool save = false;
        var saveBtn = new Button { Content = App.Loc["Save"], MinWidth = 80 };
        saveBtn.Classes.Add("Accent");
        saveBtn.IsEnabled = changes.Count > 0 || keyboardChanged;
        saveBtn.Click += (_, _) => { save = true; win.Close(); };
        var closeBtn = new Button { Content = App.Loc["Close"], MinWidth = 80 };
        closeBtn.Classes.Add("Flat");
        closeBtn.Click += (_, _) => win.Close();
        body.Children.Add(MessageBox.ButtonRow(saveBtn, closeBtn));

        win.Content = body;
        await win.ShowDialog(owner);
        return save;
    }

    private static void AddGroup(Panel list, string title, IEnumerable<IniChange> group)
    {
        var changes = group.ToList();
        if (changes.Count == 0)
            return;

        list.Children.Add(new TextBlock
        {
            Text = $"{title} ({changes.Count})",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 12, 0, 4)
        });
        foreach (var change in changes)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*") };
            row.Children.Add(new TextBlock
            {
                Text = change.Key, FontFamily = MonoFont.Family, FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var value = new TextBlock { FontFamily = MonoFont.Family, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            if (change.Before is not null)
                value.Inlines!.Add(new Run(change.Before)
                {
                    Foreground = Dim,
                    TextDecorations = change.After is null ? TextDecorations.Strikethrough : null
                });
            if (change.Before is not null && change.After is not null)
                value.Inlines!.Add(new Run("  →  ") { Foreground = Dim });
            if (change.After is not null)
                value.Inlines!.Add(new Run(change.After) { Foreground = New });
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            list.Children.Add(row);
        }
    }
}
