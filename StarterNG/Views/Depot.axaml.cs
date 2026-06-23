using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // suppresses combo SelectionChanged handlers while we populate them
    private bool _suppress;

    // debounces search input so we don't rebuild on every keystroke
    private DispatcherTimer? _searchTimer;

    // scenery-consist previews are built lazily on first dropdown open
    private bool _consistPreviewsBuilt;

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

    // Coupling-bit labels in mask-bit order (bit i = value 1<<i), per the wiki:
    // https://wiki.eu07.pl/index.php?title=Wpisy_hamulca_dla_pojazdow
    private static readonly string[] CouplingBits =
        { "Mechanical", "Brake pipe", "Control (MU)", "High voltage",
          "Gangway", "Aux pneumatic", "Heating", "Workshop lock" };

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
            PopulateSceneryCombo();    // -> populates consist combo for the first scenery
            RebuildConsist();
        }, DispatcherPriority.Background);

        // When the tab is shown, pick up the consist selected in the scenery view.
        AttachedToVisualTree += (_, _) => SyncFromSelection();
    }

    private void SyncFromSelection()
    {
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
        RebuildConsist();
    }

    // ------------------------------------------------------------- scenery combos

    private void PopulateSceneryCombo()
    {
        _suppress = true;
        sceneryCombo.Items.Clear();
        foreach (var scenery in _sceneries)
        {
            sceneryCombo.Items.Add(new ComboBoxItem
            {
                Content = Path.GetFileNameWithoutExtension(scenery.Path),
                Tag = scenery
            });
        }
        if (sceneryCombo.Items.Count > 0)
            sceneryCombo.SelectedIndex = 0;
        _suppress = false;

        PopulateConsistCombo(_sceneries.FirstOrDefault());
    }

    private void PopulateConsistCombo(Scenery? scenery)
    {
        _suppress = true;
        sceneryConsistCombo.Items.Clear();

        if (scenery != null)
        {
            foreach (var trainset in scenery.Trainsets)
            {
                if (trainset.Vehicles.Count == 0)
                    continue;
                if (trainset.Vehicles.All(v => string.IsNullOrWhiteSpace(v.Name)))
                    continue;

                // lightweight text content now; the rich mini-preview is built
                // lazily the first time the dropdown is opened
                sceneryConsistCombo.Items.Add(new ComboBoxItem
                {
                    Content = ConsistSummary(trainset),
                    Tag = trainset
                });
            }
        }

        _consistPreviewsBuilt = false;
        sceneryConsistCombo.SelectedItem = null;
        _suppress = false;
    }

    private static string ConsistSummary(Trainset trainset)
    {
        string text = string.Join(" + ", trainset.Vehicles.Select(v => v.SkinFile));
        return text.Length > 70 ? text[..67] + "..." : text;
    }

    // Builds the mini-thumbnail previews only once, on first dropdown open.
    private void SceneryConsistCombo_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (_consistPreviewsBuilt) return;
        _consistPreviewsBuilt = true;

        foreach (var obj in sceneryConsistCombo.Items)
            if (obj is ComboBoxItem { Tag: Trainset trainset } item)
                item.Content = BuildConsistPreview(trainset);
    }

    // Combo item: row of mini thumbnails on top, skin names underneath.
    private Control BuildConsistPreview(Trainset trainset)
    {
        var minis = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var names = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var v in trainset.Vehicles)
        {
            var bmp = GetMiniBitmap(_db.MiniForSkin(v.SkinFile) ?? v.MiniName, 20);
            if (bmp != null)
                minis.Children.Add(new Image { Source = bmp, Height = 20, Stretch = Stretch.Uniform });
            else
                minis.Children.Add(new Border
                {
                    Height = 20, Width = 32, Background = Placeholder, CornerRadius = new CornerRadius(3)
                });

            names.Children.Add(new TextBlock { Text = v.SkinFile, FontSize = 10, Opacity = 0.8 });
        }

        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(minis);
        panel.Children.Add(names);
        return panel;
    }

    private void SceneryCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var scenery = (sceneryCombo.SelectedItem as ComboBoxItem)?.Tag as Scenery;
        PopulateConsistCombo(scenery);
    }

    // Selecting a scenery consist binds the editor to that trainset.
    private void SceneryConsistCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (sceneryConsistCombo.SelectedItem is not ComboBoxItem { Tag: Trainset trainset })
            return;

        AppState.Instance.CurrentScenery = (sceneryCombo.SelectedItem as ComboBoxItem)?.Tag as Scenery;
        AppState.Instance.CurrentTrainset = trainset;
        LoadTrainset(trainset);
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
        UpdateSummary();
        UpdateDetails();
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
        card.PointerPressed += (_, _) =>
        {
            _selected = item;
            RebuildConsist();
        };
        return card;
    }

    private static string UnitLabel(ConsistItem item)
    {
        string key = UnitKey(item.Cars[0]);
        return item.Cars.Count > 1 ? $"{key}  [{item.Cars.Count}]" : key;
    }

    // Coupling indicator between two units. Hovering shows the 8-bit coupling
    // editor for the left unit's last car (placeholder; full editing comes later).
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
        var coupler = new Border
        {
            Width = 18,
            Padding = new Thickness(2),
            Cursor = _hand,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Child = glyph
        };
        ToolTip.SetTip(coupler, BuildCouplingBox(item.Cars[^1]));
        ToolTip.SetShowDelay(coupler, 150);
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

        for (int i = 0; i < CouplingBits.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = CouplingBits[i],
                IsChecked = d.Coupling.Has(bit),
                FontSize = 11
            };
            // edits the shared Dynamic, so the change is written back on export
            check.IsCheckedChanged += (_, _) => d.Coupling.Set(bit, check.IsChecked == true);
            panel.Children.Add(check);
        }

        // Brake-rack setting (the "B" parameter of the coupling field).
        panel.Children.Add(new TextBlock
        {
            Text = "Brake mode",
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 2)
        });

        var brakeCombo = new ComboBox { FontSize = 11, MinWidth = 90 };
        brakeCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
        foreach (string mode in BrakeSetting.Modes)
            brakeCombo.Items.Add(new ComboBoxItem { Content = mode, Tag = mode });

        string? current = d.Coupling.GetBrake()?.Mode;
        brakeCombo.SelectedIndex = current is null
            ? 0
            : Array.IndexOf(BrakeSetting.Modes, current) + 1;

        brakeCombo.SelectionChanged += (_, _) =>
        {
            if ((brakeCombo.SelectedItem as ComboBoxItem)?.Tag is not string mode)
            {
                d.Coupling.SetBrake(null);
            }
            else
            {
                var brake = d.Coupling.GetBrake() ?? new BrakeSetting();
                brake.Mode = mode;
                d.Coupling.SetBrake(brake);
            }
        };
        panel.Children.Add(brakeCombo);

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

    private void UpdateSummary()
    {
        int cars = _consist.Sum(i => i.Cars.Count);
        int crewed = _consist.Count(i => i.Driver is eDriverType.Headdriver or eDriverType.Reardriver);
        summaryLabel.Text = $"{cars} {App.Loc["Vehicles"]}  ·  {crewed} {App.Loc["Crew"]}";
    }

    // Brakes / Loads tabs are placeholders bound to the selected unit.
    private void UpdateDetails()
    {
        if (_selected is null)
        {
            brakesContent.Text = App.Loc["SelectVehicleHint"];
            loadsContent.Text = App.Loc["SelectVehicleHint"];
            return;
        }

        var lead = _selected.Cars[0];
        string title = _selected.Grouped ? UnitLabel(_selected) : lead.Name;
        brakesContent.Text =
            $"{App.Loc["Vehicle"]}: {title}\n" +
            $"cars: {string.Join(", ", _selected.Cars.Select(c => c.SkinFile))}\n\n" +
            $"{App.Loc["ComingSoon"]}";
        loadsContent.Text =
            $"{App.Loc["Vehicle"]}: {title}\n\n" +
            $"{App.Loc["ComingSoon"]}";
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

    private void NewConsist_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // detach from any scenery trainset - this consist is now standalone
        _editingTrainset = null;
        AppState.Instance.CurrentTrainset = null;
        _consist.Clear();
        _selected = null;
        _suppress = true;
        sceneryConsistCombo.SelectedItem = null;
        _suppress = false;
        RebuildConsist();
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
