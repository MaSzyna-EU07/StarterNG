using System;
using System.Collections.Generic;
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
    // Data is parsed once at startup (behind the splash) and shared here.
    private readonly VehicleDatabase _db = GameData.Instance.Vehicles;
    private readonly List<Scenery> _sceneries = GameData.Instance.Sceneries;

    // The consist being edited: a list of units (each unit = one or more cars).
    private readonly List<ConsistItem> _consist = new();
    private ConsistItem? _selected;

    // The scenery trainset this consist is bound to; edits are written back to it
    // so the scenery is modified. Null when building an unattached consist.
    private Trainset? _editingTrainset;

    // Shared, size-limited thumbnail cache (decoded down to display height).
    private readonly Dictionary<string, Bitmap?> _miniCache = new();
    private int ConsistThumbHeight =>
        StarterNG.Classes.Settings.Instance.LargeThumbnails ? 68 : 34;

    // Display height of the mini under the vehicle list; the class combo uses
    // the same size.
    private const int MiniPreviewHeight = 54;

    private const int CompactThumbHeight = 24;

    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private int _nameCounter;

    // shared RNG for the "random load / random amount" consist tools
    private static readonly Random _rng = new();

    // suppresses combo SelectionChanged handlers while we populate them
    private bool _suppress;
    private bool _syncingCombos;   // set while the combos mirror a pick

    // debounces search input so we don't rebuild on every keystroke
    private DispatcherTimer? _searchTimer;

    // the scenery whose consists are listed on the right (from the Scenarios tab)
    private Scenery? _listScenery;

    // active database filters
    private Func<string?, bool>? _categoryFilter;  // matches a category letter, null = none chosen
    private string? _classFilter;                  // group mini (e.g. EU07), null = all in category

    // the vehicle currently highlighted in the flat list (drives the mini preview
    // and the single Add button)
    private VehicleTexture? _browserSelected;

    // drag-drop payload while a pointer drag is in progress
    private VehicleTexture? _dragTexture;
    private int _dragConsistIndex = -1;
    private const string DragVehicleFormat = "starterng/vehicle";
    private const string DragConsistFormat = "starterng/consist-index";

    // Armed on press, started only after the pointer moves past DragThreshold.
    // Starting DoDragDropAsync on every click freezes Avalonia (pointer capture).
    private PointerPressedEventArgs? _pendingDragPress;
    private Point _pendingDragOrigin;
    private VehicleTexture? _pendingDragTexture;
    private int _pendingDragConsistIndex = -1;
    private ConsistItem? _pendingBrowserSync;
    private bool _dragSessionActive;
    private const double DragThreshold = 6;

    // hard cap on how many entries the flat list shows at once
    private const int MaxListRows = 2000;

    // card highlight brushes (theme-neutral overlays)
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#33888888"));
    private static readonly IBrush CardBorderSel = new SolidColorBrush(Color.Parse("#AA41C400"));
    private static readonly IBrush CardBgSel = new SolidColorBrush(Color.Parse("#2241C400"));
    private static readonly IBrush Placeholder = new SolidColorBrush(Color.Parse("#22808080"));

    // Vehicle types, keyed by the JSON "category" letter. Powered/technical
    // vehicles use lowercase letters; wagons use uppercase letters.
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

    // lowercase letters that already have a named type (so they aren't "other")
    private static readonly HashSet<string> NamedLowerCats =
        new(StringComparer.Ordinal) { "a", "b", "c", "d", "e", "f", "h", "n", "o", "p", "r", "s", "t", "x", "z" };

    private static bool IsOtherCat(string? c) =>
        c is { Length: 1 } && char.IsLower(c[0]) && !NamedLowerCats.Contains(c);

    // Car-type suffixes (longest first) used only for the consist unit label.
    private static readonly string[] CarSuffixes = { "sa", "sb", "ra", "rb", "s" };

    // Coupling-bit localisation keys in mask-bit order (bit i = value 1<<i), per
    // the wiki: https://wiki.eu07.pl/index.php?title=Wpisy_hamulca_dla_pojazdow
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

        // Defer population so the tab renders immediately, then fills in.
        Dispatcher.UIThread.Post(() =>
        {
            hideArchivalCheck.IsChecked = StarterNG.Classes.Settings.Instance.HideArchivalVehicles;
            InitClassComboTemplates();
            PopulateCategoryCombo();   // -> populates class combo -> builds browser
            PopulateSceneryConsists(); // -> lists the current scenery's consists
            RebuildConsist();
        }, DispatcherPriority.Background);

        // Consist strip accepts drops from the browser and reorders via card drag.
        DragDrop.SetAllowDrop(consistDropTarget, true);
        consistDropTarget.AddHandler(DragDrop.DragOverEvent, Consist_OnDragOver);
        consistDropTarget.AddHandler(DragDrop.DropEvent, Consist_OnDrop);

        vehicleListBox.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        vehicleListBox.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);

        // Switching tabs only toggles IsVisible (the view stays in the visual
        // tree), so refresh whenever the depot becomes visible - this picks up a
        // scenery changed in the Scenarios tab in the meantime.
        AttachedToVisualTree += (_, _) => SyncFromSelection();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                SyncFromSelection();
        };
    }

    private void SyncFromSelection()
    {
        // pick up Settings-tab changes to the archival-vehicle filter
        bool hideArchival = StarterNG.Classes.Settings.Instance.HideArchivalVehicles;
        if (hideArchivalCheck.IsChecked != hideArchival)
        {
            _suppress = true;
            hideArchivalCheck.IsChecked = hideArchival;
            _suppress = false;
            BuildList();
        }

        // refresh the on-map consists list to the scenery picked in the Scenarios tab
        PopulateSceneryConsists();

        var trainset = AppState.Instance.CurrentTrainset;
        if (trainset != null && !ReferenceEquals(trainset, _editingTrainset))
            LoadTrainset(trainset);
        else if (_editingTrainset != null)
            RebuildConsist(); // pick up LargeThumbnails / filter-driven list changes
    }

    // Loads a scenery trainset into the editor and binds edits back to it.
    // Consecutive cars that belong to the same auto-consist set are regrouped
    // into one locked unit so a scenery EN57 shows as a single vehicle.
    private void LoadTrainset(Trainset trainset)
    {
        _editingTrainset = trainset;
        _consist.Clear();

        var cars = trainset.Vehicles.Select(v =>
        {
            var c = v.Clone();
            c.MiniName = _db.MiniForSkin(c.SkinFile) ?? c.MiniName; // mini from texture_mini
            return c;
        }).ToList();

        int i = 0;
        while (i < cars.Count)
        {
            VehicleSet? set = null;
            if (_db.TextureForSkin(cars[i].SkinFile)?.Uuid is { } uuid)
                _db.SetByTextureUuid.TryGetValue(uuid, out set);

            if (set?.TextureRefs is { Count: > 1 })
            {
                var members = new HashSet<string>(set.TextureRefs.Where(r => !string.IsNullOrEmpty(r)));
                var seen = new HashSet<string>();
                var group = new List<Dynamic>();

                int j = i;
                while (j < cars.Count)
                {
                    if (_db.TextureForSkin(cars[j].SkinFile)?.Uuid is { } u
                        && members.Contains(u) && seen.Add(u))
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
                        // FirstOrDefault(predicate) defaults the enum to Headdriver(0) when
                        // every car is Nobody — that re-staffed decorative consists (#12).
                        Driver = UnitDriver(group)
                    });
                    i = j;
                    continue;
                }
            }

            // fallback: group consecutive cars sharing a unit number (e.g. EN57-001ra/s/rb)
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
                Driver = cars[i].DriverType
            });
            i++;
        }

        _selected = _consist.FirstOrDefault();
        SyncStartingVehicle();
        RebuildConsist();
    }

    // First staffed occupancy on the unit, or Nobody when the whole unit is unstaffed.
    private static eDriverType UnitDriver(IReadOnlyList<Dynamic> group) =>
        group.Select(c => c.DriverType)
            .Where(d => d != eDriverType.Nobody)
            .DefaultIfEmpty(eDriverType.Nobody)
            .First();

    private void SyncStartingVehicle()
    {
        if (_selected?.Cars.Count > 0)
            AppState.Instance.StartingVehicleName = _selected.Cars[0].Name;
    }

    // ----------------------------------------------------- series / unit helpers

    // Removes a trailing car-type suffix that directly follows a digit
    // ("EN57-001ra" -> "EN57-001", "EN57ra" -> "EN57", "EP07-332" -> unchanged).
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

    // True when the skin has a car-type suffix (so it is part of a multi-car unit).
    private static bool IsUnitCar(Dynamic d) => UnitKey(d) != Base(d.SkinFile);

    // Vehicle class = the "mini" of the group the texture belongs to (e.g. EU07).
    // Resolved once at load (see VehicleDatabase.BuildTextureIndex); read the cache
    // here so search/filter never repeats the group lookup per keystroke.
    private static string ClassOf(VehicleTexture t) => t.ResolvedClass;

    // Vehicle type = the group's "category" letter (also resolved once at load).
    private static string? CategoryOf(VehicleTexture t) => t.ResolvedCategory;

    // ------------------------------------------------------------ category combo

    private void PopulateCategoryCombo()
    {
        // No "All" entry: nothing is selected by default, so the list starts empty
        // (matching the original Starter's depot).
        _suppress = true;
        categoryCombo.Items.Clear();
        foreach (var (locKey, match) in CategoryDefs)
            categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc[locKey], Tag = match });
        categoryCombo.SelectedIndex = -1;
        _suppress = false;

        _categoryFilter = null;
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

    // The dropdown shows the big stacked miniature, the closed combo a one-line
    // row. Plain "=": SelectionBoxItemTemplate coerces to ItemTemplate while
    // unset, so "??=" would never assign.
    private void InitClassComboTemplates()
    {
        classCombo.ItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, large: true), false);
        classCombo.SelectionBoxItemTemplate = new FuncDataTemplate<string>(
            (cls, _) => ClassComboContent(cls, large: false), false);
    }

    // Fills the class combo for the current category filter; the caller owns the
    // selection and _classFilter. Runs under _suppress.
    // No "All" entry: an unselected class means "every class in the category".
    private void FillClassCombo()
    {
        classCombo.Items.Clear();
        foreach (string cls in ClassesForCategory(_categoryFilter))
            classCombo.Items.Add(cls);
    }

    // Points both combos at the given category/class pair without firing their
    // handlers. Returns false when they already matched.
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

    // Every texture of a class shares its category.
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

        // ApplyFilters refills this very combo, so it cannot run from here.
        if (cls != null && _categoryFilter == null &&
            PostSyncing(() => { ApplyFilters(CategoryOfClass(cls), cls); BuildList(); }))
            return;

        BuildList();
    }

    // Hovering over the Type/Class combos and scrolling cycles the selection
    // without having to open the dropdown (scroll up = previous, down = next).
    // When the dropdown is open the wheel scrolls the list as usual.
    private void Combo_OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ComboBox combo || combo.IsDropDownOpen)
            return;

        int count = combo.ItemCount;
        if (count == 0)
            return;

        int index = combo.SelectedIndex;
        if (e.Delta.Y > 0)                 // scroll up -> previous
            index = index <= 0 ? 0 : index - 1;
        else if (e.Delta.Y < 0)            // scroll down -> next
            index = index < 0 ? 0 : Math.Min(index + 1, count - 1);
        else
            return;

        combo.SelectedIndex = index;
        e.Handled = true;
    }

    // ----------------------------------------------------------- vehicle browser

    // Flat list of plain-text vehicle entries (no expanders, like the original
    // Starter). Empty by default: an entry only appears once a category or class
    // is chosen or a search is typed. Picking one drives the mini preview and the
    // Add button.
    private void BuildList()
    {
        // reset selection-dependent UI
        _browserSelected = null;
        miniPreview.Source = null;
        addVehicleButton.IsEnabled = false;
        browserDetails.IsVisible = false;

        vehicleListBox.Items.Clear();

        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        // no filter and nothing searched -> keep the list empty (a class counts
        // as a filter: it can be picked without a category)
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

    // Selecting an entry shows its mini preview and enables the Add button.
    private void VehicleListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (vehicleListBox.SelectedItem is ListBoxItem { Tag: VehicleTexture texture })
        {
            _browserSelected = texture;
            miniPreview.Source = GetMiniBitmap(_db.ResolveMiniName(texture), MiniPreviewHeight);
            addVehicleButton.IsEnabled = true;
            ShowTextureInfo(texture);
            browserDetails.IsVisible = true;
            SyncCombosTo(texture);
        }
        else
        {
            _browserSelected = null;
            miniPreview.Source = null;
            addVehicleButton.IsEnabled = false;
            textureInfoPanel.Children.Clear();
            browserDetails.IsVisible = false;
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
                await top.Clipboard.SetTextAsync(BrowserLabel(texture, _db.ResolveSet(texture)));
        };
        menu.Items.Add(copy);

        var open = new MenuItem { Header = App.Loc["OpenTextureFolder"] };
        open.Click += (_, _) =>
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "dynamic",
                texture.Directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd('\\', '/'));
            if (!Directory.Exists(dir))
                dir = Path.Combine(Directory.GetCurrentDirectory(), texture.Directory);
            if (!Directory.Exists(dir)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        };
        menu.Items.Add(open);
        return menu;
    }

    private void ShowTextureInfo(VehicleTexture texture)
    {
        textureInfoPanel.Children.Clear();
        // The rows are trimmed, so the tooltip carries the full text.
        var full = new List<string>();
        void Row(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string line = $"{label}: {value}";
            full.Add(line);
            textureInfoPanel.Children.Add(new TextBlock
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
            Row(App.Loc["TexAuthor"], meta.TextureAuthor);
            Row(App.Loc["TexPhoto"], meta.PhotoAuthor);
        }
        ToolTip.SetTip(textureInfoPanel, string.Join("\n", full));
    }

    // Double-click an entry to replace the selected consist vehicle.
    private void VehicleListBox_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_browserSelected != null)
            ReplaceSelected(_browserSelected);
    }

    // The single Add button inserts the highlighted vehicle after the selected
    // consist vehicle (or at the end when nothing is selected).
    private void AddVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_browserSelected != null)
            AddTexture(_browserSelected);
    }

    // Pascal actAddVehiclesCategory — every skin currently listed in the browser.
    private void AddAllCategoryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var t in CurrentBrowserTextures())
            InsertTextureAt(t, _consist.Count);
    }

    // Pascal actAddVehiclesMMD — first skin per distinct model/MMD.
    private void AddUniqueMmdButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in CurrentBrowserTextures())
        {
            string key = string.IsNullOrEmpty(t.Model) ? t.Skinfile : t.Model!;
            if (!seen.Add(key)) continue;
            InsertTextureAt(t, _consist.Count);
        }
    }

    private List<VehicleTexture> CurrentBrowserTextures()
    {
        var list = new List<VehicleTexture>();
        foreach (var obj in vehicleListBox.Items)
        {
            if (obj is ListBoxItem { Tag: VehicleTexture t, IsEnabled: true })
                list.Add(t);
        }
        return list;
    }

    private bool PassesFilters(VehicleTexture t, string search, bool hasSearch)
    {
        if (_categoryFilter != null && !_categoryFilter(CategoryOf(t)))
            return false;

        if (_classFilter != null &&
            !string.Equals(ClassOf(t), _classFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        // Hide subsequent multi-unit cars; adding the lead expands the whole set.
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

    // Replaces the selected consist unit with this vehicle/unit (keeping its
    // slot, crew and orientation). Falls back to appending if nothing is selected.
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

    private string BrowserLabel(VehicleTexture texture, IReadOnlyList<VehicleTexture>? set)
    {
        // When texture_mini is missing or only the class/category mini, use the
        // skin basename so the list doesn't show the category name as the vehicle.
        string name;
        if (!string.IsNullOrEmpty(texture.TextureMini) &&
            !string.Equals(texture.TextureMini, texture.ResolvedClass, StringComparison.OrdinalIgnoreCase))
            name = texture.TextureMini!;
        else
            name = Base(texture.Skinfile);

        if (!string.IsNullOrEmpty(texture.Meta?.Operator))
            name += $"  ·  {texture.Meta!.Operator}";
        if (set is { Count: > 1 })
            name += $"   [{set.Count}]";
        return name;
    }

    // Clicking "Add" inserts after the selected vehicle (or appends). If the
    // texture belongs to an auto-consist set, the whole set is one locked unit.
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

    // Like the Pascal MatchOccupancy: only powered units get a driver, and only
    // when the consist has no crew yet (or the unit is placed at the front).
    private void MatchOccupancy(ConsistItem item, int position)
    {
        item.Driver = eDriverType.Nobody;
        var lead = item.Cars.FirstOrDefault();
        if (lead is null) return;

        var tex = _db.TextureForSkin(lead.SkinFile);
        string? cat = tex != null ? CategoryOf(tex) : null;
        if (!IsPoweredCategory(cat))
            return;

        bool staffed = _consist.Any(i =>
            i.Driver is eDriverType.Headdriver or eDriverType.Reardriver);
        if (position == 0 || !staffed)
            item.Driver = eDriverType.Headdriver;
    }

    private static bool IsPoweredCategory(string? c) =>
        c is "e" or "s" or "p" or "z" or "a";

    // Every caption here is read from App.Loc when it is built, so a language
    // switch has to build them again.
    public void RebuildAfterLanguageChange()
    {
        PopulateCategoryCombo();     // -> class combo -> browser list
        PopulateSceneryConsists();
        RebuildConsist();            // -> train stats
        UpdateDetails();
    }

    // ------------------------------------------------------------- scenery combos

    // Lists the consists of the scenery currently selected in the Scenarios tab,
    // so they can be picked and edited here.
    private void PopulateSceneryConsists()
    {
        _suppress = true;
        sceneryConsistList.Items.Clear();

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
                if (trainset.Decor && drivableOnly) continue;

                var entry = new ListBoxItem
                {
                    Content = ConsistListContent(trainset),
                    Tag = trainset
                };
                // right-click: warehouse save/load, clipboard copy/paste, clear-all
                var ts = trainset;
                entry.ContextMenu = ConsistContextMenu.Build(
                    entry,
                    () => ts,
                    vehicles => ApplyConsist(ts, vehicles),
                    () => RemoveAllVehicles(ts));
                sceneryConsistList.Items.Add(entry);

                if (ReferenceEquals(trainset, AppState.Instance.CurrentTrainset))
                    sceneryConsistList.SelectedItem = entry;
            }
        }
        _suppress = false;
    }

    // Selecting a consist on the map loads it into the editor below.
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

    // Shift+Delete clears the selected scenery consist (same as the context-menu
    // "remove all vehicles" entry).
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

    // Replaces a scenery consist's vehicles (from a warehouse preset or a pasted
    // clipboard consist), keeping the consist's map slot (name/track/offset), then
    // loads it into the editor and refreshes the list.
    private void ApplyConsist(Trainset target, List<Dynamic> vehicles)
    {
        target.Vehicles = vehicles;
        AppState.Instance.CurrentScenery = _listScenery;
        AppState.Instance.CurrentTrainset = target;
        LoadTrainset(target);
        PopulateSceneryConsists();
    }

    // Clears every vehicle from a scenery consist.
    private void RemoveAllVehicles(Trainset target)
    {
        target.Vehicles = new List<Dynamic>();
        AppState.Instance.CurrentScenery = _listScenery;
        AppState.Instance.CurrentTrainset = target;
        LoadTrainset(target);
        PopulateSceneryConsists();
    }

    // List caption for a scenery consist — grows with the list width (ellipsis).
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

    // ------------------------------------------------------------ consist edits

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
            Coupling = new Coupling { Flags = Coupling.Mechanical | Coupling.BrakePipe }, // 3
            HasVelocity = true, // emit a canonical "0" velocity in the trainset entry
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
        _consist.Remove(item);
        if (ReferenceEquals(_selected, item)) _selected = null;
        RebuildConsist();
    }

    private void FlipItem(ConsistItem item)
    {
        item.Flipped = !item.Flipped;
        RebuildConsist();
    }

    private void CycleDriver(ConsistItem item)
    {
        item.Driver = item.Driver switch
        {
            eDriverType.Nobody => eDriverType.Headdriver,
            eDriverType.Headdriver => eDriverType.Reardriver,
            eDriverType.Reardriver => eDriverType.Passenger,
            _ => eDriverType.Nobody
        };
        if (ReferenceEquals(_selected, item))
            SyncStartingVehicle();
        RebuildConsist();
        AppState.Instance.NotifyChanged(); // crew change may enable/disable Start
    }

    // Breaks a grouped unit into individual single-car items.
    private void SplitItem(ConsistItem item)
    {
        int i = _consist.IndexOf(item);
        if (i < 0) return;

        _consist.RemoveAt(i);
        for (int c = 0; c < item.Cars.Count; c++)
        {
            _consist.Insert(i + c, new ConsistItem
            {
                Cars = new List<Dynamic> { item.Cars[c] },
                Grouped = false,
                Flipped = item.Flipped,
                Driver = c == 0 ? item.Driver : eDriverType.Nobody
            });
        }
        _selected = _consist[i];
        RebuildConsist();
    }

    // ----------------------------------------------------------- consist render

    private void RebuildConsist()
    {
        consistStack.Children.Clear();
        for (int i = 0; i < _consist.Count; i++)
        {
            var item = _consist[i];
            consistStack.Children.Add(BuildCard(item, i));
            // Coupler between units, plus a trailing coupler after the last unit
            // so end-of-train coupling / lights can be set (#10).
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

    // Flattens the edited consist back onto the bound scenery trainset so the
    // change is reflected on the scenery. Driver goes on each unit's lead car;
    // a flipped unit writes its cars in reverse order.
    private void WriteBackToScenery()
    {
        if (_editingTrainset is null)
            return;

        var flat = new List<Dynamic>();
        foreach (var item in _consist)
        {
            var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse().ToList() : item.Cars;
            for (int k = 0; k < cars.Count; k++)
            {
                cars[k].DriverType = k == 0 ? item.Driver : eDriverType.Nobody;
                flat.Add(cars[k]);
            }
        }
        _editingTrainset.Vehicles = flat;
        RefreshConsistListLabels();
    }

    // Resolves the .fiz physics for a consist car: the .fiz is named after the
    // model, so try the database model first, then the scenery mmd token, then the
    // skin file name.
    private Physics? PhysicsFor(Dynamic car)
    {
        string? dbModel = _db.TextureForSkin(car.SkinFile)?.Model;
        return Physics.For(car.DataFolder, dbModel)
            ?? Physics.For(car.DataFolder, car.MmdFile)
            ?? Physics.For(car.DataFolder, car.SkinFile);
    }

    // Recomputes every coupling from the vehicles' .fiz AllowedFlag, like the
    // original Starter's AutoCoupler: the coupling is the bits common to the
    // facing ends, minus the multiple-unit (control) bit when the control types
    // differ. Called when a vehicle is added or replaced.
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

            // GetMaxCoupler: the end facing the neighbour (swapped when flipped)
            int leftMax = lp == null ? 3 : (lf ? lp.AllowedFlagA : lp.AllowedFlagB);
            int rightMax = rp == null ? 3 : (rf ? rp.AllowedFlagB : rp.AllowedFlagA);
            int common = leftMax & rightMax; // CommonCoupler = shared flag bits

            string lct = lp == null ? "" : (lf ? lp.ControlTypeA : lp.ControlTypeB);
            string rct = rp == null ? "" : (rf ? rp.ControlTypeB : rp.ControlTypeA);
            if ((common & Coupling.ControlMU) != 0 &&
                !string.Equals(lct, rct, StringComparison.OrdinalIgnoreCase))
                common &= ~Coupling.ControlMU;

            left.Coupling.Flags = left.Coupling.Locked ? -common : common;
        }
    }

    // Train totals — same Pascal mass rules as Scenarios (IncludeVehicleToMass + 200 t retry).
    private void UpdateTrainStats()
    {
        if (_editingTrainset is null)
        {
            trainStats.Text = "";
            return;
        }

        trainStats.Text = TrainsetDisplay.FormatStats(
            _editingTrainset,
            PhysicsFor,
            App.Loc["Length"], App.Loc["Mass"], App.Loc["VehicleCount"], App.Loc["Track"],
            car => _db.TextureForSkin(car.SkinFile) is { } t ? CategoryOf(t) : null,
            LoadWeight);
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
            var bmp = GetMiniBitmap(car.MiniName, ConsistThumbHeight) ?? GetMiniBitmap(car.SkinFile, ConsistThumbHeight);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Height = ConsistThumbHeight, Stretch = Stretch.Uniform };
                if (item.Flipped)
                {
                    img.RenderTransform = new ScaleTransform(-1, 1);
                    img.RenderTransformOrigin = RelativePoint.Center;
                }
                minis.Children.Add(img);
            }
            else
            {
                minis.Children.Add(new Border
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
                });
            }
        }

        var nameBlock = new TextBlock
        {
            Text = item.Grouped ? UnitLabel(item) : item.Cars[0].Name,
            FontSize = 11,
            MaxWidth = Math.Max(120, item.Cars.Count * 96),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
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

        var remove = SmallButton("✕", App.Loc["RemoveSelectedVehicle"], () => RemoveItem(item));
        remove.Foreground = new SolidColorBrush(Color.Parse("#E05252"));
        remove.FontWeight = FontWeight.Bold;
        controls.Children.Add(remove);

        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(minis);
        inner.Children.Add(nameBlock);
        inner.Children.Add(controls);

        if (item.Grouped && item.Cars.Count > 1)
        {
            var split = new Button
            {
                Content = App.Loc["Split"],
                FontSize = 11,
                Padding = new Thickness(6, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = _hand
            };
            split.Classes.Add("Basic");
            ToolTip.SetTip(split, App.Loc["TipSplit"]);
            split.Click += (_, _) => SplitItem(item);
            inner.Children.Add(split);
        }

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
            // left button only - right-click is reserved for the context popup
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;
            // Buttons on the card (split / remove) handle their own clicks.
            if (e.Source is Control src && src.GetVisualAncestors().OfType<Button>().Any())
                return;

            // Update selection without RebuildConsist so the pressed card stays
            // alive for a possible drag. Browser sync waits until click/release
            // so BuildList does not run under an active drag capture.
            _selected = item;
            SyncStartingVehicle();
            RefreshCardSelectionChrome();
            UpdateDetails();

            ArmConsistDrag(e, card, index, item);
        };
        card.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        card.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);
        DragDrop.SetAllowDrop(card, true);
        card.AddHandler(DragDrop.DragOverEvent, Consist_OnDragOver);
        card.AddHandler(DragDrop.DropEvent, (s, e) => ConsistCard_OnDrop(e, index, card));
        // right-click a vehicle: wagon number + load actions (copy load from the
        // previous vehicle, fill to the maximum possible load)
        card.ContextMenu = BuildCardMenu(card, item);
        return card;
    }

    private void RefreshCardSelectionChrome()
    {
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

    // Selects, in the left database browser, the category, class and list entry of
    // the given vehicle - so clicking a consist car reveals it in the database.
    private void SelectInBrowser(Dynamic car)
    {
        var texture = _db.TextureForSkin(car.SkinFile);
        if (texture is null)
            return;

        // Prefer the set lead so multi-unit followers (hidden in the browser) still
        // select their lead texture / class.
        var set = _db.ResolveSet(texture);
        var browserTex = set is { Count: > 0 } ? set[0] : texture;

        WhileSyncing(() =>
        {
            ApplyFilters(CategoryOf(browserTex), ClassOf(browserTex));
            BuildList();
            SelectListEntry(browserTex);
        });
    }

    // Mirrors a list pick in the combos, then restores the selection that the
    // rebuilt list dropped.
    private void SyncCombosTo(VehicleTexture texture)
    {
        if (_syncingCombos || !ApplyFilters(CategoryOf(texture), ClassOf(texture)))
            return;

        PostSyncing(() => { BuildList(); SelectListEntry(texture); });
    }

    // Runs work with the sync flag up, so the selections it makes do not come
    // back as another sync.
    private void WhileSyncing(Action work)
    {
        _syncingCombos = true;
        try { work(); }
        finally { _syncingCombos = false; }
    }

    // Same, on the next dispatcher pass: clearing Items from inside a
    // SelectionChanged throws. False when a sync is already pending.
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

    // The "#<n>" number written into the node name (e.g. EU07-005#11). 0 = none.
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

    // Small right-click popup to type a wagon number for the vehicle.
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

    // Coupling indicator between two units (or trailing after the last unit).
    // Click it to open a flyout with the 8-bit coupling editor for the unit's last car.
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
        var d = item.Cars[^1];
        int unitIdx = _consist.IndexOf(item);

        var panel = new StackPanel { Spacing = 2 };

        var copy = new Button
        {
            Content = App.Loc["CopyCoupler"],
            FontSize = 11,
            Padding = new Thickness(6, 1),
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = unitIdx > 0,
            Cursor = _hand
        };
        copy.Classes.Add("Flat");
        copy.Click += (_, _) =>
        {
            if (unitIdx <= 0) return;
            d.Coupling.Flags = _consist[unitIdx - 1].Cars[^1].Coupling.Flags;
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

    private Button SmallButton(string glyph, string tip, Action onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            FontSize = 12,
            Padding = new Thickness(5, 1),
            MinWidth = 0,
            Cursor = _hand
        };
        btn.Classes.Add("Basic");
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button DriverButton(ConsistItem item)
    {
        (string glyph, string color) = item.Driver switch
        {
            eDriverType.Headdriver => ("1", "#4CAF50"),
            eDriverType.Reardriver => ("2", "#FF9800"),
            eDriverType.Passenger => ("P", "#2196F3"),
            _ => ("–", "#888888")
        };

        var btn = SmallButton(glyph, App.Loc["TipDriver"], () => CycleDriver(item));
        btn.Foreground = new SolidColorBrush(Color.Parse(color));
        btn.FontWeight = FontWeight.Bold;
        return btn;
    }

    // Brakes / Loads editors for the selected unit (applied to all of its cars).
    private void UpdateDetails()
    {
        brakesPanel.Children.Clear();
        loadsPanel.Children.Clear();
        damagePanel.Children.Clear();

        selectedVehiclePanel.Header = _selected is null
            ? App.Loc["SelectedVehicle"]
            : $"{App.Loc["SelectedVehicle"]}: " +
              (_selected.Grouped ? UnitLabel(_selected) : _selected.Cars[0].Name);

        // whole-consist load tools sit at the top of the Loads tab, always available
        loadsPanel.Children.Add(BuildConsistLoadTools());

        if (_selected is null)
        {
            brakesPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            loadsPanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            damagePanel.Children.Add(DetailHint(App.Loc["SelectVehicleHint"]));
            return;
        }

        BuildBrakesEditor(_selected);
        BuildLoadsEditor(_selected);
        BuildDamageEditor(_selected);
    }

    // ---------------------------------------------------------------- load tools

    // The per-vehicle right-click menu: wagon number plus the two load shortcuts.
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

    // Button bar that applies a load operation to every vehicle in the consist.
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

    // True when the unit's lead car can actually carry cargo.
    private bool IsLoadable(ConsistItem item)
    {
        var p = PhysicsFor(item.Cars[0]);
        return p != null && (p.MaxLoad > 0 || !string.IsNullOrWhiteSpace(p.LoadAccepted));
    }

    // The largest cargo this car accepts (.fiz MaxLoad), 1000 as a fallback for a
    // car that lists accepted cargo but no explicit maximum, 0 when it can't load.
    private int MaxLoadFor(Dynamic car)
    {
        var p = PhysicsFor(car);
        if (p == null) return 0;
        if (p.MaxLoad > 0) return p.MaxLoad;
        return string.IsNullOrWhiteSpace(p.LoadAccepted) ? 0 : 1000;
    }

    // Cargo types this car accepts (.fiz LoadAccepted), else the global list.
    private List<string> AcceptedTypesFor(Dynamic car)
    {
        var p = PhysicsFor(car);
        if (p != null && !string.IsNullOrWhiteSpace(p.LoadAccepted))
            return p.LoadAccepted.Split(',', ';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        return LoadTypes().ToList();
    }

    // Copies the previous unit's load (type + amount) onto this unit, clamped to
    // each car's own maximum.
    private void CopyLoadFromPrevious(ConsistItem item)
    {
        int idx = _consist.IndexOf(item);
        if (idx <= 0) return;

        var source = _consist[idx - 1].Cars[0];
        foreach (var c in item.Cars)
        {
            int max = MaxLoadFor(c);
            c.LoadType = max > 0 ? source.LoadType : null;
            c.LoadCount = max > 0 ? Math.Min(source.LoadCount, max) : 0;
            if (c.LoadCount > 0) c.HasVelocity = true;
        }
        RebuildConsist();
    }

    // Fills one unit's cars to their maximum load (keeping the cargo type, or
    // assigning the first accepted one when none is set yet).
    private void SetUnitMaxLoad(ConsistItem item)
    {
        foreach (var c in item.Cars)
            FillToMax(c);
        RebuildConsist();
    }

    // Random cargo type for every loadable car in the consist, filled to the max.
    private void RandomLoadTypeForConsist()
    {
        foreach (var c in _consist.SelectMany(u => u.Cars))
        {
            int max = MaxLoadFor(c);
            if (max <= 0) continue;
            var types = AcceptedTypesFor(c);
            if (types.Count == 0) continue;
            c.LoadType = types[_rng.Next(types.Count)];
            c.LoadCount = max;
            c.HasVelocity = true;
        }
        RebuildConsist();
    }

    // Every loadable car in the consist filled to its maximum.
    private void MaxAmountForConsist()
    {
        foreach (var c in _consist.SelectMany(u => u.Cars))
            FillToMax(c);
        RebuildConsist();
    }

    // Random amount (per car) for every loadable car in the consist, keeping the
    // cargo type (assigning the first accepted one when none is set). Lets you roll
    // a different tonnage for each wagon with one click.
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
            c.HasVelocity = true;
        }
        RebuildConsist();
    }

    // Fills a single car to its maximum load (no-op for non-loadable cars).
    private void FillToMax(Dynamic car)
    {
        int max = MaxLoadFor(car);
        if (max <= 0) return;
        if (string.IsNullOrEmpty(car.LoadType))
            car.LoadType = AcceptedTypesFor(car).FirstOrDefault();
        if (string.IsNullOrEmpty(car.LoadType)) return;
        car.LoadCount = max;
        car.HasVelocity = true;
    }

    private static TextBlock DetailHint(string text) => new()
    {
        Text = text, Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap
    };

    // Row of "<label> <control>".
    private static Control LabeledRow(string label, Control control)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12, MinWidth = 120
        });
        sp.Children.Add(control);
        return sp;
    }

    private static void AddOption(ComboBox combo, string content, string? tag)
        => combo.Items.Add(new ComboBoxItem { Content = content, Tag = tag });

    // Brake mode / load adaptation / switch, written into each car's coupling "B" param.
    private void BuildBrakesEditor(ConsistItem item)
    {
        var brake = item.Cars[0].Coupling.GetBrake();

        var modeCombo = new ComboBox { FontSize = 12, MinWidth = 280 };
        AddOption(modeCombo, App.Loc["None"], null);
        foreach (var m in BrakeSetting.Modes) AddOption(modeCombo, BrakeModeLabel(m), m);
        modeCombo.SelectedIndex = brake?.Mode is { } mode
            ? Array.IndexOf(BrakeSetting.Modes, mode) + 1 : 0;

        string?[] loads = { null, "T", "H", "F", "A" };
        var loadCombo = new ComboBox { FontSize = 12, MinWidth = 280 };
        foreach (var l in loads) AddOption(loadCombo, LoadLabel(l), l);
        loadCombo.SelectedIndex = Math.Max(0, Array.IndexOf(loads, brake?.Load));

        string?[] switches = { null, "0", "1", "A" };
        var switchCombo = new ComboBox { FontSize = 12, MinWidth = 280 };
        foreach (var s in switches) AddOption(switchCombo, SwitchLabel(s), s);
        switchCombo.SelectedIndex = Math.Max(0, Array.IndexOf(switches, brake?.Switch));

        void Apply()
        {
            string? selMode = (modeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (selMode is null)
            {
                foreach (var c in item.Cars) c.Coupling.SetBrake(null);
                return;
            }
            var b = new BrakeSetting
            {
                Mode = selMode,
                Load = (loadCombo.SelectedItem as ComboBoxItem)?.Tag as string,
                Switch = (switchCombo.SelectedItem as ComboBoxItem)?.Tag as string
            };
            foreach (var c in item.Cars) c.Coupling.SetBrake(b);
        }

        modeCombo.SelectionChanged += (_, _) => Apply();
        loadCombo.SelectionChanged += (_, _) => Apply();
        switchCombo.SelectionChanged += (_, _) => Apply();

        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeMode"], modeCombo));
        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeLoad"], loadCombo));
        brakesPanel.Children.Add(LabeledRow(App.Loc["BrakeSwitch"], switchCombo));
    }

    // Descriptive labels (what the option does), per the wiki - not the raw code.
    private static string BrakeModeLabel(string code) => code switch
    {
        "G" => App.Loc["BrakeFreight"],
        "P" => App.Loc["BrakePassenger"],
        "R" => App.Loc["BrakeExpress"],
        "R+Mg" => App.Loc["BrakeExpressMg"],
        "Q" => App.Loc["BrakeNoAir"],
        "O" => App.Loc["BrakeOff"],
        "A" => App.Loc["BrakeAuto"],
        _ => code
    };

    private static string LoadLabel(string? code) => code switch
    {
        "T" => App.Loc["LoadEmpty"],
        "H" => App.Loc["LoadMedium"],
        "F" => App.Loc["LoadFull"],
        "A" => App.Loc["LoadAuto"],
        _ => App.Loc["None"]
    };

    private static string SwitchLabel(string? code) => code switch
    {
        "0" => App.Loc["SwitchOff"],
        "1" => App.Loc["SwitchOff10"],
        "A" => App.Loc["SwitchOn"],
        _ => App.Loc["None"]
    };

    // Wheel-damage editor: sway / flat-spot parameters (the "W" coupling prefix).
    private void BuildDamageEditor(ConsistItem item)
    {
        var w = item.Cars[0].Coupling.GetWheels() ?? new WheelSettings();

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
            foreach (var c in item.Cars)
                c.Coupling.SetWheels(ws);
        }

        sway.ValueChanged += (_, _) => Apply();
        flat.ValueChanged += (_, _) => Apply();
        flatRand.ValueChanged += (_, _) => Apply();
        flatProb.ValueChanged += (_, _) => Apply();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowSpacing = 6,
            ColumnSpacing = 8
        };
        void Row(string label, Control control)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var caption = new TextBlock
            {
                Text = label, FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(caption, r);
            Grid.SetColumn(caption, 0);

            control.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);

            grid.Children.Add(caption);
            grid.Children.Add(control);
        }

        Row(App.Loc["DamageSway"], sway);
        Row(App.Loc["DamageFlatness"], flat);
        Row(App.Loc["DamageFlatnessRand"], flatRand);
        Row(App.Loc["DamageFlatnessProb"], flatProb);
        damagePanel.Children.Add(grid);
    }

    // Cargo type + amount, written into each car's node::dynamic trailing params.
    private void BuildLoadsEditor(ConsistItem item)
    {
        var lead = item.Cars[0];
        var phys = PhysicsFor(lead);

        // Prefer the cargo this vehicle actually accepts (.fiz LoadAccepted);
        // otherwise offer everything from data/load_weights.txt.
        var available = phys != null && !string.IsNullOrWhiteSpace(phys.LoadAccepted)
            ? phys.LoadAccepted.Split(',', ';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
            : LoadTypes().ToList();

        // Keep a load already set on the vehicle selectable even when it is not in
        // the accepted/known list (e.g. a custom load read from the scenery).
        if (!string.IsNullOrEmpty(lead.LoadType) &&
            !available.Contains(lead.LoadType!, StringComparer.OrdinalIgnoreCase))
            available.Insert(0, lead.LoadType!);

        // Dropdown of available cargo: the leading "(none)" entry clears the load.
        var typeCombo = new ComboBox { FontSize = 12, MinWidth = 200 };
        typeCombo.Items.Add(new ComboBoxItem { Content = App.Loc["None"], Tag = null });
        foreach (var name in available)
            typeCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });

        // Pre-select the vehicle's current load (or "(none)").
        typeCombo.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(lead.LoadType))
            foreach (var obj in typeCombo.Items)
                if (obj is ComboBoxItem ci && ci.Tag is string t &&
                    string.Equals(t, lead.LoadType, StringComparison.OrdinalIgnoreCase))
                {
                    typeCombo.SelectedItem = ci;
                    break;
                }

        // Cap the amount at the .fiz MaxLoad when it is defined.
        int maxLoad = phys != null && phys.MaxLoad > 0 ? phys.MaxLoad : 1000;
        var countBox = new NumericUpDown
        {
            FontSize = 12, Minimum = 0, Maximum = maxLoad, Increment = 1,
            Value = Math.Min(lead.LoadCount, maxLoad), MinWidth = 120, FormatString = "0"
        };

        void Apply()
        {
            string? type = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            int count = (int)(countBox.Value ?? 0);
            foreach (var c in item.Cars)
            {
                // "(none)" -> no cargo at all; otherwise store the chosen type/amount.
                c.LoadType = string.IsNullOrEmpty(type) ? null : type;
                c.LoadCount = string.IsNullOrEmpty(type) ? 0 : count;
                if (c.LoadCount > 0)
                    c.HasVelocity = true; // velocity token must precede loadcount
            }
        }

        // Wire the handlers only after the initial selection so we don't apply
        // (and mark the consist dirty) while merely populating the editor.
        typeCombo.SelectionChanged += (_, _) => Apply();
        countBox.ValueChanged += (_, _) => Apply();

        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadType"], typeCombo));
        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadCount"], countBox));
    }

    // Cargo names + per-unit weights parsed once from data/load_weights.txt (the
    // file the simulator and the original Starter use). Each line is
    // "<name>: <weight>".
    private static List<string>? _loadTypes;
    private static Dictionary<string, int>? _loadWeights;

    private static void EnsureLoads()
    {
        if (_loadTypes != null)
            return;

        var names = new List<string>();
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = Path.Combine("data", "load_weights.txt");
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path, Encoding.GetEncoding(1250)))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                        continue;
                    int colon = line.IndexOf(':');
                    string name = (colon >= 0 ? line[..colon] : line).Trim();
                    if (name.Length == 0) continue;
                    names.Add(name);
                    if (colon >= 0 && int.TryParse(line[(colon + 1)..].Trim(), out int w))
                        weights[name] = w;
                }
            }
        }
        catch { /* missing / unreadable - just no suggestions */ }

        _loadTypes = names.Distinct(StringComparer.OrdinalIgnoreCase)
                          .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                          .ToList();
        _loadWeights = weights;
    }

    private static IReadOnlyList<string> LoadTypes()
    {
        EnsureLoads();
        return _loadTypes!;
    }

    // Per-unit weight (kg) of a cargo, defaulting to 1000 like the original Starter.
    private static int LoadWeight(string? name)
    {
        EnsureLoads();
        return !string.IsNullOrEmpty(name) && _loadWeights!.TryGetValue(name!, out int w) ? w : 1000;
    }

    // ------------------------------------------------------------------- minis

    // Decodes the miniature down to the requested display height and caches it,
    // so the same skin is never decoded twice and full-resolution bitmaps never
    // stay resident in memory.
    private Bitmap? GetMiniBitmap(string? name, int height)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        string key = $"{name}@{height}";
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

    // ------------------------------------------------------------------ events

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

    private async void RandomTexturesButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_consist.Count == 0) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dlg = BuildTextureRandomizerDialog();
        bool? ok = await dlg.ShowDialog<bool?>(owner);
        if (ok != true) return;

        if (dlg.Tag is not TexRandomizerOpts opts) return;
        var rnd = new TexRandomizer(_db, _rng)
        {
            WithoutArchival = opts.WithoutArchival,
            RevisionTolerance = opts.RevisionTolerance,
            RevYear = opts.RevYear
        };
        if (rnd.Apply(_consist, MakeDynamic) > 0)
        {
            AutoConnectAll();
            RebuildConsist();
        }
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
                // DockPanel: in a horizontal StackPanel the caption gets infinite
                // width, never wraps and shoves the spinner off the edge.
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
        string path = TexRandomizer.RulesPath;
        string text = "";
        try
        {
            if (File.Exists(path))
                text = File.ReadAllText(path);
        }
        catch { }

        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = StarterNG.Infrastructure.MonoFont.Family,
            FontSize = 12
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var win = new Window
        {
            Title = App.Loc["RandomizeTexturesRules"],
            Width = 480,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var save = new Button { Content = App.Loc["RandomizeTexturesRulesSave"], Cursor = _hand };
        save.Classes.Add("Accent");
        save.Click += (_, _) =>
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, box.Text ?? "");
            }
            catch { }
            win.Close();
        };

        var cancel = new Button { Content = App.Loc["Cancel"], Cursor = _hand };
        cancel.Classes.Add("Flat");
        cancel.Click += (_, _) => win.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { save, cancel }
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        win.Content = new DockPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                buttons,
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = App.Loc["RandomizeTexturesRulesHint"],
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.8,
                            FontSize = 11
                        },
                        box
                    }
                }
            }
        };

        await win.ShowDialog(owner);
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

    // Shuffle wagon order from the selected unit to the end (multi-unit groups stay put).
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

    // Randomly flip units from the selected one to the end of the consist.
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

    // ── drag / drop (browser → consist, reorder inside consist) ────────────

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
        // Resolve the row under the pointer so a drag can start before selection settles.
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

    private void ArmConsistDrag(PointerPressedEventArgs e, Visual relativeTo, int index, ConsistItem item)
    {
        ClearPendingDrag();
        _pendingDragPress = e;
        _pendingDragOrigin = e.GetPosition(relativeTo);
        _pendingDragTexture = null;
        _pendingDragConsistIndex = index;
        _pendingBrowserSync = item;
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
        // Keep browser sync cancelled for the drag session; click path uses it.
        _pendingBrowserSync = null;
        _pendingDragPress = null;
        _dragSessionActive = true;

        if (texture != null)
        {
            _dragTexture = texture;
            _dragConsistIndex = -1;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(DragVehicleFormat));
            try
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
            }
            finally
            {
                _dragTexture = null;
                ClearPendingDrag();
            }
            return;
        }

        if (consistIndex >= 0)
        {
            _dragTexture = null;
            _dragConsistIndex = consistIndex;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(DragConsistFormat));
            try
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
            }
            finally
            {
                _dragConsistIndex = -1;
                ClearPendingDrag();
            }
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
        // Plain click on a consist card: reveal it in the vehicle browser.
        if (sync is { Cars.Count: > 0 })
            SelectInBrowser(sync.Cars[0]);
    }

    private void Consist_OnDragOver(object? sender, DragEventArgs e)
    {
        // Payload is kept in fields set before DoDragDropAsync; formats are markers.
        if (_dragTexture != null || _dragConsistIndex >= 0)
        {
            e.DragEffects = _dragConsistIndex >= 0 ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Consist_OnDrop(object? sender, DragEventArgs e)
    {
        if (_dragTexture != null)
        {
            InsertTextureAt(_dragTexture, _consist.Count);
            e.Handled = true;
        }
        else if (_dragConsistIndex >= 0)
        {
            MoveConsistUnit(_dragConsistIndex, _consist.Count);
            e.Handled = true;
        }
    }

    private void ConsistCard_OnDrop(DragEventArgs e, int targetIndex, Control card)
    {
        double x = e.GetPosition(card).X;
        bool after = x > card.Bounds.Width / 2;
        int insertAt = after ? targetIndex + 1 : targetIndex;

        if (_dragConsistIndex >= 0)
        {
            MoveConsistUnit(_dragConsistIndex, insertAt);
            e.Handled = true;
            return;
        }

        if (_dragTexture != null)
        {
            InsertTextureAt(_dragTexture, insertAt);
            e.Handled = true;
        }
    }

    private void MoveConsistUnit(int from, int to)
    {
        if (from < 0 || from >= _consist.Count) return;
        if (to > from) to--; // account for removal shifting indices
        to = Math.Clamp(to, 0, _consist.Count - 1);
        if (from == to) return;

        var item = _consist[from];
        _consist.RemoveAt(from);
        _consist.Insert(to, item);
        _selected = item;
        AutoConnectAll();
        RebuildConsist();
    }

}
