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
using Avalonia.VisualTree;

using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Sceneries;
using StarterNG.Domain.Vehicles;
using StarterNG.Application;
using StarterNG.Controls;

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

    private readonly VehicleCatalog _db;
    private readonly MiniTextures _minis;
    private readonly Action _openTextureBase;

    public void SyncHideArchival()
    {
        bool hideArchival = AppServices.Current.Settings.HideArchivalVehicles;
        if (hideArchivalCheck.IsChecked == hideArchival)
            return;

        _suppress = true;
        hideArchivalCheck.IsChecked = hideArchival;
        _suppress = false;
        Rebuild();
    }

    public VehicleTexture? Selected { get; private set; }

    public Action<VehicleTexture>? TextureSelected { get; set; }

    /// <summary>Adds the vehicle to the consist the given number of times.</summary>
    public Action<VehicleTexture, int>? AddMany { get; set; }

    private bool _suppress;
    private bool _syncingCombos;

    private DispatcherTimer? _searchTimer;

    private Func<string?, bool>? _categoryFilter;
    private string? _classFilter;

    public VehicleBrowser(
        ComboBox categoryCombo, ComboBox classCombo, ListBox vehicleListBox, TextBox searchBox,
        CheckBox hideArchivalCheck, Image miniPreview, Button addVehicleButton,
        VehicleCatalog db, MiniTextures minis, Action openTextureBase)
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


    // In the old starter's order: traction first, then the wagons, then the rest.
    private static readonly (string LocKey, Func<string?, bool> Match)[] CategoryDefs =
    {
        ("CatElectricLoco", c => c == "e"),
        ("CatDieselLoco",   c => c == "s"),
        ("CatSteamLoco",    c => c == "p"),
        ("CatRailbus",      c => c == "a"),
        ("CatEMU",          c => c == "z"),
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
        ("CatWork",         c => c == "r"),
        ("CatDraisine",     c => c == "d"),
        ("CatTram",         c => c == "t"),
        ("CatCar",          c => c == "o"),
        ("CatBus",          c => c == "b"),
        ("CatTruck",        c => c == "c"),
        ("CatPeople",       c => c == "h"),
        ("CatAnimals",      c => c == "f"),
        ("CatPrototype",    c => c == "x"),

        ("CatOther",        IsOtherCat),
    };

    private static readonly HashSet<string> NamedLowerCats =
        new(StringComparer.Ordinal) { "a", "b", "c", "d", "e", "f", "h", "n", "o", "p", "r", "s", "t", "x", "z" };

    private static bool IsOtherCat(string? c) =>
        string.IsNullOrEmpty(c) ||
        (c is { Length: 1 } && char.IsLower(c[0]) && !NamedLowerCats.Contains(c));

    /// <summary>
    /// Only the categories something is listed in - with the archival ones hidden, the
    /// ones holding nothing else go too - keeping the pick and its class where they stay.
    /// </summary>
    public void PopulateCategoryCombo()
    {
        var keep = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag;
        string? keepClass = _classFilter;

        var present = _db.Textures
            .Where(Listable)
            .Select(VehicleInfo.CategoryOf)
            .Distinct()
            .ToList();

        _suppress = true;
        categoryCombo.Items.Clear();
        foreach (var (locKey, match) in CategoryDefs)
            if (present.Any(match))
                categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc[locKey], Tag = match });
        categoryCombo.SelectedItem = categoryCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, keep));
        _suppress = false;

        _categoryFilter = categoryCombo.SelectedItem is ComboBoxItem { Tag: Func<string?, bool> f }
            ? f : null;
        RebuildClassCombo(keepClass);
    }

    /// <summary>
    /// Refills the classes. Within a category one is always picked - the one kept, or
    /// else the first - so the list shows its vehicles straight away, as the old starter
    /// did, instead of every vehicle of the category at once.
    /// </summary>
    private void RebuildClassCombo(string? keepClass = null)
    {
        _suppress = true;
        FillClassCombo();
        var classes = classCombo.Items.OfType<string>().ToList();
        classCombo.SelectedItem =
            classes.FirstOrDefault(cls => string.Equals(cls, keepClass, StringComparison.OrdinalIgnoreCase))
            ?? (_categoryFilter is not null ? classes.FirstOrDefault() : null);
        _suppress = false;

        _classFilter = classCombo.SelectedItem as string;
        Rebuild();
    }

    /// <summary>A texture the browser lists at all: no set follower, no hidden archival one.</summary>
    private bool Listable(VehicleTexture texture) =>
        !_db.IsSetFollower(texture) &&
        !(AppServices.Current.Settings.HideArchivalVehicles && texture.ResolvedArchived);

    /// <summary>
    /// The classes with their thumbnail above the name: in the open list at the size
    /// the consist cards use, called again when that size changes; in the closed box
    /// at a fixed modest one, so the class picked is seen, as in the old starter,
    /// without taking the height the vehicle list needs.
    /// </summary>
    public void InitClassComboTemplates()
    {
        int height = VehicleCardStyle.ThumbHeight;
        classCombo.ItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, height, stacked: true), false);
        classCombo.SelectionBoxItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, PickedClassThumbHeight, stacked: true), false);
    }

    private const int PickedClassThumbHeight = 30;

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

    private Control ClassComboContent(string cls, int height, bool stacked)
    {
        var cell = new StackPanel
        {
            Orientation = stacked ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = stacked ? 2 : 8,
            HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Left,
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
            HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return cell;
    }

    private IEnumerable<string> ClassesForCategory(Func<string?, bool>? category) =>
        _db.Textures
            .Where(Listable)
            .Where(t => category == null || category(VehicleInfo.CategoryOf(t)))
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
        {
            AddNote(App.Loc["BrowserHint"]);
            return;
        }

        var matched = _db.Textures
            .Where(t => PassesFilters(t, search, hasSearch))
            .OrderBy(t => Consist.Base(t.Skinfile), TextureOrder)
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
            AddNote(hasSearch ? string.Format(App.Loc["BrowserNoMatch"], search) : App.Loc["NoVehicles"]);
    }

    private void AddNote(string text) =>
        vehicleListBox.Items.Add(new ListBoxItem
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
            IsEnabled = false
        });

    public void VehicleListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (vehicleListBox.SelectedItem is ListBoxItem { Tag: VehicleTexture texture })
        {
            Selected = texture;
            miniPreview.Source = _minis.Get(_db.ResolveMiniName(texture), MiniPreviewHeight);
            addVehicleButton.IsEnabled = true;
            SyncCombosTo(texture);

            // Clicking a card in the consist also selects its texture here. The
            // detail panels then belong to that car, not to the catalogue entry.
            if (!_syncingCombos)
                TextureSelected?.Invoke(texture);
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
        if ((e.Source as Visual).FindAncestorOfType<ListBoxItem>(includeSelf: true) is not
            { Tag: VehicleTexture texture } hit)
            return;

        BuildBrowserMenu(texture, hit).Open(hit);
        e.Handled = true;
    }

    private ContextMenu BuildBrowserMenu(VehicleTexture texture, Control anchor)
    {
        var menu = new ContextMenu();

        var addMany = new MenuItem { Header = App.Loc["AddMany"], IsEnabled = AddMany is not null };
        addMany.Click += (_, _) =>
            AddManyFlyout.Show(anchor, PlacementMode.Right, count => AddMany?.Invoke(texture, count));
        menu.Items.Add(addMany);
        menu.Items.Add(new Separator());

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

    /// <summary>
    /// Fills a detail panel with what is known about a livery: the skin itself,
    /// then whatever its credit line carries.
    /// </summary>
    /// <remarks>
    /// Built from the same <see cref="DetailRows"/> as the general tab, so the
    /// labels line up in a column instead of each line being its own sentence.
    /// Fields the credit line leaves out are dropped rather than shown as
    /// blanks - most liveries fill in only a few.
    /// </remarks>
    public void ShowTextureInfo(VehicleTexture texture, StackPanel target)
    {
        target.Children.Clear();

        var tooltip = new List<string>();

        void Reading(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            target.Children.Add(DetailRows.Reading(label, value));
            tooltip.Add($"{label}: {value}");
        }

        Reading(App.Loc["TextureInfo"], Consist.Base(texture.Skinfile));
        Reading(App.Loc["Model"], texture.Directory + texture.Model);

        if (texture.Meta is { } meta)
        {
            Reading(App.Loc["TexOperator"], meta.Operator);
            Reading(App.Loc["TexStation"], meta.Depot);
            Reading(App.Loc["TexRevision"], meta.RevisionDate);
            Reading(App.Loc["TexWorks"], meta.RevisionPlace);
            Reading(App.Loc["TexAuthor"], meta.TextureAuthor);
            Reading(App.Loc["TexPhoto"], meta.PhotoAuthor);
        }

        ToolTip.SetTip(target, string.Join("\n", tooltip));
    }

    public List<VehicleTexture> CurrentTextures()
    {
        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        if (_categoryFilter == null && _classFilter == null && !hasSearch)
            return new List<VehicleTexture>();

        return _db.Textures
            .Where(t => PassesFilters(t, search, hasSearch))
            .OrderBy(t => Consist.Base(t.Skinfile), TextureOrder)
            .ToList();
    }

    private bool PassesFilters(VehicleTexture t, string search, bool hasSearch)
    {
        if (!Listable(t))
            return false;

        // A search looks through everything; the category and the class narrow the
        // browsing, not the finding.
        if (hasSearch)
            return Matches(t, search);

        if (_categoryFilter != null && !_categoryFilter(VehicleInfo.CategoryOf(t)))
            return false;

        return _classFilter == null ||
               string.Equals(VehicleInfo.ClassOf(t), _classFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool Matches(VehicleTexture t, string f)
    {
        bool C(string? s) => !string.IsNullOrEmpty(s) &&
                             s.Contains(f, StringComparison.OrdinalIgnoreCase);
        return C(t.Skinfile) || C(t.Model) || C(t.TextureMini) || C(t.MiniRef)
               || C(t.Meta?.Vehicle) || C(t.Meta?.Operator) || C(VehicleInfo.ClassOf(t));
    }

    // The old starter listed textures by file name and left the ordering to
    // Delphi's Sorted, which is AnsiCompareText: case-insensitive and locale
    // aware. Ordinal comparison puts digits, underscores and accented letters
    // somewhere else entirely, which is why the list looked reshuffled.
    private static readonly StringComparer TextureOrder = StringComparer.CurrentCultureIgnoreCase;

    // The texture's own file name, as the old starter listed it and as the detail
    // panel names it. The thumbnail name went here before, which meant the list
    // called a vehicle something none of the other panels did.
    private static string BrowserName(VehicleTexture texture) => Consist.Base(texture.Skinfile);

    // The file name alone, as the old starter listed it - the operator is in the
    // texture panel - with a faint count for a set of cars.
    private static Control BrowserLabel(VehicleTexture texture, IReadOnlyList<VehicleTexture>? set)
    {
        var name = new TextBlock { Text = BrowserName(texture) };
        if (set is not { Count: > 1 })
            return name;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { name, new TextBlock { Text = $"×{set.Count}", Opacity = 0.55 } }
        };
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
        AppServices.Current.Settings.HideArchivalVehicles = hideArchivalCheck.IsChecked ?? true;
        AppServices.Current.SettingsStore.Save();
        PopulateCategoryCombo();
    }

    public void SelectInBrowser(Dynamic car)
    {
        var texture = _db.TextureFor(car.DataFolder, car.SkinFile, car.MmdFile);
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
                string.Equals(t.Model, texture.Model, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Directory, texture.Directory, StringComparison.OrdinalIgnoreCase))
            {
                vehicleListBox.SelectedItem = entry;
                entry.BringIntoView();
                break;
            }
    }
}
