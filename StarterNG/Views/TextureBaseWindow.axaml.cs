using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using StarterNG.Classes;

namespace StarterNG.Views;

public partial class TextureBaseWindow : Window
{
    private readonly VehicleDatabase _db = GameData.Instance.Vehicles;
    private readonly List<Row> _all = new();

    public VehicleTexture? Picked { get; private set; }

    public TextureBaseWindow()
    {
        InitializeComponent();

        foreach (var t in _db.Textures)
        {
            if (_db.IsSetFollower(t)) continue;
            _all.Add(new Row(t, _db));
        }

        foreach (var box in new Control[] { fName, fOp, fMini, fAuthor, fStation, fPhoto })
        {
            if (box is TextBox tb)
                tb.TextChanged += (_, _) => Refresh();
        }
        foreach (var box in new Control[] { fNameOn, fOpOn, fMiniOn, fAuthorOn, fStationOn, fPhotoOn })
        {
            if (box is CheckBox cb)
                cb.IsCheckedChanged += (_, _) => Refresh();
        }

        textureList.ContextRequested += TextureList_OnContextRequested;
        Refresh();
    }

    private void Refresh()
    {
        IEnumerable<Row> q = _all;

        q = Filter(q, fNameOn, fName, r => r.Skin);
        q = Filter(q, fOpOn, fOp, r => r.Operator);
        q = Filter(q, fMiniOn, fMini, r => r.Mini);
        q = Filter(q, fAuthorOn, fAuthor, r => r.Author);
        q = Filter(q, fStationOn, fStation, r => r.Station);
        q = Filter(q, fPhotoOn, fPhoto, r => r.Photo);

        var list = q
            .OrderBy(r => r.Mini, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Skin, StringComparer.OrdinalIgnoreCase)
            .Take(5000)
            .ToList();

        textureList.ItemsSource = list;
        statusText.Text = $"{list.Count} / {_all.Count}";
    }

    private static IEnumerable<Row> Filter(
        IEnumerable<Row> src, CheckBox? on, TextBox? box, Func<Row, string> field)
    {
        if (on?.IsChecked != true) return src;
        string needle = box?.Text?.Trim() ?? "";
        if (needle.Length < 1) return src;
        return src.Where(r => field(r).Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void TextureList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (textureList.SelectedItem is not Row row) return;
        Picked = row.Texture;
        Close();
    }

    private void TextureList_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (textureList.SelectedItem is not Row row) return;

        var menu = new ContextMenu();
        var copy = new MenuItem { Header = App.Loc["CopyTextureName"] };
        copy.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(row.Skin);
        };
        menu.Items.Add(copy);

        var open = new MenuItem { Header = App.Loc["OpenTextureFolder"] };
        open.Click += (_, _) =>
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "dynamic",
                row.Texture.Directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd('\\', '/'));
            if (!Directory.Exists(dir)) return;
            try
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch { }
        };
        menu.Items.Add(open);
        menu.Open(textureList);
        e.Handled = true;
    }

    private sealed class Row
    {
        public VehicleTexture Texture { get; }
        public string Skin { get; }
        public string Mini { get; }
        public string Operator { get; }
        public string Revision { get; }
        public string Author { get; }
        public string Station { get; }
        public string Photo { get; }

        public Row(VehicleTexture t, VehicleDatabase db)
        {
            Texture = t;
            Skin = Path.GetFileNameWithoutExtension(t.Skinfile);
            Mini = db.ResolveMiniName(t) ?? t.ResolvedClass ?? "";
            Operator = t.Meta?.Operator ?? "";
            Revision = t.Meta?.RevisionDate ?? "";
            Author = t.Meta?.TextureAuthor ?? "";
            Station = t.Meta?.Depot ?? "";
            Photo = t.Meta?.PhotoAuthor ?? "";
        }
    }
}
