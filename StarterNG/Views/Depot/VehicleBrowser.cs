using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Views;

public sealed class VehicleBrowser
{
    private readonly ComboBox categoryCombo;
    private readonly ComboBox classCombo;
    private readonly ListBox vehicleListBox;
    private readonly TextBox searchBox;
    private readonly CheckBox hideArchivalCheck;
    private readonly Image miniPreview;
    private readonly Button addVehicleButton;

    private readonly VehicleDatabase _db;
    private readonly MiniTextures _minis;
    private readonly Action _openTextureBase;

    public void SyncHideArchival()
    {
        bool hideArchival = Classes.Settings.Instance.HideArchivalVehicles;
        if (hideArchivalCheck.IsChecked == hideArchival)
            return;

        _suppress = true;
        hideArchivalCheck.IsChecked = hideArchival;
        _suppress = false;
        Rebuild();
    }

    public VehicleTexture? Selected { get; private set; }

    private bool _suppress;
    private bool _syncingCombos;

    private DispatcherTimer? _searchTimer;

    private Func<string?, bool>? _categoryFilter;
    private string? _classFilter;

    public VehicleBrowser(
        ComboBox categoryCombo, ComboBox classCombo, ListBox vehicleListBox, TextBox searchBox,
        CheckBox hideArchivalCheck, Image miniPreview, Button addVehicleButton,
        VehicleDatabase db, MiniTextures minis, Action openTextureBase)
    {
        this.categoryCombo = categoryCombo;
        this.classCombo = classCombo;
        this.vehicleListBox = vehicleListBox;
        this.searchBox = searchBox;
        this.hideArchivalCheck = hideArchivalCheck;
        this.miniPreview = miniPreview;
        this.addVehicleButton = addVehicleButton;

        _db = db;
        _minis = minis;
        _openTextureBase = openTextureBase;

        vehicleListBox.ContextRequested += List_OnContextRequested;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            Rebuild();
        };
    }

    private const int MaxListRows = 2000;

    private const int MiniPreviewHeight = 54;

    private const int CompactThumbHeight = 24;

    private static readonly (string LocKey, Func<string?, bool> Match)[] CategoryDefs =
    {
        ("CatElectricLoco", c => c == "e"),
        ("CatDieselLoco",   c => c == "s"),
        ("CatSteamLoco",    c => c == "p"),
        ("CatEMU",          c => c == "z"),
        ("CatRailbus",      c => c == "a"),
        ("CatDraisine",     c => c == "d"),
        ("CatWork",         c => c == "r"),
        ("CatPrototype",    c => c == "x"),
        ("CatTram",         c => c == "t"),
        ("CatCar",          c => c == "o"),
        ("CatBus",          c => c == "b"),
        ("CatTruck",        c => c == "c"),
        ("CatPeople",       c => c == "h"),
        ("CatAnimals",      c => c == "f"),
        ("CatWagonsA",       c => c == "A"),
        ("CatWagonsB",       c => c == "B"),
        ("CatWagonsC",       c => c == "C"),
        ("CatWagonsD",       c => c == "D"),
        ("CatWagonsE",       c => c == "E"),
        ("CatWagonsF",       c => c == "F"),
        ("CatWagonsG",       c => c == "G"),
        ("CatWagonsH",       c => c == "H"),
        ("CatWagonsI",       c => c == "I"),
        ("CatWagonsJ",       c => c == "J"),
        ("CatWagonsK",       c => c == "K"),
        ("CatWagonsL",       c => c == "L"),
        ("CatWagonsM",       c => c == "M"),
        ("CatWagonsN",       c => c == "N"),
        ("CatWagonsO",       c => c == "O"),
        ("CatWagonsP",       c => c == "P"),
        ("CatWagonsR",       c => c == "R"),
        ("CatWagonsS",       c => c == "S"),
        ("CatWagonsT",       c => c == "T"),
        ("CatWagonsU",       c => c == "U"),
        ("CatWagonsV",       c => c == "V"),
        ("CatWagonsW",       c => c == "W"),
        ("CatWagonsX",       c => c == "X"),
        ("CatWagonsY",       c => c == "Y"),
        ("CatWagonsZ",       c => c == "Z"),

        ("CatOther",        IsOtherCat),
    };

    private static readonly HashSet<string> NamedLowerCats =
        new(StringComparer.Ordinal) { "a", "b", "c", "d", "e", "f", "h", "n", "o", "p", "r", "s", "t", "x", "z" };

    private static bool IsOtherCat(string? c) =>
        string.IsNullOrEmpty(c) ||
        (c is { Length: 1 } && char.IsLower(c[0]) && !NamedLowerCats.Contains(c));

    public void PopulateCategoryCombo()
    {
        int keep = categoryCombo.SelectedIndex;

        _suppress = true;
        categoryCombo.Items.Clear();
        foreach (var (locKey, match) in CategoryDefs)
            categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc[locKey], Tag = match });
        categoryCombo.SelectedIndex = keep >= 0 && keep < categoryCombo.Items.Count ? keep : -1;
        _suppress = false;

        _categoryFilter = categoryCombo.SelectedItem is ComboBoxItem { Tag: Func<string?, bool> f }
            ? f : null;
        RebuildClassCombo();
    }

    private void RebuildClassCombo()
    {
        _suppress = true;
        FillClassCombo();
        classCombo.SelectedIndex = -1;
        _suppress = false;

        _classFilter = null;
        Rebuild();
    }

    public void InitClassComboTemplates()
    {
        classCombo.ItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, large: true), false);
        classCombo.SelectionBoxItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, large: false), false);
    }

    private void FillClassCombo()
    {
        classCombo.Items.Clear();
        foreach (string cls in ClassesForCategory(_categoryFilter))
            classCombo.Items.Add(cls);
    }

    private bool ApplyFilters(string? category, string? cls)
    {
        var catItem = categoryCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(it => it.Tag is Func<string?, bool> f && f(category));

        if (catItem != null && ReferenceEquals(categoryCombo.SelectedItem, catItem) &&
            string.Equals(_classFilter, cls, StringComparison.OrdinalIgnoreCase))
            return false;

        _suppress = true;
        try
        {
            if (catItem != null)
            {
                categoryCombo.SelectedItem = catItem;
                _categoryFilter = catItem.Tag as Func<string?, bool>;
            }
            FillClassCombo();
            classCombo.SelectedItem = classCombo.Items.OfType<string>()
                .FirstOrDefault(s => string.Equals(s, cls, StringComparison.OrdinalIgnoreCase));
            _classFilter = classCombo.SelectedItem as string;
        }
        finally { _suppress = false; }
        return true;
    }

    private string? CategoryOfClass(string cls) =>
        _db.Textures.FirstOrDefault(t => string.Equals(VehicleInfo.ClassOf(t), cls, StringComparison.OrdinalIgnoreCase))
            is { } t ? VehicleInfo.CategoryOf(t) : null;

    private Control ClassComboContent(string cls, bool large)
    {
        int height = large ? MiniPreviewHeight : CompactThumbHeight;
        var cell = new StackPanel
        {
            Orientation = large ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = large ? 2 : 8,
            HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var bmp = _minis.Get(cls, height);
        if (bmp != null)
            cell.Children.Add(MiniTextures.Sharp(new Image
            {
                Source = bmp, Height = height,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }));
        cell.Children.Add(new TextBlock
        {
            Text = cls,
            HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return cell;
    }

    private IEnumerable<string> ClassesForCategory(Func<string?, bool>? category) =>
        _db.Textures
            .Where(t => category == null || category(VehicleInfo.CategoryOf(t)))
            .Where(t => !_db.IsSetFollower(t))
            .Where(t => !StarterNG.Classes.Settings.Instance.HideArchivalVehicles || !t.ResolvedArchived)
            .Select(VehicleInfo.ClassOf)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

    public void CategoryCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _categoryFilter = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as Func<string?, bool>;
        RebuildClassCombo();
    }

    public void ClassCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        string? cls = classCombo.SelectedItem as string;
        _classFilter = cls;

        if (cls != null && _categoryFilter == null &&
            PostSyncing(() => { ApplyFilters(CategoryOfClass(cls), cls); Rebuild(); }))
            return;

        Rebuild();
    }

    public void Combo_OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ComboBox combo || combo.IsDropDownOpen)
            return;

        int count = combo.ItemCount;
        if (count == 0)
            return;

        int index = combo.SelectedIndex;
        if (e.Delta.Y > 0)
            index = index <= 0 ? 0 : index - 1;
        else if (e.Delta.Y < 0)
            index = index < 0 ? 0 : Math.Min(index + 1, count - 1);
        else
            return;

        combo.SelectedIndex = index;
        e.Handled = true;
    }

    public void Rebuild()
    {

        Selected = null;
        miniPreview.Source = null;
        addVehicleButton.IsEnabled = false;

        vehicleListBox.Items.Clear();

        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        if (_categoryFilter == null && _classFilter == null && !hasSearch)
            return;

        var matched = _db.Textures
            .Where(t => PassesFilters(t, search, hasSearch))
            .OrderBy(t => t.TextureMini ?? t.Skinfile, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListRows)
            .ToList();

        foreach (var texture in matched)
        {
            var row = new ListBoxItem
            {
                Content = BrowserLabel(texture, _db.ResolveSet(texture)),
                Tag = texture
            };
            vehicleListBox.Items.Add(row);
        }

        if (vehicleListBox.Items.Count == 0)
            vehicleListBox.Items.Add(new ListBoxItem
            {
                Content = App.Loc["NoVehicles"],
                IsEnabled = false
            });
    }

    public void VehicleListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (vehicleListBox.SelectedItem is ListBoxItem { Tag: VehicleTexture texture })
        {
            Selected = texture;
            miniPreview.Source = _minis.Get(_db.ResolveMiniName(texture), MiniPreviewHeight);
            addVehicleButton.IsEnabled = true;
            SyncCombosTo(texture);
        }
        else
        {
            Selected = null;
            miniPreview.Source = null;
            addVehicleButton.IsEnabled = false;
        }
    }

    private void List_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var hit = e.Source as Control;
        while (hit != null && hit is not ListBoxItem)
            hit = hit.Parent as Control;

        if (hit is not ListBoxItem { Tag: VehicleTexture texture })
            return;

        BuildBrowserMenu(texture).Open(hit);
        e.Handled = true;
    }

    private ContextMenu BuildBrowserMenu(VehicleTexture texture)
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = App.Loc["CopyTextureName"] };
        copy.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(vehicleListBox);
            if (top?.Clipboard != null)
                await top.Clipboard.SetTextAsync(BrowserName(texture));
        };
        menu.Items.Add(copy);

        var open = new MenuItem { Header = App.Loc["OpenTextureFolder"] };
        open.Click += (_, _) =>
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "dynamic",
                texture.Directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd('\\', '/'));
            if (!Directory.Exists(dir))
                dir = Path.Combine(Directory.GetCurrentDirectory(), texture.Directory);
            if (!Directory.Exists(dir))
            {
                StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(
                    string.Format(App.Loc["FaultFileNotFound"], dir));
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(
                    $"{dir}{Environment.NewLine}{Environment.NewLine}{App.Loc["FaultDetail"]} {ex.Message}");
            }
        };
        menu.Items.Add(open);

        menu.Items.Add(new Separator());
        var baseItem = new MenuItem { Header = App.Loc["OpenTextureBase"] };
        baseItem.Click += (_, _) => _openTextureBase();
        menu.Items.Add(baseItem);

        return menu;
    }

    public void ShowTextureInfo(VehicleTexture texture, StackPanel target)
    {
        target.Children.Clear();

        var full = new List<string>();
        void Row(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string line = $"{label}: {value}";
            full.Add(line);
            target.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 12,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Row(App.Loc["TextureInfo"], Consist.Base(texture.Skinfile));
        var meta = texture.Meta;
        if (meta != null)
        {
            Row(App.Loc["TexOperator"], meta.Operator);
            Row(App.Loc["TexStation"], meta.Depot);
            Row(App.Loc["TexRevision"], meta.RevisionDate);
            Row(App.Loc["TexWorks"], meta.RevisionPlace);
            Row(App.Loc["TexAuthor"], meta.TextureAuthor);
            Row(App.Loc["TexPhoto"], meta.PhotoAuthor);
        }
        ToolTip.SetTip(target, string.Join("\n", full));
    }

    public List<VehicleTexture> CurrentTextures()
    {
        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        if (_categoryFilter == null && _classFilter == null && !hasSearch)
            return new List<VehicleTexture>();

        return _db.Textures
            .Where(t => PassesFilters(t, search, hasSearch))
            .OrderBy(t => t.TextureMini ?? t.Skinfile, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool PassesFilters(VehicleTexture t, string search, bool hasSearch)
    {
        if (_categoryFilter != null && !_categoryFilter(VehicleInfo.CategoryOf(t)))
            return false;

        if (_classFilter != null &&
            !string.Equals(VehicleInfo.ClassOf(t), _classFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_db.IsSetFollower(t))
            return false;

        if (StarterNG.Classes.Settings.Instance.HideArchivalVehicles && t.ResolvedArchived)
            return false;

        if (hasSearch && !Matches(t, search))
            return false;

        return true;
    }

    private bool Matches(VehicleTexture t, string f)
    {
        bool C(string? s) => !string.IsNullOrEmpty(s) &&
                             s.Contains(f, StringComparison.OrdinalIgnoreCase);
        return C(t.Skinfile) || C(t.Model) || C(t.TextureMini) || C(t.MiniRef)
               || C(t.Meta?.Vehicle) || C(t.Meta?.Operator) || C(VehicleInfo.ClassOf(t));
    }

    private static string BrowserName(VehicleTexture texture)
    {
        if (!string.IsNullOrEmpty(texture.TextureMini) &&
            !string.Equals(texture.TextureMini, texture.ResolvedClass, StringComparison.OrdinalIgnoreCase))
            return texture.TextureMini!;
        return Consist.Base(texture.Skinfile);
    }

    private string BrowserLabel(VehicleTexture texture, IReadOnlyList<VehicleTexture>? set)
    {
        string name = BrowserName(texture);

        if (!string.IsNullOrEmpty(texture.Meta?.Operator))
            name += $"  ·  {texture.Meta!.Operator}";
        if (set is { Count: > 1 })
            name += $"   [{set.Count}]";
        return name;
    }

    public void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || _searchTimer is null) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    public void HideArchivalCheck_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppress) return;
        StarterNG.Classes.Settings.Instance.HideArchivalVehicles = hideArchivalCheck.IsChecked ?? true;
        StarterNG.Classes.Settings.Instance.Save();
        Rebuild();
    }

    public void SelectInBrowser(Dynamic car)
    {
        var texture = _db.TextureForSkin(car.SkinFile);
        if (texture is null)
            return;

        var set = _db.ResolveSet(texture);
        var browserTex = set is { Count: > 0 } ? set[0] : texture;

        WhileSyncing(() =>
        {
            ApplyFilters(VehicleInfo.CategoryOf(browserTex), VehicleInfo.ClassOf(browserTex));
            Rebuild();
            SelectListEntry(browserTex);
        });
    }

    private void SyncCombosTo(VehicleTexture texture)
    {
        if (_syncingCombos || !ApplyFilters(VehicleInfo.CategoryOf(texture), VehicleInfo.ClassOf(texture)))
            return;

        PostSyncing(() => { Rebuild(); SelectListEntry(texture); });
    }

    private void WhileSyncing(Action work)
    {
        _syncingCombos = true;
        try { work(); }
        finally { _syncingCombos = false; }
    }

    private bool PostSyncing(Action work)
    {
        if (_syncingCombos)
            return false;

        _syncingCombos = true;
        Dispatcher.UIThread.Post(() => WhileSyncing(work), DispatcherPriority.Background);
        return true;
    }

    private void SelectListEntry(VehicleTexture texture)
    {
        foreach (var obj in vehicleListBox.Items)
            if (obj is ListBoxItem { Tag: VehicleTexture t } entry &&
                string.Equals(t.Skinfile, texture.Skinfile, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Directory, texture.Directory, StringComparison.OrdinalIgnoreCase))
            {
                vehicleListBox.SelectedItem = entry;
                entry.BringIntoView();
                break;
            }
    }
}
