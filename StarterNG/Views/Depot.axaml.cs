using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace StarterNG.Views;

public partial class Depot : UserControl
{

    private readonly VehicleDatabase _db = GameData.Instance.Vehicles;
    private readonly List<Scenery> _sceneries = GameData.Instance.Sceneries;

    private readonly List<ConsistItem> _consist = new();

    private Dynamic? _selectedCar;
    private Dynamic? _pressedCar;
    private readonly List<(Border Frame, ConsistItem Item, Dynamic Car)> _memberChrome = new();
    private readonly List<(Button Badge, ConsistItem Item)> _driverChrome = new();
    private ConsistItem? _selected;

    private Trainset? _editingTrainset;

    private readonly Infrastructure.DropGap _dropGap = new();
    private readonly Infrastructure.EdgeScroller _edgeScroller = new();
    private double _dragCardWidth = 64;

    private readonly Infrastructure.DropIndexTracker _dropTracker = new();

    private int _pressCardIndex = -1;
    private Point _pressPoint;
    private bool _cardDragging;
    private double _grabOffset;
    private Control? _carried;

    private readonly Dictionary<string, Bitmap?> _miniCache = new();
    private int ConsistThumbHeight =>
        StarterNG.Classes.Settings.Instance.LargeThumbnails ? 68 : 34;

    private const int MiniPreviewHeight = 54;

    private const int CompactThumbHeight = 24;

    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private int _nameCounter;

    private static readonly Random _rng = new();

    private bool _suppress;
    private bool _syncingCombos;

    private DispatcherTimer? _searchTimer;

    private Scenery? _listScenery;

    private Func<string?, bool>? _categoryFilter;
    private string? _classFilter;

    private VehicleTexture? _browserSelected;

    private VehicleTexture? _dragTexture;
    private int _dragConsistIndex = -1;
    private const string DragVehicleFormat = "starterng/vehicle";
    private const string DragConsistFormat = "starterng/consist-index";

    private PointerPressedEventArgs? _pendingDragPress;
    private Point _pendingDragOrigin;
    private VehicleTexture? _pendingDragTexture;
    private int _pendingDragConsistIndex = -1;
    private ConsistItem? _pendingBrowserSync;
    private bool _dragSessionActive;
    private const double DragThreshold = 6;

    private const int MaxListRows = 2000;

    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#33888888"));
    private static readonly IBrush CardBorderSel = new SolidColorBrush(Color.Parse("#AA41C400"));
    private static readonly IBrush CardBgSel = new SolidColorBrush(Color.Parse("#2241C400"));
    private static readonly IBrush Placeholder = new SolidColorBrush(Color.Parse("#22808080"));

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
        c is { Length: 1 } && char.IsLower(c[0]) && !NamedLowerCats.Contains(c);

    private static readonly string[] CarSuffixes = { "sa", "sb", "ra", "rb", "s" };

    private static readonly string[] CouplingBitKeys =
        { "CplMechanical", "CplBrake", "CplControl", "CplHighVoltage",
          "CplGangway", "CplAuxPneumatic", "CplHeating", "CplWorkshop" };

    public Depot()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            BuildList();
        };

        Dispatcher.UIThread.Post(() =>
        {
            hideArchivalCheck.IsChecked = StarterNG.Classes.Settings.Instance.HideArchivalVehicles;
            InitClassComboTemplates();
            PopulateCategoryCombo();
            PopulateSceneryConsists();
            PopulateWarehouse();
            RebuildConsist();
        }, DispatcherPriority.Background);

        consistStack.PointerMoved += Strip_OnPointerMoved;
        consistStack.PointerReleased += Strip_OnPointerReleased;
        consistStack.PointerCaptureLost += (_, _) => EndCardDrag();

        DragDrop.SetAllowDrop(consistDropTarget, true);
        consistDropTarget.AddHandler(DragDrop.DragOverEvent, Consist_OnDragOver);
        consistDropTarget.AddHandler(DragDrop.DropEvent, Consist_OnDrop);
        consistDropTarget.AddHandler(DragDrop.DragLeaveEvent, (_, _) => CloseDropGap());

        vehicleListBox.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        vehicleListBox.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);

        AttachedToVisualTree += (_, _) => SyncFromSelection();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                SyncFromSelection();
                PopulateWarehouse();
            }
        };
    }

    private void SyncFromSelection()
    {

        bool hideArchival = StarterNG.Classes.Settings.Instance.HideArchivalVehicles;
        if (hideArchivalCheck.IsChecked != hideArchival)
        {
            _suppress = true;
            hideArchivalCheck.IsChecked = hideArchival;
            _suppress = false;
            BuildList();
        }

        PopulateSceneryConsists();

        var trainset = AppState.Instance.CurrentTrainset;
        if (trainset != null && !ReferenceEquals(trainset, _editingTrainset))
            LoadTrainset(trainset);
        else if (_editingTrainset != null)
            RebuildConsist();
    }

    private void LoadTrainset(Trainset trainset)
    {
        _editingTrainset = trainset;
        _consist.Clear();

        var cars = trainset.Vehicles.Select(v =>
        {
            var c = v.Clone();
            c.MiniName = _db.MiniForSkin(c.SkinFile) ?? c.MiniName;
            return c;
        }).ToList();

        int i = 0;
        while (i < cars.Count)
        {
            if (HoldsNext(cars[i]))
            {
                var locked = new List<Dynamic> { cars[i] };
                int j = i;
                while (j < cars.Count - 1 && HoldsNext(cars[j]))
                    locked.Add(cars[++j]);

                _consist.Add(new ConsistItem
                {
                    Cars = locked,
                    Grouped = true,
                    Flipped = locked[0].Offset < 0,
                    Driver = UnitDriver(locked)
                });
                i = j + 1;
                continue;
            }

            VehicleSet? set = null;
            if (_db.TextureForSkin(cars[i].SkinFile)?.Uuid is { } uuid)
                _db.SetByTextureUuid.TryGetValue(uuid, out set);

            if (set?.TextureRefs is { Count: > 1 })
            {
                var members = new HashSet<string>(set.TextureRefs.Where(r => !string.IsNullOrEmpty(r)));
                var group = new List<Dynamic>();

                int j = i;
                while (j < cars.Count && group.Count < set.TextureRefs.Count)
                {
                    if (_db.TextureForSkin(cars[j].SkinFile)?.Uuid is { } u
                        && members.Contains(u))
                        group.Add(cars[j++]);
                    else
                        break;
                }

                if (group.Count >= 2)
                {
                    _consist.Add(new ConsistItem
                    {
                        Cars = group,
                        Grouped = true,
                        Flipped = group[0].Offset < 0,
                        Driver = UnitDriver(group)
                    });
                    i = j;
                    continue;
                }
            }

            if (IsUnitCar(cars[i]))
            {
                string key = UnitKey(cars[i]);
                var group = new List<Dynamic> { cars[i] };
                int j = i + 1;
                while (j < cars.Count && IsUnitCar(cars[j]) && UnitKey(cars[j]) == key)
                    group.Add(cars[j++]);

                if (group.Count >= 2)
                {
                    _consist.Add(new ConsistItem
                    {
                        Cars = group,
                        Grouped = true,
                        Flipped = group[0].Offset < 0,
                        Driver = UnitDriver(group)
                    });
                    i = j;
                    continue;
                }
            }

            _consist.Add(new ConsistItem
            {
                Cars = new List<Dynamic> { cars[i] },
                Grouped = false,
                Flipped = cars[i].Offset < 0,
                Driver = cars[i].DriverType
            });
            i++;
        }

        _selected = _consist.FirstOrDefault();
        SyncStartingVehicle();
        RebuildConsist();
    }

    private static eDriverType UnitDriver(IReadOnlyList<Dynamic> group) =>
        group.Select(c => c.DriverType)
            .Where(d => d != eDriverType.Nobody)
            .DefaultIfEmpty(eDriverType.Nobody)
            .First();

    private void SyncStartingVehicle()
    {
        if (_selected is not { Cars.Count: > 0 } item)
            return;

        var driven = item.Cars.FirstOrDefault(c =>
            c.DriverType is eDriverType.Headdriver or eDriverType.Reardriver);
        AppState.Instance.StartingVehicleName = (driven ?? item.Cars[0]).Name;
    }

    private static string StripCarSuffix(string name)
    {
        foreach (string suf in CarSuffixes)
        {
            if (name.Length > suf.Length &&
                name.EndsWith(suf, StringComparison.OrdinalIgnoreCase) &&
                char.IsDigit(name[name.Length - suf.Length - 1]))
                return name[..^suf.Length];
        }
        return name;
    }

    private static string Base(string skinOrName) => Path.GetFileNameWithoutExtension(skinOrName);

    private static string UnitKey(Dynamic d) => StripCarSuffix(Base(d.SkinFile));

    private static bool IsUnitCar(Dynamic d) => UnitKey(d) != Base(d.SkinFile);

    private static string ClassOf(VehicleTexture t) => t.ResolvedClass;

    private static string? CategoryOf(VehicleTexture t) => t.ResolvedCategory;

    private void PopulateCategoryCombo()
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
        BuildList();
    }

    private void InitClassComboTemplates()
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
        _db.Textures.FirstOrDefault(t => string.Equals(ClassOf(t), cls, StringComparison.OrdinalIgnoreCase))
            is { } t ? CategoryOf(t) : null;

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
        var bmp = GetMiniBitmap(cls, height);
        if (bmp != null)
            cell.Children.Add(new Image
            {
                Source = bmp, Height = height,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
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
            .Where(t => category == null || category(CategoryOf(t)))
            .Where(t => !_db.IsSetFollower(t))
            .Where(t => !StarterNG.Classes.Settings.Instance.HideArchivalVehicles || !t.ResolvedArchived)
            .Select(ClassOf)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

    private void CategoryCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _categoryFilter = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as Func<string?, bool>;
        RebuildClassCombo();
    }

    private void ClassCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        string? cls = classCombo.SelectedItem as string;
        _classFilter = cls;

        if (cls != null && _categoryFilter == null &&
            PostSyncing(() => { ApplyFilters(CategoryOfClass(cls), cls); BuildList(); }))
            return;

        BuildList();
    }

    private void Combo_OnPointerWheel(object? sender, PointerWheelEventArgs e)
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

    private void BuildList()
    {

        _browserSelected = null;
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
            row.ContextMenu = BuildBrowserMenu(texture);
            vehicleListBox.Items.Add(row);
        }

        if (vehicleListBox.Items.Count == 0)
            vehicleListBox.Items.Add(new ListBoxItem
            {
                Content = App.Loc["NoVehicles"],
                IsEnabled = false
            });
    }

    private void VehicleListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (vehicleListBox.SelectedItem is ListBoxItem { Tag: VehicleTexture texture })
        {
            _browserSelected = texture;
            miniPreview.Source = GetMiniBitmap(_db.ResolveMiniName(texture), MiniPreviewHeight);
            addVehicleButton.IsEnabled = true;
            SyncCombosTo(texture);
        }
        else
        {
            _browserSelected = null;
            miniPreview.Source = null;
            addVehicleButton.IsEnabled = false;
        }
    }

    private ContextMenu BuildBrowserMenu(VehicleTexture texture)
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = App.Loc["CopyTextureName"] };
        copy.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
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
        baseItem.Click += (_, _) => TextureBaseButton_OnClick(null, null!);
        menu.Items.Add(baseItem);

        return menu;
    }

    private void ShowTextureInfo(VehicleTexture texture, StackPanel target)
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
                FontSize = 10,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Row(App.Loc["TextureInfo"], Base(texture.Skinfile));
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

    private void VehicleListBox_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_browserSelected != null)
            ReplaceSelected(_browserSelected);
    }

    private void AddVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_browserSelected != null)
            AddTexture(_browserSelected);
    }

    public void ToolAddAllCategory() => AddAllCategoryButton_OnClick(null, null!);
    public void ToolAddUniqueMmd() => AddUniqueMmdButton_OnClick(null, null!);
    public void ToolRemoveAllTrains() => RemoveAllTrainsButton_OnClick(null, null!);

    private async void AddAllCategoryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var all = CurrentBrowserTextures();
        if (all.Count == 0)
        {
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["PickCategoryFirst"]);
            return;
        }

        foreach (var t in all)
            InsertTextureAt(t, _consist.Count);
    }

    private async void AddUniqueMmdButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var all = CurrentBrowserTextures();
        if (all.Count == 0)
        {
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["PickCategoryFirst"]);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all)
        {
            foreach (string model in ModelsOf(t))
            {
                if (!seen.Add(model)) continue;
                InsertTextureAt(t, _consist.Count);

                var added = _consist[^1];
                foreach (var car in added.Cars)
                    car.MmdFile = model;
            }
        }
    }

    private static IEnumerable<string> ModelsOf(VehicleTexture t)
    {
        string first = string.IsNullOrEmpty(t.Model) ? t.Skinfile : t.Model!;
        if (!string.IsNullOrEmpty(first))
            yield return first;

        foreach (var alias in t.Aliases)
            if (!string.IsNullOrEmpty(alias.Model))
                yield return alias.Model!;
    }

    private async void RemoveAllTrainsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_listScenery is null || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        if (!await MessageBox.Show(owner, App.Loc["RemoveAllTrainsConfirm"],
                                   App.Loc["RemoveAllTrains"], MessageBoxButtons.YesNo))
            return;

        foreach (var ts in _listScenery.Trainsets)
            ts.Vehicles = new List<Dynamic>();

        if (_editingTrainset != null)
            LoadTrainset(_editingTrainset);
        PopulateSceneryConsists();
        RebuildConsist();
    }

    private List<VehicleTexture> CurrentBrowserTextures()
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
        if (_categoryFilter != null && !_categoryFilter(CategoryOf(t)))
            return false;

        if (_classFilter != null &&
            !string.Equals(ClassOf(t), _classFilter, StringComparison.OrdinalIgnoreCase))
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
               || C(t.Meta?.Vehicle) || C(t.Meta?.Operator) || C(ClassOf(t));
    }

    private void ReplaceSelected(VehicleTexture texture)
    {
        if (_selected is null)
        {
            AddTexture(texture);
            return;
        }
        int i = _consist.IndexOf(_selected);
        if (i < 0)
        {
            AddTexture(texture);
            return;
        }

        var set = _db.ResolveSet(texture);
        var unit = set ?? new List<VehicleTexture> { texture };
        var item = new ConsistItem
        {
            Cars = unit.Select(MakeDynamic).ToList(),
            Grouped = unit.Count > 1,
            Driver = _selected.Driver,
            Flipped = _selected.Flipped
        };
        _consist[i] = item;
        _selected = item;
        AutoConnectAll();
        RebuildConsist();
    }

    private static string BrowserName(VehicleTexture texture)
    {
        if (!string.IsNullOrEmpty(texture.TextureMini) &&
            !string.Equals(texture.TextureMini, texture.ResolvedClass, StringComparison.OrdinalIgnoreCase))
            return texture.TextureMini!;
        return Base(texture.Skinfile);
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

    private void AddTexture(VehicleTexture texture)
    {
        int at = _consist.Count;
        if (_selected != null)
        {
            int si = _consist.IndexOf(_selected);
            if (si >= 0) at = si + 1;
        }
        InsertTextureAt(texture, at);
    }

    private void InsertTextureAt(VehicleTexture texture, int at)
    {
        var set = _db.ResolveSet(texture);
        var unit = set ?? new List<VehicleTexture> { texture };

        var item = new ConsistItem
        {
            Cars = unit.Select(MakeDynamic).ToList(),
            Grouped = unit.Count > 1,
            Driver = eDriverType.Nobody,
            Flipped = false
        };

        at = Math.Clamp(at, 0, _consist.Count);
        MatchOccupancy(item, at);
        _consist.Insert(at, item);
        _selected = item;
        AutoConnectAll();
        RebuildConsist();
    }

    private void MatchOccupancy(ConsistItem item, int position)
    {
        item.Driver = eDriverType.Nobody;
        var lead = item.Cars.FirstOrDefault();
        if (lead is null) return;

        foreach (var car in item.Cars)
            car.DriverType = eDriverType.Nobody;

        var tex = _db.TextureForSkin(lead.SkinFile);
        string? cat = tex != null ? CategoryOf(tex) : null;
        if (!IsPoweredCategory(cat))
            return;

        bool staffed = _consist.Any(i =>
            i.Driver is eDriverType.Headdriver or eDriverType.Reardriver);
        if (position == 0 || !staffed)
        {
            item.Driver = eDriverType.Headdriver;
            lead.DriverType = eDriverType.Headdriver;
        }
    }

    private static bool IsPoweredCategory(string? c) =>
        c is "e" or "s" or "p" or "z" or "a";

    public void RebuildAfterLanguageChange()
    {
        PopulateCategoryCombo();
        PopulateSceneryConsists();
        RebuildConsist();
        UpdateDetails();
    }

    private void PopulateSceneryConsists()
    {
        _suppress = true;
        sceneryConsistList.Items.Clear();

        depotShowAiCheck.IsChecked = StarterNG.Classes.Settings.Instance.ShowAiVehicles;
        depotDrivableOnlyCheck.IsChecked = StarterNG.Classes.Settings.Instance.DrivableOnly;

        _listScenery = AppState.Instance.CurrentScenery ?? _sceneries.FirstOrDefault();
        mapConsistsPanel.Header = _listScenery != null
            ? $"{App.Loc["MapConsists"]}: {Path.GetFileNameWithoutExtension(_listScenery.Path)}"
            : App.Loc["NoSceneryConsists"];

        if (_listScenery != null)
        {
            bool showAi = StarterNG.Classes.Settings.Instance.ShowAiVehicles;
            bool drivableOnly = StarterNG.Classes.Settings.Instance.DrivableOnly;

            foreach (var trainset in _listScenery.Trainsets)
            {
                if (IsAiTrainset(trainset) && !showAi) continue;
                if (drivableOnly && !UData.StaffedTrain(trainset)) continue;

                var entry = new ListBoxItem
                {
                    Content = ConsistListContent(trainset),
                    Tag = trainset
                };

                var ts = trainset;
                entry.ContextMenu = ConsistContextMenu.Build(
                    entry,
                    () => ts,
                    vehicles => ApplyConsist(ts, vehicles),
                    () => RemoveAllVehicles(ts),
                    randomOrder: ToolRandomOrder,
                    randomTurn: ToolRandomOrientation);
                sceneryConsistList.Items.Add(entry);

                if (ReferenceEquals(trainset, AppState.Instance.CurrentTrainset))
                    sceneryConsistList.SelectedItem = entry;
            }
        }
        _suppress = false;
    }

    private void SceneryConsistList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (sceneryConsistList.SelectedItem is not ListBoxItem { Tag: Trainset trainset })
            return;

        AppState.Instance.CurrentScenery = _listScenery;
        AppState.Instance.CurrentTrainset = trainset;
        AppState.Instance.StartingVehicleName = TrainsetDisplay.DefaultStartingVehicle(trainset);
        LoadTrainset(trainset);
    }

    private static bool IsAiTrainset(Trainset trainset) =>
        !string.IsNullOrEmpty(trainset.Description) &&
        trainset.Description.TrimStart().StartsWith("-", StringComparison.Ordinal);

    private void SceneryConsistList_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            sceneryConsistList.SelectedItem is ListBoxItem { Tag: Trainset trainset } &&
            trainset.Vehicles.Count > 0)
        {
            RemoveAllVehicles(trainset);
            e.Handled = true;
        }
    }

    private void ApplyConsist(Trainset target, List<Dynamic> vehicles)
    {
        target.Vehicles = vehicles;
        AppState.Instance.CurrentScenery = _listScenery;
        AppState.Instance.CurrentTrainset = target;
        LoadTrainset(target);
        PopulateSceneryConsists();
    }

    private void PopulateWarehouse()
    {
        string? current = (warehouseList.SelectedItem as ListBoxItem)?.Tag as string;
        warehouseList.Items.Clear();

        foreach (var preset in PresetStore.All())
        {
            var entry = new ListBoxItem
            {
                Tag = preset.Name,
                Content = new TextBlock
                {
                    Text = preset.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            warehouseList.Items.Add(entry);
            if (current is not null && string.Equals(current, preset.Name, StringComparison.Ordinal))
                warehouseList.SelectedItem = entry;
        }

        warehouseList.ContextMenu ??= BuildWarehouseMenu();
        removeFromWarehouseButton.IsEnabled = warehouseList.Items.Count > 0;
    }

    private string? SelectedPresetName() =>
        (warehouseList.SelectedItem as ListBoxItem)?.Tag as string;

    private ContextMenu BuildWarehouseMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            bool hasPreset = SelectedPresetName() != null;
            bool hasTarget = _editingTrainset != null;

            void Add(string key, Action run, bool enabled = true)
            {
                var mi = new MenuItem { Header = App.Loc[key], IsEnabled = enabled };
                mi.Click += (_, _) => run();
                menu.Items.Add(mi);
            }

            Add("WarehouseApply", ApplySelectedPreset, hasPreset && hasTarget);
            Add("WarehouseAppend", () => AppendWarehouseButton_OnClick(null, null!), hasPreset && hasTarget);
            Add("ChangeName", () => RenameWarehouseButton_OnClick(null, null!), hasPreset);

            menu.Items.Add(new Separator());
            Add("PresetImportMagazyn", () => ImportMagazynButton_OnClick(null, null!));
            Add("WarehouseImportFile", () => ImportFileButton_OnClick(null, null!));

            menu.Items.Add(new Separator());
            var sort = new MenuItem { Header = App.Loc["PresetSort"] };
            var byName = new MenuItem
            {
                Header = (PresetStore.SortMode == PresetSort.Name ? "\u25CF " : "\u25CB ") + App.Loc["PresetSortName"]
            };
            byName.Click += (_, _) => { PresetStore.SortMode = PresetSort.Name; PopulateWarehouse(); };
            var byTrack = new MenuItem
            {
                Header = (PresetStore.SortMode == PresetSort.Track ? "\u25CF " : "\u25CB ") + App.Loc["PresetSortTrack"]
            };
            byTrack.Click += (_, _) => { PresetStore.SortMode = PresetSort.Track; PopulateWarehouse(); };
            sort.Items.Add(byName);
            sort.Items.Add(byTrack);
            menu.Items.Add(sort);
        };
        return menu;
    }

    private async void AddToWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_editingTrainset is not { } trainset)
            return;

        string name = trainset.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = App.Loc["PresetEmpty"];

        try
        {
            PresetStore.Save(name, ConsistText.Serialize(trainset));
        }
        catch (Exception ex)
        {
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(PresetStore.FilePath, ex);
            return;
        }
        PopulateWarehouse();
    }

    private void WarehouseList_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) =>
        ApplySelectedPreset();

    private void ApplySelectedPreset()
    {
        if (SelectedPresetName() is not { } name || _editingTrainset is not { } target)
            return;

        var preset = PresetStore.All().FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
        if (preset is null)
            return;

        var vehicles = ConsistText.VehiclesFrom(preset.Entry);
        if (vehicles != null)
            ApplyConsist(target, vehicles);
    }

    private void RenameWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedPresetName() is not { } name)
            return;

        var preset = PresetStore.All().FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
        if (preset is null)
            return;

        var nameBox = new TextBox { Text = name, MinWidth = 240 };
        var ok = new Button { Content = App.Loc["Ok"], Cursor = _hand };
        ok.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["PresetNamePrompt"],
            FontWeight = FontWeight.Bold,
            FontSize = 12
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            string? fresh = nameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(fresh) && !string.Equals(fresh, name, StringComparison.Ordinal))
            {
                PresetStore.Save(fresh, preset.Entry);
                PresetStore.Delete(name);
                PopulateWarehouse();
            }
            flyout.Hide();
        };
        flyout.ShowAt(warehouseList);
        nameBox.Focus();
    }

    private void AppendWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedPresetName() is not { } name || _editingTrainset is not { } target)
            return;

        var preset = PresetStore.All().FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
        var vehicles = ConsistText.VehiclesFrom(preset?.Entry);
        if (vehicles is null || vehicles.Count == 0)
            return;

        var merged = new List<Dynamic>(target.Vehicles);
        merged.AddRange(vehicles);
        ApplyConsist(target, merged);
    }

    private async void ImportFileButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;

        var picked = await top.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = App.Loc["WarehouseImportFile"],
                AllowMultiple = false
            });

        string? path = picked.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrEmpty(path))
            return;

        int added = PresetStore.ImportMagazyn(path);
        PopulateWarehouse();
        if (added == 0)
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["WarehouseImportNothing"]);
    }

    private async void RemoveFromWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedPresetName() is not { } name)
            return;

        try
        {
            PresetStore.Delete(name);
        }
        catch (Exception ex)
        {
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(PresetStore.FilePath, ex);
            return;
        }
        PopulateWarehouse();
    }

    private async void ImportMagazynButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        int added = PresetStore.ImportMagazyn();
        PopulateWarehouse();
        if (added == 0)
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["WarehouseImportNothing"]);
    }

    private void RemoveAllVehicles(Trainset target)
    {
        target.Vehicles = new List<Dynamic>();
        AppState.Instance.CurrentScenery = _listScenery;
        AppState.Instance.CurrentTrainset = target;
        LoadTrainset(target);
        PopulateSceneryConsists();
    }

    private static Control ConsistListContent(Trainset trainset) =>
        new TextBlock
        {
            Text = ConsistLabel(trainset),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

    private static string ConsistLabel(Trainset trainset)
    {
        string? text = TrainsetDisplay.CompositionText(trainset);
        if (text != null)
            return text;
        return string.Format(App.Loc["EmptyTrainset"], trainset.Track ?? "");
    }

    private void RefreshConsistListLabels()
    {
        foreach (var obj in sceneryConsistList.Items)
        {
            if (obj is ListBoxItem { Tag: Trainset ts } item)
                item.Content = ConsistListContent(ts);
        }
    }

    private Dynamic MakeDynamic(VehicleTexture texture)
    {
        string skin = Base(texture.Skinfile);
        return new Dynamic
        {
            RangeMax = -1,
            RangeMin = 0,
            Name = MakeUniqueName(skin),
            DataFolder = StripDynamicPrefix(texture.Directory),
            SkinFile = skin,
            MmdFile = string.IsNullOrEmpty(texture.Model) ? skin : texture.Model!,
            Offset = 0f,
            DriverType = eDriverType.Nobody,
            Coupling = new Coupling { Flags = Coupling.Mechanical | Coupling.BrakePipe },
            HasVelocity = true,
            MiniName = _db.ResolveMiniName(texture)
        };
    }

    private string MakeUniqueName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "vehicle";
        baseName = baseName.Replace(' ', '_');

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _consist)
            foreach (var c in item.Cars)
                if (!string.IsNullOrEmpty(c.Name))
                    used.Add(c.Name!);

        var scn = _listScenery ?? AppState.Instance.CurrentScenery;
        if (scn != null)
        {
            foreach (string n in scn.LooseVehicleNames)
                used.Add(n);
            foreach (var ts in scn.Trainsets)
            {
                if (ReferenceEquals(ts, _editingTrainset)) continue;
                foreach (var v in ts.Vehicles)
                    if (!string.IsNullOrEmpty(v.Name))
                        used.Add(v.Name!);
            }
        }

        string candidate;
        do
            candidate = $"{baseName}_{++_nameCounter}";
        while (used.Contains(candidate));
        return candidate;
    }

    private static string StripDynamicPrefix(string directory)
    {
        string d = directory.Replace('\\', '/').TrimEnd('/');
        const string prefix = "dynamic/";
        if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            d = d[prefix.Length..];
        return d;
    }

    private void MoveLeft(ConsistItem item)
    {
        int i = _consist.IndexOf(item);
        if (i > 0)
        {
            (_consist[i - 1], _consist[i]) = (_consist[i], _consist[i - 1]);
            RebuildConsist();
        }
    }

    private void MoveRight(ConsistItem item)
    {
        int i = _consist.IndexOf(item);
        if (i >= 0 && i < _consist.Count - 1)
        {
            (_consist[i + 1], _consist[i]) = (_consist[i], _consist[i + 1]);
            RebuildConsist();
        }
    }

    private void RemoveItem(ConsistItem item)
    {
        int gap = _consist.IndexOf(item);
        _consist.Remove(item);

        if (ReferenceEquals(_selected, item))
            _selected = _consist.Count == 0
                ? null
                : _consist[Math.Clamp(gap, 0, _consist.Count - 1)];

        RebuildConsist();
    }

    private void FlipItem(ConsistItem item)
    {
        int index = _consist.IndexOf(item);
        if (index < 0) return;

        int first = index;
        while (first > 0 && HoldsTail(_consist[first - 1]))
            first--;

        int last = index;
        while (last + 1 < _consist.Count && HoldsTail(_consist[last]))
            last++;

        for (int k = first; k <= last; k++)
        {
            var card = _consist[k];
            card.Flipped = !card.Flipped;
            foreach (var car in card.Cars)
                car.Offset = car.Offset >= 0 ? -1f : 0f;
        }

        if (last > first)
            _consist.Reverse(first, last - first + 1);

        AutoConnectAll();
        RebuildConsist();
    }

    private void CycleDriver(ConsistItem item)
    {
        var car = ActiveCar(item);
        car.DriverType = car.DriverType switch
        {
            eDriverType.Nobody => eDriverType.Headdriver,
            eDriverType.Headdriver => eDriverType.Reardriver,
            eDriverType.Reardriver => eDriverType.Passenger,
            _ => eDriverType.Nobody
        };
        item.Driver = UnitDriver(item.Cars);
        if (ReferenceEquals(_selected, item))
            SyncStartingVehicle();
        RebuildConsist();
        AppState.Instance.NotifyChanged();
    }

    private void SplitItem(ConsistItem item)
    {
        int i = _consist.IndexOf(item);
        if (i < 0) return;

        var order = item.Flipped
            ? Enumerable.Reverse(item.Cars).ToList()
            : item.Cars;

        _consist.RemoveAt(i);
        for (int c = 0; c < order.Count; c++)
        {
            _consist.Insert(i + c, new ConsistItem
            {
                Cars = new List<Dynamic> { order[c] },
                Grouped = false,
                Flipped = item.Flipped,
                Driver = order[c].DriverType
            });
        }
        _selected = _consist[i];
        RebuildConsist();
    }

    private static bool HoldsNext(Dynamic car) =>
        car.Coupling.Has(Coupling.WorkshopLock) || car.Coupling.Locked;

    private void JoinItem(ConsistItem item)
    {
        int index = _consist.IndexOf(item);
        if (index < 0) return;

        int first = index;
        while (first > 0 && HoldsTail(_consist[first - 1]))
            first--;

        int last = index;

        bool inUnit = first < index || HoldsTail(_consist[index]);
        if (!inUnit && last + 1 < _consist.Count)
        {
            var card = _consist[last];
            if (card.Cars.Count > 0)
                TailCar(card).Coupling.Set(Coupling.WorkshopLock, true);
            last++;
        }

        while (last + 1 < _consist.Count && HoldsTail(_consist[last]))
            last++;

        if (last == first) return;

        var cars = new List<Dynamic>();
        var driver = eDriverType.Nobody;
        for (int k = first; k <= last; k++)
        {
            cars.AddRange(_consist[k].Cars);
            if (driver == eDriverType.Nobody)
                driver = _consist[k].Driver;
        }

        bool flipped = _consist[first].Flipped;
        if (flipped)
            cars.Reverse();

        var joined = new ConsistItem
        {
            Cars = cars,
            Grouped = true,
            Flipped = flipped,
            Driver = driver
        };

        _consist.RemoveRange(first, last - first + 1);
        _consist.Insert(first, joined);
        _selected = joined;

        AutoConnectAll();
        RebuildConsist();
    }

    private Dynamic ActiveCar(ConsistItem item) =>
        _selectedCar != null && item.Cars.Contains(_selectedCar) ? _selectedCar : item.Cars[0];

    private IEnumerable<Dynamic> UnitTargets(ConsistItem item) => item.Cars;

    private IEnumerable<Dynamic> MemberTargets(ConsistItem item) =>
        item.Cars.Count == 1 ? item.Cars : new[] { ActiveCar(item) };

    private static Dynamic TailCar(ConsistItem item) =>
        item.Flipped ? item.Cars[0] : item.Cars[^1];

    private static Dynamic HeadCar(ConsistItem item) =>
        item.Flipped ? item.Cars[^1] : item.Cars[0];

    private static bool HoldsTail(ConsistItem item) =>
        item.Cars.Count > 0 && HoldsNext(TailCar(item));

    private bool CanFormUnit(ConsistItem left, ConsistItem right)
    {
        if (left.Cars.Count == 0 || right.Cars.Count == 0)
            return false;

        var tail = TailCar(left);
        var head = HeadCar(right);

        var lp = PhysicsFor(tail);
        var rp = PhysicsFor(head);
        int leftMax = lp == null ? 3 : (left.Flipped ? lp.AllowedFlagA : lp.AllowedFlagB);
        int rightMax = rp == null ? 3 : (right.Flipped ? rp.AllowedFlagB : rp.AllowedFlagA);

        return (leftMax & rightMax & Coupling.WorkshopLock) != 0 || SameSet(tail, head);
    }

    private bool SameSet(Dynamic a, Dynamic b)
    {
        if (IsUnitCar(a) && IsUnitCar(b) && UnitKey(a) == UnitKey(b))
            return true;

        string? ua = _db.TextureForSkin(a.SkinFile)?.Uuid;
        string? ub = _db.TextureForSkin(b.SkinFile)?.Uuid;
        if (string.IsNullOrEmpty(ua) || string.IsNullOrEmpty(ub))
            return false;

        return _db.SetByTextureUuid.TryGetValue(ua!, out var sa) &&
               _db.SetByTextureUuid.TryGetValue(ub!, out var sb) &&
               ReferenceEquals(sa, sb) && sa.TextureRefs.Count > 1;
    }

    private void RebuildConsist()
    {
        consistStack.Children.Clear();
        _memberChrome.Clear();
        _driverChrome.Clear();
        _dropGap.Forget();
        _dropTracker.Reset();
        for (int i = 0; i < _consist.Count; i++)
        {
            var item = _consist[i];
            consistStack.Children.Add(BuildCard(item, i));

            if (i < _consist.Count - 1)
                consistStack.Children.Add(BuildCoupler(item));
            else
                consistStack.Children.Add(BuildCoupler(item, trailing: true));
        }

        emptyHint.IsVisible = _consist.Count == 0;
        UpdateDetails();
        UpdateTrainStats();
        WriteBackToScenery();
    }

    private void WriteBackToScenery()
    {
        if (_editingTrainset is null)
            return;

        var flat = new List<Dynamic>();
        foreach (var item in _consist)
        {
            var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse().ToList() : item.Cars;
            foreach (var car in cars)
                flat.Add(car);
        }
        _editingTrainset.Vehicles = flat;
        RefreshConsistListLabels();
    }

    private Physics? PhysicsFor(Dynamic car)
    {
        string? dbModel = _db.TextureForSkin(car.SkinFile)?.Model;
        return Physics.For(car.DataFolder, dbModel)
            ?? Physics.For(car.DataFolder, car.MmdFile)
            ?? Physics.For(car.DataFolder, car.SkinFile);
    }

    private void AutoConnectAll()
    {
        var flat = new List<(Dynamic car, bool flipped)>();
        foreach (var item in _consist)
        {
            var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse() : item.Cars;
            foreach (var c in cars)
                flat.Add((c, item.Flipped));
        }

        for (int i = 0; i < flat.Count - 1; i++)
        {
            var (left, lf) = flat[i];
            var (right, rf) = flat[i + 1];

            var lp = PhysicsFor(left);
            var rp = PhysicsFor(right);

            int leftMax = lp == null ? 3 : (lf ? lp.AllowedFlagA : lp.AllowedFlagB);
            int rightMax = rp == null ? 3 : (rf ? rp.AllowedFlagB : rp.AllowedFlagA);
            int common = leftMax & rightMax;

            string lct = lp == null ? "" : (lf ? lp.ControlTypeA : lp.ControlTypeB);
            string rct = rp == null ? "" : (rf ? rp.ControlTypeB : rp.ControlTypeA);
            if ((common & Coupling.ControlMU) != 0 &&
                !string.Equals(lct, rct, StringComparison.OrdinalIgnoreCase))
                common &= ~Coupling.ControlMU;

            left.Coupling.Flags = left.Coupling.Locked ? -common : common;
        }
    }

    private void UpdateTrainStats()
    {
        if (_editingTrainset is null)
        {
            trainStats.Children.Clear();
            return;
        }

        StarterNG.Infrastructure.StatsBar.Fill(trainStats, TrainsetDisplay.StatsFields(
            _editingTrainset,
            PhysicsFor,
            car => _db.TextureForSkin(car.SkinFile) is { } t ? CategoryOf(t) : null,
            LoadWeight));
    }

    private Control BuildCard(ConsistItem item, int index)
    {
        bool selected = ReferenceEquals(_selected, item);

        var minis = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse() : item.Cars;
        foreach (var car in cars)
        {
            var bmp = GetMiniBitmap(car.MiniName, ConsistThumbHeight, strict: true)
                      ?? GetMiniBitmap(car.SkinFile, ConsistThumbHeight, strict: true)
                      ?? GetMiniBitmap(car.MiniName, ConsistThumbHeight);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Height = ConsistThumbHeight, Stretch = Stretch.Uniform };
                if (item.Flipped)
                {
                    img.RenderTransform = new ScaleTransform(-1, 1);
                    img.RenderTransformOrigin = RelativePoint.Center;
                }

                minis.Children.Add(MemberFrame(img, item, car, CardCaption(item, car, index)));
            }
            else
            {
                minis.Children.Add(MemberFrame(new Border
                {
                    Height = ConsistThumbHeight, Width = 90, Background = Placeholder,
                    CornerRadius = new CornerRadius(4),
                    Child = new TextBlock
                    {
                        Text = car.SkinFile, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                }, item, car, CardCaption(item, car, index)));
            }
        }

        var nameBlock = new TextBlock
        {
            Text = item.Grouped && item.Cars.Count > 1 ? UnitLabel(item) : string.Empty,
            FontSize = 11,
            MaxWidth = Math.Max(120, item.Cars.Count * 96),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = item.Grouped && item.Cars.Count > 1
        };

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        controls.Children.Add(SmallButton("◀", App.Loc["TipMoveLeft"], () => MoveLeft(item)));
        controls.Children.Add(DriverButton(item));
        controls.Children.Add(SmallButton("⇄", App.Loc["TipFlip"], () => FlipItem(item)));
        controls.Children.Add(SmallButton("▶", App.Loc["TipMoveRight"], () => MoveRight(item)));

        if (item.Grouped && item.Cars.Count > 1)
            controls.Children.Add(SmallButton("⧉", App.Loc["TipSplit"], () => SplitItem(item)));
        else if ((index + 1 < _consist.Count &&
                  (HoldsTail(item) || CanFormUnit(item, _consist[index + 1]))) ||
                 (index > 0 && HoldsTail(_consist[index - 1])))
            controls.Children.Add(SmallButton("⛓", App.Loc["TipJoin"], () => JoinItem(item)));

        var remove = SmallButton("✕", App.Loc["RemoveSelectedVehicle"], () => RemoveItem(item));
        remove.Foreground = new SolidColorBrush(Color.Parse("#E05252"));
        remove.FontWeight = FontWeight.Bold;
        controls.Children.Add(remove);

        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(minis);
        inner.Children.Add(nameBlock);
        inner.Children.Add(controls);

        var card = new Border
        {
            Margin = new Thickness(2, 0),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? CardBorderSel : CardBorder,
            Background = selected ? CardBgSel : Brushes.Transparent,
            Cursor = _hand,
            Child = inner,
            Tag = item
        };
        card.PointerPressed += (_, e) =>
        {

            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;

            if (e.Source is Control src && src.GetVisualAncestors().OfType<Button>().Any())
                return;

            _selected = item;
            _selectedCar = _pressedCar ?? item.Cars[0];
            _pressedCar = null;
            SyncStartingVehicle();
            RefreshCardSelectionChrome();
            UpdateDetails();

            _pressCardIndex = index;
            _pressPoint = e.GetPosition(consistStack);
            _cardDragging = false;
        };
        card.ContextMenu = BuildCardMenu(card, item);
        return card;
    }

    private string CardCaption(ConsistItem item, Dynamic car, int index) =>
        TrainsetDisplay.VehicleLabel(car,
            head: index == 0 && ReferenceEquals(car, HeadCar(item)) && TrainsetDisplay.IsPowered(car));

    private Control MemberFrame(Control visual, ConsistItem item, Dynamic car, string caption)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(visual);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 10,
            MaxWidth = 96,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var frame = new Border
        {
            Child = stack,
            Padding = new Thickness(1),
            BorderThickness = new Thickness(0, 2),
            BorderBrush = Brushes.Transparent,
            Cursor = _hand
        };

        if (!string.IsNullOrWhiteSpace(car.Name) && car.Name != caption)
            ToolTip.SetTip(frame, car.Name);

        var picked = car;
        frame.PointerPressed += (_, _) => _pressedCar = picked;
        _memberChrome.Add((frame, item, car));
        return frame;
    }

    private void RefreshMemberChrome()
    {
        foreach (var (badge, item) in _driverChrome)
            PaintDriverButton(badge, item);

        foreach (var (frame, item, car) in _memberChrome)
        {
            bool lit = item.Cars.Count > 1 &&
                       ReferenceEquals(item, _selected) &&
                       ReferenceEquals(car, ActiveCar(item));
            frame.BorderBrush = lit ? CardBorderSel : Brushes.Transparent;
        }
    }

    private void RefreshCardSelectionChrome()
    {
        RefreshMemberChrome();

        foreach (var child in consistStack.Children)
        {
            if (child is not Border { Tag: ConsistItem ci } border)
                continue;
            bool sel = ReferenceEquals(ci, _selected);
            border.BorderThickness = new Thickness(sel ? 2 : 1);
            border.BorderBrush = sel ? CardBorderSel : CardBorder;
            border.Background = sel ? CardBgSel : Brushes.Transparent;
        }
    }

    private void SelectInBrowser(Dynamic car)
    {
        var texture = _db.TextureForSkin(car.SkinFile);
        if (texture is null)
            return;

        var set = _db.ResolveSet(texture);
        var browserTex = set is { Count: > 0 } ? set[0] : texture;

        WhileSyncing(() =>
        {
            ApplyFilters(CategoryOf(browserTex), ClassOf(browserTex));
            BuildList();
            SelectListEntry(browserTex);
        });
    }

    private void SyncCombosTo(VehicleTexture texture)
    {
        if (_syncingCombos || !ApplyFilters(CategoryOf(texture), ClassOf(texture)))
            return;

        PostSyncing(() => { BuildList(); SelectListEntry(texture); });
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

    private static int GetWagonNumber(Dynamic car)
    {
        string name = car.Name ?? "";
        int hash = name.LastIndexOf('#');
        if (hash >= 0 && hash + 1 < name.Length && name[(hash + 1)..].All(char.IsDigit)
            && int.TryParse(name[(hash + 1)..], out int n))
            return n;
        return 0;
    }

    private void SetWagonNumber(Dynamic car, int number)
    {
        string name = car.Name ?? "";
        int hash = name.LastIndexOf('#');
        string baseName = (hash >= 0 && hash + 1 < name.Length && name[(hash + 1)..].All(char.IsDigit))
            ? name[..hash]
            : name;
        car.Name = number > 0 ? $"{baseName}#{number}" : baseName;
        RebuildConsist();
    }

    private void ShowWagonNumberFlyout(Control anchor, Dynamic car)
    {
        var num = new NumericUpDown
        {
            Minimum = 0, Maximum = 99999, Increment = 1, FormatString = "0",
            Value = GetWagonNumber(car), MinWidth = 120
        };
        var apply = new Button { Content = App.Loc["Set"], HorizontalAlignment = HorizontalAlignment.Right };
        apply.Classes.Add("Flat");
        apply.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 160 };
        panel.Children.Add(new TextBlock { Text = App.Loc["WagonNumber"], FontWeight = FontWeight.Bold, FontSize = 12 });
        panel.Children.Add(num);
        panel.Children.Add(apply);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Top };
        apply.Click += (_, _) =>
        {
            SetWagonNumber(car, (int)(num.Value ?? 0));
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    private static string UnitLabel(ConsistItem item)
    {
        string key = UnitKey(item.Cars[0]);
        return item.Cars.Count > 1 ? $"{key}  [{item.Cars.Count}]" : key;
    }

    private Control BuildCoupler(ConsistItem item, bool trailing = false)
    {
        var glyph = new TextBlock
        {
            Text = trailing ? "╡" : "≣",
            FontSize = 18,
            Opacity = trailing ? 0.55 : 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var coupler = new Button
        {
            Padding = new Thickness(2),
            MinWidth = 0,
            Cursor = _hand,
            VerticalAlignment = VerticalAlignment.Center,
            Content = glyph,
            Flyout = new Flyout
            {
                Content = BuildCouplingBox(item),
                Placement = PlacementMode.Top
            }
        };
        coupler.Classes.Add("Basic");
        ToolTip.SetTip(coupler, App.Loc["Coupling"]);
        return coupler;
    }

    private Control BuildCouplingBox(ConsistItem item)
    {
        var d = TailCar(item);
        int unitIdx = _consist.IndexOf(item);

        var panel = new StackPanel { Spacing = 2 };

        var copy = new Button
        {
            Content = "\u00AB",
            FontSize = 13,
            Padding = new Thickness(6, 1),
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = unitIdx > 0,
            Cursor = _hand
        };
        copy.Classes.Add("Flat");
        ToolTip.SetTip(copy, App.Loc["CopyCoupler"]);
        copy.Click += (_, _) =>
        {
            if (unitIdx <= 0) return;
            d.Coupling.Flags = TailCar(_consist[unitIdx - 1]).Coupling.Flags;
            RebuildConsist();
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(new TextBlock
        {
            Text = App.Loc["Coupling"],
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(header);

        for (int i = 0; i < CouplingBitKeys.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = App.Loc[CouplingBitKeys[i]],
                IsChecked = d.Coupling.Has(bit),
                FontSize = 11
            };
            check.IsCheckedChanged += (_, _) => d.Coupling.Set(bit, check.IsChecked == true);
            panel.Children.Add(check);
        }

        var thermo = new CheckBox
        {
            Content = App.Loc["ThermoAmbient"],
            IsChecked = d.Coupling.ThermoDynamic,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        };
        thermo.IsCheckedChanged += (_, _) => d.Coupling.ThermoDynamic = thermo.IsChecked == true;
        panel.Children.Add(thermo);

        return new Border { Padding = new Thickness(8), Child = panel };
    }

    private const string TextPresentation = "\uFE0E";

    private Button SmallButton(string glyph, string tip, Action onClick)
    {
        var btn = new Button
        {
            Content = glyph + TextPresentation,
            FontSize = 15,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Width = 26,
            Height = 22,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = _hand
        };
        btn.Classes.Add("Basic");
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button DriverButton(ConsistItem item)
    {
        var btn = SmallButton("", App.Loc["TipDriver"], () => CycleDriver(item));
        btn.FontWeight = FontWeight.Bold;
        _driverChrome.Add((btn, item));
        PaintDriverButton(btn, item);
        return btn;
    }

    private void PaintDriverButton(Button btn, ConsistItem item)
    {
        var type = ReferenceEquals(item, _selected) && item.Cars.Count > 1
            ? ActiveCar(item).DriverType
            : item.Driver;

        (string glyph, string color) = type switch
        {
            eDriverType.Headdriver => ("1", "#4CAF50"),
            eDriverType.Reardriver => ("2", "#FF9800"),
            eDriverType.Passenger => ("P", "#2196F3"),
            _ => ("–", "#888888")
        };

        btn.Content = glyph;
        btn.Foreground = new SolidColorBrush(Color.Parse(color));
    }

    private void UpdateDetails()
    {
        generalPanel.Children.Clear();
        consistTexturePanel.Children.Clear();
        couplerPanel.Children.Clear();
        brakesPanel.Children.Clear();
        loadsPanel.Children.Clear();
        damagePanel.Children.Clear();

        selectedVehiclePanel.Header = _selected is null
            ? App.Loc["SelectedVehicle"]
            : $"{App.Loc["SelectedVehicle"]}: " +
              (_selected.Grouped ? UnitLabel(_selected) : _selected.Cars[0].Name);

        if (_selected != null && !_selected.Cars.Contains(_selectedCar!))
            _selectedCar = _selected.Cars[0];

        RefreshMemberChrome();

        loadsPanel.Children.Add(BuildConsistLoadTools());

        if (_selected is null)
        {
            generalPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            consistTexturePanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            couplerPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            brakesPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            loadsPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            damagePanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            removeVehicleButton.IsEnabled = false;
            return;
        }

        BuildGeneralInfo(_selected);

        if (_db.TextureForSkin(ActiveCar(_selected).SkinFile) is { } tex)
            ShowTextureInfo(tex, consistTexturePanel);
        else
            consistTexturePanel.Children.Add(DetailHint(App.Loc["NoTextureInfo"]));

        removeVehicleButton.IsEnabled = true;

        if (_selected.Cars.Count > 1)
        {
            couplerPanel.Children.Add(UnitScopeHint(App.Loc["CouplingUnitRear"]));
            brakesPanel.Children.Add(UnitScopeHint(App.Loc["AppliesToUnit"]));
        }

        BuildCouplerEditor(_selected);
        BuildBrakesEditor(_selected);
        BuildLoadsEditor(_selected);
        BuildConfigExtras(_selected);
        BuildDamageEditor(_selected);
    }

    private ContextMenu BuildCardMenu(Control anchor, ConsistItem item)
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            var num = new MenuItem { Header = App.Loc["WagonNumber"] };
            num.Click += (_, _) => ShowWagonNumberFlyout(anchor, item.Cars[0]);
            menu.Items.Add(num);

            menu.Items.Add(new Separator());

            var copyPrev = new MenuItem
            {
                Header = App.Loc["LoadCopyPrev"],
                IsEnabled = _consist.IndexOf(item) > 0
            };
            copyPrev.Click += (_, _) => CopyLoadFromPrevious(item);
            menu.Items.Add(copyPrev);

            var copyToAll = new MenuItem
            {
                Header = App.Loc["LoadCopyToAll"],
                IsEnabled = IsLoadable(item)
            };
            copyToAll.Click += (_, _) => CopyLoadToFollowing(item);
            menu.Items.Add(copyToAll);

            var max = new MenuItem
            {
                Header = App.Loc["LoadMax"],
                IsEnabled = IsLoadable(item)
            };
            max.Click += (_, _) => SetUnitMaxLoad(item);
            menu.Items.Add(max);
        };
        return menu;
    }

    private Control BuildConsistLoadTools()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LoadToolButton(App.Loc["ConsistRandomType"], RandomLoadTypeForConsist));
        row.Children.Add(LoadToolButton(App.Loc["ConsistMaxAmount"], MaxAmountForConsist));
        row.Children.Add(LoadToolButton(App.Loc["ConsistRandomAmount"], RandomAmountForConsist));
        panel.Children.Add(row);
        return panel;
    }

    private Button LoadToolButton(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text, FontSize = 11, Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(8, 3), Cursor = _hand,
            IsEnabled = _consist.Count > 0
        };
        b.Classes.Add("Flat");
        b.Click += (_, _) => onClick();
        return b;
    }

    private bool IsLoadable(ConsistItem item) => IsLoadable(item.Cars[0]);

    private bool IsLoadable(Dynamic car)
    {
        string? cat = _db.TextureForSkin(car.SkinFile) is { } t ? CategoryOf(t) : null;
        if (cat is "e" or "s" or "p")
            return false;
        return !string.IsNullOrWhiteSpace(PhysicsFor(car)?.LoadAccepted);
    }

    private int MaxLoadFor(Dynamic car, string? type = null)
    {
        if (!IsLoadable(car)) return 0;

        if (Dynamic.IsPantStateType(type ?? car.LoadType)) return Dynamic.PantStateMax;

        if (car.MaxLoad >= 0) return car.MaxLoad;
        return PhysicsFor(car)?.MaxLoad ?? 0;
    }

    private int LoadLimitFor(Dynamic car, string? type = null)
    {
        int max = MaxLoadFor(car, type);
        return max > 0 ? max : (IsLoadable(car) ? int.MaxValue : 0);
    }

    private List<string> AcceptedTypesFor(Dynamic car)
    {
        var p = PhysicsFor(car);
        if (!IsLoadable(car) || p == null)
            return new List<string>();

        return p.LoadAccepted.Split(',', ';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool Accepts(Dynamic car, string? type) =>
        !string.IsNullOrEmpty(type) &&
        AcceptedTypesFor(car).Contains(type!, StringComparer.OrdinalIgnoreCase);

    private void CopyLoadFromPrevious(ConsistItem item)
    {
        int idx = _consist.IndexOf(item);
        if (idx <= 0) return;

        var source = _consist[idx - 1].Cars[0];
        foreach (var c in item.Cars)
        {
            if (!Accepts(c, source.LoadType)) continue;
            c.LoadType = source.LoadType;
            c.LoadCount = Math.Min(source.LoadCount, LoadLimitFor(c));
        }
        RebuildConsist();
    }

    private void SetUnitMaxLoad(ConsistItem item)
    {
        foreach (var c in item.Cars)
            FillToMax(c);
        RebuildConsist();
    }

    private void CopyLoadToFollowing(ConsistItem item)
    {
        int idx = _consist.IndexOf(item);
        if (idx < 0) return;

        var lead = item.Cars[0];
        string? type = lead.LoadType;
        if (string.IsNullOrEmpty(type)) return;

        foreach (var c in _consist.Skip(idx + 1).SelectMany(u => u.Cars))
        {
            if (!AcceptedTypesFor(c).Contains(type!, StringComparer.OrdinalIgnoreCase))
                continue;
            c.LoadType = type;
            c.LoadCount = Math.Min(lead.LoadCount, MaxLoadFor(c));
        }
        RebuildConsist();
    }

    private void RandomLoadTypeForConsist()
    {
        foreach (var c in _consist.SelectMany(u => u.Cars))
        {
            var types = AcceptedTypesFor(c);
            if (types.Count == 0) continue;
            c.LoadType = types[_rng.Next(types.Count)];
        }
        RebuildConsist();
    }

    private void MaxAmountForConsist()
    {
        foreach (var c in _consist.SelectMany(u => u.Cars))
            FillToMax(c);
        RebuildConsist();
    }

    private void RandomAmountForConsist()
    {
        foreach (var c in _consist.SelectMany(u => u.Cars))
        {
            int max = MaxLoadFor(c);
            if (max <= 0) continue;
            if (string.IsNullOrEmpty(c.LoadType))
                c.LoadType = AcceptedTypesFor(c).FirstOrDefault();
            if (string.IsNullOrEmpty(c.LoadType)) continue;
            int min = Math.Max(1, max / 10);
            c.LoadCount = _rng.Next(min, max + 1);
        }
        RebuildConsist();
    }

    private void FillToMax(Dynamic car)
    {
        int max = MaxLoadFor(car);
        if (max <= 0) return;
        if (string.IsNullOrEmpty(car.LoadType))
            car.LoadType = AcceptedTypesFor(car).FirstOrDefault();
        if (string.IsNullOrEmpty(car.LoadType)) return;
        car.LoadCount = max;
    }

    private void BuildGeneralInfo(ConsistItem item)
    {
        var car = ActiveCar(item);
        var inv = CultureInfo.InvariantCulture;

        bool unit = item.Cars.Count > 1;
        var members = item.Cars.Select(PhysicsFor).Where(p => p != null).ToList();

        double? length = members.Count == 0 ? null
            : unit ? members.Sum(p => p!.Length) : members[0]!.Length;
        double? mass = members.Count == 0 ? null
            : (unit ? members.Sum(p => p!.Mass) : members[0]!.Mass) / 1000.0;
        double? vmax = members.Count == 0 ? null
            : unit ? members.Min(p => p!.VMax) : members[0]!.VMax;

        generalPanel.Children.Add(InfoRow($"{App.Loc["Length"]} [m]:",
            length?.ToString("0.##", inv)));
        generalPanel.Children.Add(InfoRow($"{App.Loc["Mass"]} [t]:",
            mass?.ToString("0.#", inv)));
        generalPanel.Children.Add(InfoRow($"{App.Loc["VMax"]} [km/h]:",
            vmax?.ToString("0", inv)));

        if (unit)
            generalPanel.Children.Add(InfoRow($"{App.Loc["VehicleCount"]}:",
                item.Cars.Count.ToString(inv)));

        generalPanel.Children.Add(InfoRow($"{App.Loc["Model"]}:", car.MmdFile));
        generalPanel.Children.Add(InfoRow($"{App.Loc["Texture"]}:", car.SkinFile));
    }

    private static Control InfoRow(string label, string? value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*") };

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 12,
            MinWidth = 110,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var reading = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "–" : value,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetColumn(reading, 2);
        grid.Children.Add(caption);
        grid.Children.Add(reading);
        return grid;
    }

    private void BuildCouplerEditor(ConsistItem item)
    {
        int unitIdx = _consist.IndexOf(item);
        if (unitIdx < 0)
            return;

        bool isLast = unitIdx >= _consist.Count - 1;

        var d = TailCar(item);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 4
        };
        for (int i = 0; i < CouplingBitKeys.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = App.Loc[CouplingBitKeys[i]],
                IsChecked = d.Coupling.Has(bit),
                Classes = { "Checklist" }
            };
            check.IsCheckedChanged += (_, _) =>
            {
                d.Coupling.Set(bit, check.IsChecked == true);
                RebuildConsist();
            };
            int half = (CouplingBitKeys.Length + 1) / 2;
            Grid.SetColumn(check, i < half ? 0 : 2);
            Grid.SetRow(check, i % half);
            grid.Children.Add(check);
        }
        couplerPanel.Children.Add(grid);

        var copy = new Button
        {
            Content = App.Loc["CopyCoupler"],
            FontSize = 11,
            Cursor = _hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = unitIdx > 0
        };
        copy.Classes.Add("Flat");
        copy.Click += (_, _) =>
        {
            if (unitIdx <= 0) return;
            d.Coupling.Flags = TailCar(_consist[unitIdx - 1]).Coupling.Flags;
            RebuildConsist();
            UpdateDetails();
        };

        var auto = new Button
        {
            Content = App.Loc["AutoCoupler"],
            FontSize = 11,
            Cursor = _hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = _consist.Count > 1 && !isLast
        };
        auto.Classes.Add("Flat");
        ToolTip.SetTip(auto, App.Loc["TipAutoCoupler"]);
        auto.Click += (_, _) =>
        {
            AutoConnectAll();
            RebuildConsist();
            UpdateDetails();
        };

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("125*,4,48*"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(auto, 2);
        buttons.Children.Add(copy);
        buttons.Children.Add(auto);
        couplerPanel.Children.Add(buttons);
    }

    private void BuildConfigExtras(ConsistItem item)
    {
        var driver = new ComboBox { FontSize = 12, MinWidth = 0 };
        foreach (string key in new[] { "DriverHead", "DriverRear", "DriverPassenger", "DriverNobody" })
            driver.Items.Add(new ComboBoxItem { Content = App.Loc[key] });
        var crewCar = ActiveCar(item);

        driver.SelectedIndex = crewCar.DriverType switch
        {
            eDriverType.Headdriver => 0,
            eDriverType.Reardriver => 1,
            eDriverType.Passenger  => 2,
            _                      => 3
        };
        driver.SelectionChanged += (_, _) =>
        {
            var picked = driver.SelectedIndex switch
            {
                0 => eDriverType.Headdriver,
                1 => eDriverType.Reardriver,
                2 => eDriverType.Passenger,
                _ => eDriverType.Nobody
            };
            if (picked == crewCar.DriverType) return;

            crewCar.DriverType = picked;
            item.Driver = UnitDriver(item.Cars);
            if (ReferenceEquals(_selected, item))
                SyncStartingVehicle();
            RebuildConsist();
            AppState.Instance.NotifyChanged();
        };
        driver.MinWidth = 0;
        driver.HorizontalAlignment = HorizontalAlignment.Stretch;
        loadsPanel.Children.Insert(0, LabeledRow(App.Loc["CrewLabel"], driver, labelWidth: 70));

        var reversed = new CheckBox
        {
            Content = App.Loc["Reversed"],
            IsChecked = item.Flipped,
            FontSize = 11
        };
        reversed.IsCheckedChanged += (_, _) =>
        {
            if ((reversed.IsChecked == true) != item.Flipped)
                FlipItem(item);
        };

        var d = item.Cars[0];
        var thermo = new CheckBox
        {
            Content = App.Loc["ThermoAmbient"],
            IsChecked = d.Coupling.ThermoDynamic,
            FontSize = 11
        };
        thermo.IsCheckedChanged += (_, _) => d.Coupling.ThermoDynamic = thermo.IsChecked == true;

        var switches = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetColumn(thermo, 2);
        switches.Children.Add(reversed);
        switches.Children.Add(thermo);
        loadsPanel.Children.Add(switches);
    }

    private void RemoveVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selected is { } item)
            RemoveItem(item);
    }

    private void ConsistFilter_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppress) return;
        StarterNG.Classes.Settings.Instance.ShowAiVehicles = depotShowAiCheck.IsChecked == true;
        StarterNG.Classes.Settings.Instance.DrivableOnly = depotDrivableOnlyCheck.IsChecked == true;
        PopulateSceneryConsists();
    }

    private static TextBlock UnitScopeHint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.6,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2)
    };

    private static TextBlock DetailHint(string text) => new()
    {
        Text = text, Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap
    };

    private static Control LabeledRow(string label, Control control, double labelWidth = 120)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*") };

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 12,
            MinWidth = labelWidth,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(control, 2);
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }

    private static void AddOption(ComboBox combo, string content, string? tag)
        => combo.Items.Add(new ComboBoxItem { Content = content, Tag = tag });

    private void BuildBrakesEditor(ConsistItem item)
    {
        var brake = item.Cars[0].Coupling.GetBrake();

        ComboBox Combo(string?[] codes, string? current, Func<string?, string> label)
        {
            var combo = new ComboBox { FontSize = 12, MinWidth = 0 };
            foreach (var code in codes) AddOption(combo, label(code), code);
            combo.SelectedIndex = Math.Max(0, Array.IndexOf(codes, current));
            return combo;
        }

        static string?[] WithDefault(string[] codes) =>
            new string?[] { null }.Concat(codes).ToArray();

        var modeCombo = Combo(WithDefault(BrakeSetting.Modes), brake?.Mode, BrakeModeLabel);
        var switchCombo = Combo(WithDefault(BrakeSetting.Switches), brake?.Switch, SwitchLabel);
        var loadCombo = Combo(WithDefault(BrakeSetting.Loads), brake?.Load, LoadLabel);

        void Apply()
        {
            var b = new BrakeSetting
            {
                Mode = (modeCombo.SelectedItem as ComboBoxItem)?.Tag as string,
                Switch = (switchCombo.SelectedItem as ComboBoxItem)?.Tag as string,
                Load = (loadCombo.SelectedItem as ComboBoxItem)?.Tag as string
            };
            foreach (var c in UnitTargets(item)) c.Coupling.SetBrake(b);
        }

        modeCombo.SelectionChanged += (_, _) => Apply();
        switchCombo.SelectionChanged += (_, _) => Apply();
        loadCombo.SelectionChanged += (_, _) => Apply();

        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeMode"], modeCombo, labelWidth: 96));
        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeSwitch"], switchCombo, labelWidth: 96));
        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeLoad"], loadCombo, labelWidth: 96));
    }

    private static string BrakeModeLabel(string? code) => code switch
    {
        "G" => App.Loc["BrakeFreight"],
        "P" => App.Loc["BrakePassenger"],
        "R" => App.Loc["BrakeExpress"],
        "M" => App.Loc["BrakeExpressMg"],
        "Q" => App.Loc["BrakeNoAir"],
        _ => App.Loc["BrakeActingNone"]
    };

    private static string LoadLabel(string? code) => code switch
    {
        "T" => App.Loc["LoadEmpty"],
        "H" => App.Loc["LoadMedium"],
        "F" => App.Loc["LoadFull"],
        _ => App.Loc["LoadNeutral"]
    };

    private static string SwitchLabel(string? code) => code switch
    {
        "0" => App.Loc["SwitchOff"],
        "1" => App.Loc["SwitchOff10"],
        _ => App.Loc["SwitchNormal"]
    };

    private void BuildDamageEditor(ConsistItem item)
    {
        var w = ActiveCar(item).Coupling.GetWheels() ?? new WheelSettings();

        NumericUpDown Spin(int value, int max) => new()
        {
            FontSize = 12, Minimum = 0, Maximum = max, Increment = 1,
            Value = value, MinWidth = 0, Width = 100, FormatString = "0"
        };

        var sway = Spin(w.Sway, 100);
        var flat = Spin(w.Flatness, 100);
        var flatRand = Spin(w.FlatnessRand, 100);
        var flatProb = Spin(w.FlatnessProb, 100);

        void Apply()
        {
            var ws = new WheelSettings
            {
                Sway = (int)(sway.Value ?? 0),
                Flatness = (int)(flat.Value ?? 0),
                FlatnessRand = (int)(flatRand.Value ?? 0),
                FlatnessProb = (int)(flatProb.Value ?? 0)
            };
            foreach (var c in MemberTargets(item))
                c.Coupling.SetWheels(ws);
        }

        sway.ValueChanged += (_, _) => Apply();
        flat.ValueChanged += (_, _) => Apply();
        flatRand.ValueChanged += (_, _) => Apply();
        flatProb.ValueChanged += (_, _) => Apply();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto"),
            RowSpacing = 6
        };
        void Row(string label, Control control)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var caption = new TextBlock
            {
                Text = label, FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(caption, r);
            Grid.SetColumn(caption, 0);

            control.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 2);

            grid.Children.Add(caption);
            grid.Children.Add(control);
        }

        Row(App.Loc["DamageSway"], sway);
        Row(App.Loc["DamageFlatness"], flat);
        Row(App.Loc["DamageFlatnessRand"], flatRand);
        Row(App.Loc["DamageFlatnessProb"], flatProb);
        damagePanel.Children.Add(grid);
    }

    private void BuildLoadsEditor(ConsistItem item)
    {
        var lead = ActiveCar(item);
        var available = AcceptedTypesFor(lead);

        if (available.Count == 0 && string.IsNullOrEmpty(lead.LoadType))
        {
            loadsPanel.Children.Add(DetailHint(App.Loc["LoadNotLoadable"]));
            return;
        }

        if (!string.IsNullOrEmpty(lead.LoadType) &&
            !available.Contains(lead.LoadType!, StringComparer.OrdinalIgnoreCase))
            available.Insert(0, lead.LoadType!);

        var typeCombo = new ComboBox { FontSize = 12, MinWidth = 0 };
        typeCombo.Items.Add(new ComboBoxItem { Content = App.Loc["None"], Tag = null });
        foreach (var name in available)
            typeCombo.Items.Add(new ComboBoxItem { Content = LoadDesc(name), Tag = name });

        typeCombo.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(lead.LoadType))
            foreach (var obj in typeCombo.Items)
                if (obj is ComboBoxItem ci && ci.Tag is string t &&
                    string.Equals(t, lead.LoadType, StringComparison.OrdinalIgnoreCase))
                {
                    typeCombo.SelectedItem = ci;
                    break;
                }

        int limit = LoadLimitFor(lead);
        var countBox = new NumericUpDown
        {
            FontSize = 12, Minimum = 0, Maximum = limit, Increment = 1,
            Value = Math.Min(lead.LoadCount, limit), MinWidth = 120, FormatString = "0"
        };

        var pantHint = DetailHint(App.Loc["LoadPantStateHint"]);
        pantHint.IsVisible = lead.IsPantState;

        string? SelectedType() => (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        void Apply()
        {
            string? type = SelectedType();
            int count = (int)(countBox.Value ?? 0);
            foreach (var c in MemberTargets(item))
            {

                c.LoadType = string.IsNullOrEmpty(type) ? null : type;
                c.LoadCount = string.IsNullOrEmpty(type) ? 0 : Math.Min(count, LoadLimitFor(c, type));
            }
        }

        typeCombo.SelectionChanged += (_, _) =>
        {
            string? type = SelectedType();
            pantHint.IsVisible = Dynamic.IsPantStateType(type);
            countBox.Maximum = LoadLimitFor(lead, type);
            Apply();
        };
        countBox.ValueChanged += (_, _) => Apply();

        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadType"], typeCombo));
        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadCount"], countBox));
        loadsPanel.Children.Add(pantHint);
    }

    private static Dictionary<string, int>? _loadWeights;

    private static void EnsureLoads()
    {
        if (_loadWeights != null)
            return;

        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Dynamic.PantState] = 0
        };
        try
        {
            string path = Path.Combine("data", "load_weights.txt");
            if (File.Exists(path))
            {
                var body = new StringBuilder();
                foreach (var raw in File.ReadAllLines(path, Encoding.GetEncoding(1250)))
                {
                    string line = raw;
                    int comment = line.IndexOfAny(new[] { '#', ';' });
                    if (comment >= 0) line = line[..comment];
                    int slashes = line.IndexOf("//", StringComparison.Ordinal);
                    if (slashes >= 0) line = line[..slashes];
                    body.Append(line).Append(' ');
                }

                var tokens = body.Replace(":", " : ").ToString()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 1; i + 1 < tokens.Length; i++)
                {
                    if (tokens[i] != ":") continue;
                    string name = tokens[i - 1];
                    if (name is "{" or "}") continue;
                    if (!int.TryParse(tokens[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int w))
                        continue;
                    weights[name] = w;
                }
            }
        }
        catch {  }

        _loadWeights = weights;
    }

    private static string LoadDesc(string name) =>
        string.Equals(name, Dynamic.PantState, StringComparison.OrdinalIgnoreCase)
            ? App.Loc["LoadPantState"]
            : name;

    private static int LoadWeight(string? name)
    {
        EnsureLoads();
        return !string.IsNullOrEmpty(name) && _loadWeights!.TryGetValue(name!, out int w) ? w : 1000;
    }

    private Bitmap? GetMiniBitmap(string? name, int height, bool strict = false)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (strict && !VehicleDatabase.HasMini(name))
            return null;

        string key = $"{(strict ? "!" : "")}{name}@{height}";
        if (_miniCache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? bmp = null;
        string? path = VehicleDatabase.MiniPath(name);
        if (path != null)
        {
            try
            {
                using var fs = File.OpenRead(path);
                bmp = Bitmap.DecodeToHeight(fs, height);
            }
            catch { bmp = null; }
        }
        _miniCache[key] = bmp;
        return bmp;
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || _searchTimer is null) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void HideArchivalCheck_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppress) return;
        StarterNG.Classes.Settings.Instance.HideArchivalVehicles = hideArchivalCheck.IsChecked ?? true;
        StarterNG.Classes.Settings.Instance.Save();
        BuildList();
    }

    private async void TextureBaseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var win = new TextureBaseWindow();
        await win.ShowDialog(owner);
        if (win.Picked != null)
            ReplaceSelected(win.Picked);
    }

    public async Task RandomizeCurrentTrainsetAsync()
    {
        if (AppState.Instance.CurrentTrainset is not { } trainset)
            return;

        if (!ReferenceEquals(trainset, _editingTrainset))
        {
            PopulateSceneryConsists();
            LoadTrainset(trainset);
        }
        await RunTextureRandomizer();
    }

    public void ToolRandomOrder() => RandomOrderButton_OnClick(null, null!);
    public void ToolRandomOrientation() => RandomOrientButton_OnClick(null, null!);

    private async void RandomTexturesButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunTextureRandomizer();

    private async Task RunTextureRandomizer()
    {
        if (_consist.Count == 0) return;
        if (await AskTextureRandomizerOpts() is not { } opts) return;

        if (ApplyTextureRandomizer(opts))
            RebuildConsist();
    }

    public async Task RandomizeSceneryTrainsetsAsync(Scenery scenery)
    {
        if (scenery.Trainsets.Count == 0) return;
        if (await AskTextureRandomizerOpts() is not { } opts) return;

        var editing = _editingTrainset;
        foreach (var trainset in scenery.Trainsets)
        {
            LoadTrainset(trainset);
            if (ApplyTextureRandomizer(opts))
                WriteBackToScenery();
        }

        if (editing != null)
            LoadTrainset(editing);
        RebuildConsist();
    }

    private async Task<TexRandomizerOpts?> AskTextureRandomizerOpts()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;

        var dlg = BuildTextureRandomizerDialog();
        bool? ok = await dlg.ShowDialog<bool?>(owner);
        return ok == true ? dlg.Tag as TexRandomizerOpts : null;
    }

    private bool ApplyTextureRandomizer(TexRandomizerOpts opts)
    {
        if (_consist.Count == 0) return false;

        var rnd = new TexRandomizer(_db, _rng)
        {
            WithoutArchival = opts.WithoutArchival,
            RevisionTolerance = opts.RevisionTolerance,
            RevYear = opts.RevYear
        };
        if (rnd.Apply(_consist, MakeDynamic) <= 0)
            return false;

        AutoConnectAll();
        return true;
    }

    private Window BuildTextureRandomizerDialog()
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
        rulesBtn.Click += async (_, _) => await EditRandomizerRules(win);

        var ok = new Button
        {
            Content = App.Loc["RandomizeTexturesApply"],
            MinWidth = 90,
            Cursor = _hand
        };
        ok.Classes.Add("Accent");
        ok.Click += (_, _) =>
        {
            win.Tag = new TexRandomizerOpts
            {
                WithoutArchival = noArch.IsChecked == true,
                RevisionTolerance = revDiff.IsChecked == true ? (int)(age.Value ?? 3) : -1,
                RevYear = vsYear.IsChecked == true ? (int)(year.Value ?? DateTime.Now.Year) : -1
            };
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

    private async Task EditRandomizerRules(Window owner)
    {
        await new RulesWindow().ShowDialog(owner);
    }

    private sealed class TexRandomizerOpts
    {
        public bool WithoutArchival;
        public int RevisionTolerance;
        public int RevYear;
    }

    private void RandomOrderButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RandomizeOrder();

    private void RandomOrientButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RandomizeOrientation();

    private void RandomizeOrder()
    {
        int start = _selected != null ? _consist.IndexOf(_selected) : 0;
        if (start < 0) start = 0;

        var indices = new List<int>();
        for (int i = start; i < _consist.Count; i++)
        {
            if (_consist[i].Grouped) continue;
            var tex = _db.TextureForSkin(_consist[i].Cars[0].SkinFile);
            string? cat = tex != null ? CategoryOf(tex) : null;
            if (cat is { Length: 1 } && char.IsUpper(cat[0]))
                indices.Add(i);
        }

        if (indices.Count < 2) return;

        var items = indices.Select(i => _consist[i]).ToList();
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        for (int k = 0; k < indices.Count; k++)
            _consist[indices[k]] = items[k];

        AutoConnectAll();
        RebuildConsist();
    }

    private void RandomizeOrientation()
    {
        int start = _selected != null ? _consist.IndexOf(_selected) : 0;
        if (start < 0) start = 0;
        if (start >= _consist.Count) return;

        for (int i = start; i < _consist.Count; i++)
            if (_rng.Next(2) == 0)
                _consist[i].Flipped = !_consist[i].Flipped;

        AutoConnectAll();
        RebuildConsist();
    }

    private void MiniPreview_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_browserSelected is null || !e.GetCurrentPoint(miniPreviewPanel).Properties.IsLeftButtonPressed)
            return;
        ArmVehicleDrag(e, miniPreviewPanel, _browserSelected);
    }

    private void VehicleListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(vehicleListBox).Properties.IsLeftButtonPressed)
            return;

        var hit = e.Source as Control;
        while (hit != null && hit is not ListBoxItem)
            hit = hit.Parent as Control;
        if (hit is ListBoxItem { Tag: VehicleTexture texture })
            ArmVehicleDrag(e, vehicleListBox, texture);
    }

    private void ArmVehicleDrag(PointerPressedEventArgs e, Visual relativeTo, VehicleTexture texture)
    {
        ClearPendingDrag();
        _pendingDragPress = e;
        _pendingDragOrigin = e.GetPosition(relativeTo);
        _pendingDragTexture = texture;
        _pendingDragConsistIndex = -1;
    }

    private void ClearPendingDrag()
    {
        _pendingDragPress = null;
        _pendingDragTexture = null;
        _pendingDragConsistIndex = -1;
        _pendingBrowserSync = null;
        _dragSessionActive = false;
    }

    private async void PendingDrag_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragPress is null || _dragSessionActive || sender is not Visual relativeTo)
            return;
        if (!e.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed)
        {
            FinishPendingClick();
            return;
        }

        var delta = e.GetPosition(relativeTo) - _pendingDragOrigin;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        var press = _pendingDragPress;
        var texture = _pendingDragTexture;
        int consistIndex = _pendingDragConsistIndex;

        _pendingBrowserSync = null;
        _pendingDragPress = null;
        _dragSessionActive = true;

        if (texture != null)
        {
            _dragTexture = texture;
            _dragConsistIndex = -1;
            CaptureCardMidpoints();
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(DragVehicleFormat));
            try
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
            }
            finally
            {
                _dragTexture = null;
                CloseDropGap();
                ClearPendingDrag();
            }
            return;
        }

    }

    private void PendingDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSessionActive) return;
        FinishPendingClick();
    }

    private void FinishPendingClick()
    {
        var sync = _pendingBrowserSync;
        ClearPendingDrag();

        if (sync is { Cars.Count: > 0 })
            SelectInBrowser(sync.Cars[0]);
    }

    private void Consist_OnDragOver(object? sender, DragEventArgs e)
    {

        if (_dragTexture != null || _dragConsistIndex >= 0)
        {
            e.DragEffects = _dragConsistIndex >= 0 ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;

            double pointerX = e.GetPosition(consistScroll).X;
            UpdateDropIndex(pointerX + consistScroll.Offset.X);
            _edgeScroller.Update(consistScroll, pointerX);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Strip_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(consistStack);

        if (!_cardDragging)
        {
            if (_pressCardIndex < 0 ||
                !e.GetCurrentPoint(consistStack).Properties.IsLeftButtonPressed)
                return;

            var delta = point - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;

            BeginCardDrag(e);
            if (!_cardDragging) return;
        }

        Canvas.SetLeft(_carried!, point.X - _grabOffset);

        double pointerX = e.GetPosition(consistScroll).X;
        UpdateDropIndex(pointerX + consistScroll.Offset.X);
        _edgeScroller.Update(consistScroll, pointerX);
    }

    private void Strip_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_cardDragging)
        {
            int clicked = _pressCardIndex;
            _pressCardIndex = -1;

            if (e.InitialPressMouseButton == MouseButton.Left &&
                clicked >= 0 && clicked < _consist.Count &&
                _consist[clicked].Cars.Count > 0)
                SelectInBrowser(_consist[clicked].Cars[0]);
            return;
        }

        int from = _dragConsistIndex;
        int target = _dropTracker.Index;

        EndCardDrag();

        if (from >= 0 && target >= 0)
            MoveConsistUnit(from, target);
    }

    private void BeginCardDrag(PointerEventArgs e)
    {
        int card = _pressCardIndex;
        if (card < 0 || 2 * card + 1 >= consistStack.Children.Count)
            return;

        var cardVisual = consistStack.Children[2 * card];
        var coupler = consistStack.Children[2 * card + 1];
        var bounds = cardVisual.Bounds;

        _dragConsistIndex = card;
        _dragTexture = null;
        _dragCardWidth = Math.Max(bounds.Width + coupler.Bounds.Width, 24);
        _grabOffset = Math.Clamp(_pressPoint.X - bounds.Left, 0, _dragCardWidth);

        CaptureCardMidpoints();

        _carried = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Opacity = 0.85,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            BorderBrush = CardBorderSel,
            Background = CardBgSel,
            Child = new Image
            {
                Source = CardSnapshot(cardVisual),
                Stretch = Stretch.Uniform
            }
        };
        Canvas.SetTop(_carried, bounds.Top);
        Canvas.SetLeft(_carried, bounds.Left);
        consistOverlay.Children.Add(_carried);

        cardVisual.IsVisible = false;
        coupler.IsVisible = false;

        _cardDragging = true;
        e.Pointer.Capture(consistStack);
    }

    private static Bitmap? CardSnapshot(Control source)
    {
        if (source.Bounds.Width < 1 || source.Bounds.Height < 1)
            return null;

        try
        {
            var size = new PixelSize((int)source.Bounds.Width, (int)source.Bounds.Height);
            var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(source);
            return bitmap;
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log("Card snapshot", ex);
            return null;
        }
    }

    private void EndCardDrag()
    {
        if (_dragConsistIndex >= 0 && 2 * _dragConsistIndex + 1 < consistStack.Children.Count)
        {
            consistStack.Children[2 * _dragConsistIndex].IsVisible = true;
            consistStack.Children[2 * _dragConsistIndex + 1].IsVisible = true;
        }

        if (_carried != null)
            consistOverlay.Children.Remove(_carried);
        _carried = null;

        CloseDropGap();
        _dragConsistIndex = -1;
        _pressCardIndex = -1;
        _cardDragging = false;
    }

    private void CaptureCardMidpoints()
    {
        var midpoints = new List<double>();
        for (int i = 0; i < consistStack.Children.Count; i += 2)
        {
            int card = i / 2;
            if (card == _dragConsistIndex) continue;

            var child = consistStack.Children[i];
            if (_dropGap.IsGap(child)) continue;

            midpoints.Add(child.Bounds.Center.X -
                          (_dragConsistIndex >= 0 && card > _dragConsistIndex ? _dragCardWidth : 0));
        }
        _dropTracker.Capture(midpoints);
    }

    private void UpdateDropIndex(double contentX)
    {
        if (!_dropTracker.Update(contentX)) return;

        int card = _dropTracker.Index;
        if (_dragConsistIndex >= 0 && card > _dragConsistIndex)
            card++;

        _dropGap.Show(consistStack, 2 * card, _dragCardWidth);
    }

    private void CloseDropGap()
    {
        _dropTracker.Reset();
        _edgeScroller.Stop();
        _dropGap.Hide(consistStack);
    }

    private void Consist_OnDrop(object? sender, DragEventArgs e)
    {
        int target = _dropTracker.Index >= 0 ? _dropTracker.Index : _consist.Count;
        CloseDropGap();

        if (_dragTexture != null)
        {
            InsertTextureAt(_dragTexture, target);
            e.Handled = true;
        }
        else if (_dragConsistIndex >= 0)
        {
            MoveConsistUnit(_dragConsistIndex, target);
            e.Handled = true;
        }
    }

    private void MoveConsistUnit(int from, int to)
    {
        if (from < 0 || from >= _consist.Count) return;

        var item = _consist[from];
        _consist.RemoveAt(from);
        to = Math.Clamp(to, 0, _consist.Count);
        _consist.Insert(to, item);

        _selected = item;
        AutoConnectAll();
        RebuildConsist();
    }

}
