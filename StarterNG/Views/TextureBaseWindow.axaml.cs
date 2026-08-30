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
    private readonly List<TextureRow> _all = new();

    public VehicleTexture? Picked { get; private set; }

    public TextureBaseWindow()
    {
        InitializeComponent();

        foreach (var t in _db.Textures)
        {
            if (_db.IsSetFollower(t)) continue;
            _all.Add(new TextureRow(t, _db));
        }

        foreach (var box in new Control[] { fName, fOp, fMini, fAuthor, fStation, fPhoto, fModel })
        {
            if (box is TextBox tb)
                tb.TextChanged += (_, _) => Refresh();
        }
        foreach (var box in new Control[] { fNameOn, fOpOn, fMiniOn, fAuthorOn, fStationOn, fPhotoOn,
                                            fModelOn, fRevFromOn, fRevToOn })
        {
            if (box is CheckBox cb)
                cb.IsCheckedChanged += (_, _) => Refresh();
        }

        fRevFrom.TextChanged += (_, _) => Refresh();
        fRevTo.TextChanged += (_, _) => Refresh();

        BuildTypeList();
        typeList.SelectionChanged += (_, _) => { BuildModelList(); Refresh(); };
        modelList.SelectionChanged += (_, _) => Refresh();

        textureList.ContextRequested += TextureList_OnContextRequested;
        Refresh();
    }

    private void BuildModelList()
    {
        modelList.ItemsSource = null;

        string? cat = (typeList.SelectedItem as ListBoxItem)?.Tag as string;
        var rows = _all
            .Where(r => cat == null || string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Mini.Length > 0)
            .GroupBy(r => r.Mini, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select((g, i) => new TextureModelRow($"{i + 1}.", g.Key, g.First().Directory))
            .ToList();

        modelList.ItemsSource = rows;
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private void BuildTypeList()
    {
        typeList.Items.Add(new ListBoxItem { Content = App.Loc["CatAll"], Tag = null });

        foreach (string cat in _all.Select(r => r.Category)
                     .Where(c => c.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            typeList.Items.Add(new ListBoxItem { Content = cat, Tag = cat });

        typeList.SelectedIndex = 0;
        BuildModelList();
    }

    private void Refresh()
    {
        IEnumerable<TextureRow> q = _all;

        q = Filter(q, fNameOn, fName, r => r.Skin);
        q = Filter(q, fOpOn, fOp, r => r.Operator);
        q = Filter(q, fMiniOn, fMini, r => r.Mini);
        q = Filter(q, fAuthorOn, fAuthor, r => r.Author);
        q = Filter(q, fStationOn, fStation, r => r.Station);
        q = Filter(q, fPhotoOn, fPhoto, r => r.Photo);
        q = Filter(q, fModelOn, fModel, r => r.Model);

        if ((typeList.SelectedItem as ListBoxItem)?.Tag is string cat)
            q = q.Where(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase));

        if (modelList.SelectedItem is TextureModelRow mr)
            q = q.Where(r => string.Equals(r.Mini, mr.Klasa, StringComparison.OrdinalIgnoreCase));

        if (fRevFromOn.IsChecked == true && ParseDate(fRevFrom.Text) is { } from)
            q = q.Where(r => r.RevisionDate is { } d && d >= from);
        if (fRevToOn.IsChecked == true && ParseDate(fRevTo.Text) is { } to)
            q = q.Where(r => r.RevisionDate is { } d && d <= to);

        var list = q
            .OrderBy(r => r.Mini, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Skin, StringComparer.OrdinalIgnoreCase)
            .Take(5000)
            .ToList();

        textureList.ItemsSource = list;
        statusText.Text = $"{list.Count} / {_all.Count}";
    }

    private static IEnumerable<TextureRow> Filter(
        IEnumerable<TextureRow> src, CheckBox? on, TextBox? box, Func<TextureRow, string> field)
    {
        if (on?.IsChecked != true) return src;
        string needle = box?.Text?.Trim() ?? "";
        if (needle.Length < 1) return src;
        return src.Where(r => field(r).Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void TextureList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (textureList.SelectedItem is not TextureRow row) return;
        Picked = row.Texture;
        Close();
    }

    private void TextureList_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (textureList.SelectedItem is not TextureRow row) return;

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
            if (!Directory.Exists(dir))
                dir = Path.Combine(Directory.GetCurrentDirectory(), row.Texture.Directory);
            if (!Directory.Exists(dir))
            {
                StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(
                    string.Format(App.Loc["FaultFileNotFound"], dir));
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(
                    $"{dir}{Environment.NewLine}{Environment.NewLine}{App.Loc["FaultDetail"]} {ex.Message}");
            }
        };
        menu.Items.Add(open);
        menu.Open(textureList);
        e.Handled = true;
    }
}

internal sealed record TextureModelRow(string No, string Klasa, string Sciezka);

internal sealed class TextureRow
{
    public VehicleTexture Texture { get; }
    public string Skin { get; }
    public string Mini { get; }
    public string Operator { get; }
    public string Revision { get; }
    public string Author { get; }
    public string Station { get; }
    public string Photo { get; }
    public string Model { get; }
    public string Category { get; }
    public string Directory { get; }
    public DateTime? RevisionDate { get; }

    public TextureRow(VehicleTexture t, VehicleDatabase db)
    {
        Texture = t;
        Skin = Path.GetFileNameWithoutExtension(t.Skinfile);
        Mini = db.ResolveMiniName(t) ?? t.ResolvedClass ?? "";
        Operator = t.Meta?.Operator ?? "";
        Revision = t.Meta?.RevisionDate ?? "";
        Author = t.Meta?.TextureAuthor ?? "";
        Station = t.Meta?.Depot ?? "";
        Photo = t.Meta?.PhotoAuthor ?? "";
        Model = t.Model ?? "";
        Category = t.ResolvedCategory ?? "";
        Directory = t.Directory ?? "";
        RevisionDate = DateTime.TryParse(Revision, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var rev) ? rev : null;
    }
}

