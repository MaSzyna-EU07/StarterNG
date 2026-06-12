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
    private const int BrowserThumbHeight = 38;
    private const int ConsistThumbHeight = 50;

    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private int _nameCounter;

    // suppresses combo SelectionChanged handlers while we populate them
    private bool _suppress;

    // debounces search input so we don't rebuild on every keystroke
    private DispatcherTimer? _searchTimer;

    // scenery-consist previews are built lazily on first dropdown open
    private bool _consistPreviewsBuilt;

    // auto-expand search results only when there are few of them
    private const int AutoExpandLimit = 40;
    private const int MaxRowsPerGroup = 250;

    // active database filters
    private Func<string?, bool>? _categoryFilter;  // matches a category letter, null = all
    private string? _classFilter;                  // group mini (e.g. EU07), null = all

    // card highlight brushes (theme-neutral overlays)
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#33888888"));
    private static readonly IBrush CardBorderSel = new SolidColorBrush(Color.Parse("#AA4D8BFF"));
    private static readonly IBrush CardBgSel = new SolidColorBrush(Color.Parse("#224D8BFF"));
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
        ("CatWagons",       IsWagonCat),
        ("CatOther",        IsOtherCat),
    };

    // lowercase letters that already have a named type (so they aren't "other")
    private static readonly HashSet<string> NamedLowerCats =
        new(StringComparer.Ordinal) { "a", "b", "c", "d", "e", "f", "h", "n", "o", "p", "r", "s", "t", "x", "z" };

    private static bool IsWagonCat(string? c) => c is { Length: 1 } && c[0] >= 'A' && c[0] <= 'Z';
    private static bool IsOtherCat(string? c) =>
        c is { Length: 1 } && char.IsLower(c[0]) && !NamedLowerCats.Contains(c);

    // Car-type suffixes (longest first) used only for the consist unit label.
    private static readonly string[] CarSuffixes = { "sa", "sb", "ra", "rb", "s" };

    // Placeholder coupling-bit labels (couplingData byte). Finished later.
    private static readonly string[] CouplingBits =
        { "Coupler", "Brake pipe", "Brake tank", "Control", "Gangway", "High voltage", "Heating", "Aux" };

    public Depot()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            BuildBrowser();
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
    private string ClassOf(VehicleTexture t)
    {
        if (t.Group != null && _db.GroupsById.TryGetValue(t.Group, out var g) && !string.IsNullOrEmpty(g.Mini))
            return g.Mini!;
        return t.MiniRef ?? "";
    }

    // Vehicle type = the group's "category" letter.
    private string? CategoryOf(VehicleTexture t) =>
        t.Group != null && _db.GroupsById.TryGetValue(t.Group, out var g) ? g.Category : null;

    // ------------------------------------------------------------ category combo

    private void PopulateCategoryCombo()
    {
        _suppress = true;
        categoryCombo.Items.Clear();
        categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc["All"], Tag = null });
        foreach (var (locKey, match) in CategoryDefs)
            categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc[locKey], Tag = match });
        categoryCombo.SelectedIndex = 0;
        _suppress = false;

        _categoryFilter = null;
        RebuildClassCombo();
    }

    private void RebuildClassCombo()
    {
        _suppress = true;
        classCombo.Items.Clear();
        classCombo.Items.Add(new ComboBoxItem { Content = App.Loc["All"], Tag = null });

        foreach (string cls in ClassesForCategory(_categoryFilter))
            classCombo.Items.Add(new ComboBoxItem { Content = cls, Tag = cls });

        classCombo.SelectedIndex = 0;
        _suppress = false;

        _classFilter = null;
        BuildBrowser();
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
        BuildBrowser();
    }

    // ----------------------------------------------------------- vehicle browser

    // Browser groups by the JSON "group" field, rendered as collapsed expanders.
    // Collapsed groups hold no controls, so the list stays light and smooth.
    private void BuildBrowser()
    {
        browserStack.Children.Clear();

        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        var matched = _db.Textures.Where(t => PassesFilters(t, search, hasSearch)).ToList();

        // Only auto-open groups when the search is specific. A broad search keeps
        // groups folded (just headers + counts) so we never realize thousands of
        // rows / decode thousands of bitmaps at once.
        bool autoExpand = hasSearch && matched.Count <= AutoExpandLimit;

        // fold by class (the group's mini)
        var grouped = matched
            .GroupBy(ClassOf)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var list = group
                .OrderBy(t => t.TextureMini ?? t.Skinfile, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string header = string.IsNullOrEmpty(group.Key)
                ? App.Loc["Others"]
                : group.Key;

            var content = new StackPanel { Spacing = 2 };
            var expander = new Expander
            {
                Header = $"{header}  ({list.Count})",
                IsExpanded = autoExpand,   // folded by default
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            expander.Content = content;

            bool populated = false;
            void Populate()
            {
                if (populated) return;
                populated = true;

                int shown = Math.Min(list.Count, MaxRowsPerGroup);
                for (int i = 0; i < shown; i++)
                    content.Children.Add(BuildBrowserItem(list[i]));

                if (list.Count > shown)
                    content.Children.Add(new TextBlock
                    {
                        Text = $"… +{list.Count - shown}",
                        Opacity = 0.6,
                        Margin = new Thickness(6, 2)
                    });
            }

            if (expander.IsExpanded)
                Populate();
            expander.Expanded += (_, _) => Populate();

            browserStack.Children.Add(expander);
        }

        if (browserStack.Children.Count == 0)
        {
            browserStack.Children.Add(new TextBlock
            {
                Text = App.Loc["NoVehicles"],
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4)
            });
        }
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

    private Control BuildBrowserItem(VehicleTexture texture)
    {
        // Grid with a star label column keeps the label width constrained so it
        // wraps instead of overlapping the Add button.
        var grid = new Grid
        {
            Margin = new Thickness(2, 1),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };
        ToolTip.SetTip(grid, texture.FullPath);

        var thumb = CreateLazyMini(_db.ResolveMiniName(texture), BrowserThumbHeight, 64);
        thumb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(thumb, 0);

        var set = _db.ResolveSet(texture);
        var label = new TextBlock
        {
            Text = BrowserLabel(texture, set),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(label, 1);

        var add = new Button
        {
            Content = App.Loc["AddVehicle"],
            FontSize = 11,
            Padding = new Thickness(10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = _hand
        };
        add.Classes.Add("Flat");
        ToolTip.SetTip(add, set != null ? App.Loc["TipAddUnit"] : App.Loc["TipAddVehicle"]);
        add.Click += (_, _) => AddTexture(texture);
        Grid.SetColumn(add, 2);

        grid.Children.Add(thumb);
        grid.Children.Add(label);
        grid.Children.Add(add);

        // double-click the row to replace the selected consist vehicle
        grid.DoubleTapped += (_, _) => ReplaceSelected(texture);
        return grid;
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

    // An image that decodes its mini only while it is on screen, and releases it
    // when scrolled out of view, so an open group never holds every bitmap at once.
    private Image CreateLazyMini(string? miniName, int height, double width)
    {
        var img = new Image { Height = height, Width = width, Stretch = Stretch.Uniform };
        bool loaded = false;
        img.EffectiveViewportChanged += (_, e) =>
        {
            bool visible = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
            if (visible && !loaded)
            {
                img.Source = GetMiniBitmap(miniName, height);
                loaded = true;
            }
            else if (!visible && loaded)
            {
                img.Source = null;
                loaded = false;
            }
        };
        return img;
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
            var bmp = GetMiniBitmap(_db.MiniForSkin(v.SkinFile) ?? v.MiniName, 28);
            if (bmp != null)
                minis.Children.Add(new Image { Source = bmp, Height = 28, Stretch = Stretch.Uniform });
            else
                minis.Children.Add(new Border
                {
                    Height = 28, Width = 46, Background = Placeholder, CornerRadius = new CornerRadius(3)
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
            RangeMin = -1,
            Name = MakeUniqueName(skin),
            DataFolder = StripDynamicPrefix(texture.Directory),
            SkinFile = skin,
            MmdFile = string.IsNullOrEmpty(texture.Model) ? skin : texture.Model!,
            Offset = 0f,
            DriverType = eDriverType.Nobody,
            couplingData = 3,
            Tail = "",
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
            panel.Children.Add(new CheckBox
            {
                Content = CouplingBits[i],
                IsChecked = (d.couplingData & (1 << i)) != 0,
                FontSize = 11
            });
        }

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
