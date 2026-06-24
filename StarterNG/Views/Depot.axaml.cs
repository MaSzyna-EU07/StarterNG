using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using StarterNG.Classes;

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
    private const int ConsistThumbHeight = 34;

    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private int _nameCounter;

    // shared RNG for the "random load / random amount" consist tools
    private static readonly Random _rng = new();

    // suppresses combo SelectionChanged handlers while we populate them
    private bool _suppress;

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
            PopulateCategoryCombo();   // -> populates class combo -> builds browser
            PopulateSceneryConsists(); // -> lists the current scenery's consists
            RebuildConsist();
        }, DispatcherPriority.Background);

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
        // refresh the on-map consists list to the scenery picked in the Scenarios tab
        PopulateSceneryConsists();

        var trainset = AppState.Instance.CurrentTrainset;
        if (trainset != null && !ReferenceEquals(trainset, _editingTrainset))
            LoadTrainset(trainset);
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
                        Driver = group.Select(c => c.DriverType).FirstOrDefault(d => d != eDriverType.Nobody)
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
                        Driver = group.Select(c => c.DriverType).FirstOrDefault(d => d != eDriverType.Nobody)
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
        RebuildConsist();
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
        // No "All" entry: an unselected class means "every class in the category".
        _suppress = true;
        classCombo.Items.Clear();
        foreach (string cls in ClassesForCategory(_categoryFilter))
            classCombo.Items.Add(new ComboBoxItem { Content = cls, Tag = cls });
        classCombo.SelectedIndex = -1;
        _suppress = false;

        _classFilter = null;
        BuildList();
    }

    private IEnumerable<string> ClassesForCategory(Func<string?, bool>? category) =>
        _db.Textures
            .Where(t => category == null || category(CategoryOf(t)))
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
        _classFilter = (classCombo.SelectedItem as ComboBoxItem)?.Tag as string;
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
    // Starter). Empty by default: an entry only appears once a category is chosen
    // or a search is typed. Picking one drives the mini preview and Add button.
    private void BuildList()
    {
        // reset selection-dependent UI
        _browserSelected = null;
        miniPreview.Source = null;
        addVehicleButton.IsEnabled = false;

        vehicleListBox.Items.Clear();

        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        // nothing selected and nothing searched -> keep the list empty
        if (_categoryFilter == null && !hasSearch)
            return;

        var matched = _db.Textures
            .Where(t => PassesFilters(t, search, hasSearch))
            .OrderBy(t => t.TextureMini ?? t.Skinfile, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListRows)
            .ToList();

        foreach (var texture in matched)
        {
            vehicleListBox.Items.Add(new ListBoxItem
            {
                Content = BrowserLabel(texture, _db.ResolveSet(texture)),
                Tag = texture
            });
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
            miniPreview.Source = GetMiniBitmap(_db.ResolveMiniName(texture), 54);
            addVehicleButton.IsEnabled = true;
        }
        else
        {
            _browserSelected = null;
            miniPreview.Source = null;
            addVehicleButton.IsEnabled = false;
        }
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

    private bool PassesFilters(VehicleTexture t, string search, bool hasSearch)
    {
        if (_categoryFilter != null && !_categoryFilter(CategoryOf(t)))
            return false;

        if (_classFilter != null &&
            !string.Equals(ClassOf(t), _classFilter, StringComparison.OrdinalIgnoreCase))
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
        string name = !string.IsNullOrEmpty(texture.TextureMini)
            ? texture.TextureMini!
            : Base(texture.Skinfile);
        if (!string.IsNullOrEmpty(texture.Meta?.Operator))
            name += $"  ·  {texture.Meta!.Operator}";
        if (set is { Count: > 1 })
            name += $"   [{set.Count}]";
        return name;
    }

    // Clicking "Add" appends to the consist. If the texture belongs to an
    // auto-consist set, the whole set is added as one locked unit; otherwise the
    // single vehicle is added.
    private void AddTexture(VehicleTexture texture)
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

        // insert after the selected vehicle, or append when nothing is selected
        int at = _consist.Count;
        if (_selected != null)
        {
            int si = _consist.IndexOf(_selected);
            if (si >= 0) at = si + 1;
        }
        _consist.Insert(at, item);
        _selected = item;
        AutoConnectAll();
        RebuildConsist();
    }

    // ------------------------------------------------------------- scenery combos

    // Lists the consists of the scenery currently selected in the Scenarios tab,
    // so they can be picked and edited here.
    private void PopulateSceneryConsists()
    {
        _suppress = true;
        sceneryConsistList.Items.Clear();

        _listScenery = AppState.Instance.CurrentScenery ?? _sceneries.FirstOrDefault();
        mapSceneryLabel.Text = _listScenery != null
            ? Path.GetFileNameWithoutExtension(_listScenery.Path)
            : App.Loc["NoSceneryConsists"];

        if (_listScenery != null)
        {
            foreach (var trainset in _listScenery.Trainsets)
            {
                if (trainset.Vehicles.Count == 0)
                    continue;
                if (trainset.Vehicles.All(v => string.IsNullOrWhiteSpace(v.Name)))
                    continue;

                var entry = new ListBoxItem
                {
                    Content = ConsistLabel(trainset),
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
        LoadTrainset(trainset);
    }

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

    // Plain-text label for a scenery consist (no thumbnails).
    private static string ConsistLabel(Trainset trainset)
    {
        string text = string.Join(" + ", trainset.Vehicles.Select(v => v.SkinFile));
        if (string.IsNullOrWhiteSpace(text))
            text = string.Join(" + ", trainset.Vehicles.Select(v => v.Name));
        return text.Length > 80 ? text[..77] + "…" : text;
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
        return $"{baseName}_{++_nameCounter}";
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
        RebuildConsist();
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
        // The first vehicle drives from cab A by default (unless a crew is set).
        if (_consist.Count > 0 && _consist.All(i => i.Driver == eDriverType.Nobody))
            _consist[0].Driver = eDriverType.Headdriver;

        consistStack.Children.Clear();
        for (int i = 0; i < _consist.Count; i++)
        {
            var item = _consist[i];
            consistStack.Children.Add(BuildCard(item));
            if (i < _consist.Count - 1)
                consistStack.Children.Add(BuildCoupler(item));
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

    // Train totals (length / mass / Vmax / load) read from the .fiz files.
    private void UpdateTrainStats()
    {
        if (_consist.Count == 0)
        {
            trainStats.Text = "";
            return;
        }

        double lengthM = 0, massKg = 0, loadKg = 0;
        double vmax = double.PositiveInfinity;

        foreach (var item in _consist)
            foreach (var c in item.Cars)
            {
                var p = PhysicsFor(c);
                if (p != null)
                {
                    lengthM += p.Length;
                    massKg += p.Mass;
                    if (p.VMax > 0) vmax = Math.Min(vmax, p.VMax);
                }
                if (c.LoadCount > 0 && !string.IsNullOrEmpty(c.LoadType))
                    loadKg += (double)c.LoadCount * LoadWeight(c.LoadType);
            }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string v = double.IsInfinity(vmax) ? "—" : $"{vmax.ToString("0", inv)} km/h";
        trainStats.Text =
            $"{App.Loc["Length"]}: {lengthM.ToString("0.#", inv)} m    ·    " +
            $"{App.Loc["Mass"]}: {(massKg / 1000.0).ToString("0.#", inv)} t    ·    " +
            $"Vmax: {v}    ·    " +
            $"{App.Loc["Load"]}: {(loadKg / 1000.0).ToString("0.#", inv)} t";
    }

    private Control BuildCard(ConsistItem item)
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
        controls.Children.Add(SmallButton("✕", App.Loc["TipRemove"], () => RemoveItem(item)));
        controls.Children.Add(SmallButton("▶", App.Loc["TipMoveRight"], () => MoveRight(item)));

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
            Child = inner
        };
        card.PointerPressed += (_, e) =>
        {
            // left button only - right-click is reserved for the context popup,
            // and rebuilding here would detach the card it anchors to
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;

            _selected = item;
            RebuildConsist();
            // mirror the click into the database browser: pick the category and
            // highlight this vehicle's entry there
            SelectInBrowser(item.Cars[0]);
        };
        // right-click a vehicle: wagon number + load actions (copy load from the
        // previous vehicle, fill to the maximum possible load)
        card.ContextMenu = BuildCardMenu(card, item);
        return card;
    }

    // Selects, in the left database browser, the category and the list entry of the
    // given vehicle - so clicking a consist car reveals it in the database.
    private void SelectInBrowser(Dynamic car)
    {
        var texture = _db.TextureForSkin(car.SkinFile);
        if (texture is null)
            return;

        string? category = CategoryOf(texture);
        var catItem = categoryCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(it => it.Tag is Func<string?, bool> f && f(category));

        if (catItem != null && !ReferenceEquals(categoryCombo.SelectedItem, catItem))
        {
            categoryCombo.SelectedItem = catItem; // -> rebuilds class combo (class=all) + list
        }
        else
        {
            // same category already chosen: drop any class filter so the entry shows
            _suppress = true;
            classCombo.SelectedIndex = -1;
            _suppress = false;
            _classFilter = null;
            BuildList();
        }

        // highlight the matching entry (selection drives the mini + Add button)
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

    // Coupling indicator between two units. Click it to open a (clickable) flyout
    // with the 8-bit coupling editor for the left unit's last car - a flyout, not a
    // tooltip, so it stays open while you toggle the options.
    private Control BuildCoupler(ConsistItem item)
    {
        var glyph = new TextBlock
        {
            Text = "≣",
            FontSize = 18,
            Opacity = 0.7,
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
                Content = BuildCouplingBox(item.Cars[^1]),
                Placement = PlacementMode.Top
            }
        };
        coupler.Classes.Add("Basic");
        ToolTip.SetTip(coupler, App.Loc["Coupling"]);
        return coupler;
    }

    private Control BuildCouplingBox(Dynamic d)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["Coupling"],
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });

        for (int i = 0; i < CouplingBitKeys.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = App.Loc[CouplingBitKeys[i]],
                IsChecked = d.Coupling.Has(bit),
                FontSize = 11
            };
            // edits the shared Dynamic, so the change is written back on export
            check.IsCheckedChanged += (_, _) => d.Coupling.Set(bit, check.IsChecked == true);
            panel.Children.Add(check);
        }

        // The brake setting lives in the Brakes tab now, not here.
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
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["ConsistLoadTools"], FontWeight = FontWeight.Bold, FontSize = 12
        });

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

    private static TextBlock UnitTitle(ConsistItem item) => new()
    {
        Text = item.Grouped ? UnitLabel(item) : item.Cars[0].Name,
        FontWeight = FontWeight.Bold, FontSize = 12
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

        brakesPanel.Children.Add(UnitTitle(item));
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
            Value = value, MinWidth = 120, FormatString = "0"
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

        damagePanel.Children.Add(UnitTitle(item));
        damagePanel.Children.Add(LabeledRow(App.Loc["DamageSway"], sway));
        damagePanel.Children.Add(LabeledRow(App.Loc["DamageFlatness"], flat));
        damagePanel.Children.Add(LabeledRow(App.Loc["DamageFlatnessRand"], flatRand));
        damagePanel.Children.Add(LabeledRow(App.Loc["DamageFlatnessProb"], flatProb));
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

        loadsPanel.Children.Add(UnitTitle(item));
        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadType"], typeCombo));
        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadCount"], countBox));
        loadsPanel.Children.Add(new TextBlock
        {
            Text = App.Loc["LoadHint"], Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
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

}

// One entry in the consist: a single vehicle or a locked multi-car unit.
public sealed class ConsistItem
{
    public List<Dynamic> Cars { get; set; } = new();
    public bool Grouped { get; set; }
    public bool Flipped { get; set; }
    public eDriverType Driver { get; set; } = eDriverType.Nobody;
}
