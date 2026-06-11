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
using StarterNG.Classes;

namespace StarterNG.Views;

public partial class Depot : UserControl
{
    private readonly VehicleDatabase _db = new();
    private readonly List<Scenery> _sceneries = new();

    // The consist currently being edited (belongs to the selected scenery).
    private readonly List<Dynamic> _consist = new();
    private Dynamic? _selected;

    private readonly Dictionary<string, Bitmap?> _miniCache = new();
    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private int _nameCounter;

    // suppresses combo SelectionChanged handlers while we populate them
    private bool _suppress;

    // active database filters
    private string[]? _categoryFilter;   // legacy category letters, null = all
    private string? _classFilter;        // group mini, null = all

    // card highlight brushes (theme-neutral overlays)
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#33888888"));
    private static readonly IBrush CardBorderSel = new SolidColorBrush(Color.Parse("#AA4D8BFF"));
    private static readonly IBrush CardBgSel = new SolidColorBrush(Color.Parse("#224D8BFF"));

    // High-level category -> legacy category letters (from the JSON "category" field).
    // NOTE: adjust these letters to match the actual database conventions.
    private static readonly (string LocKey, string[] Letters)[] CategoryMap =
    {
        ("CatElectric",    new[] { "e" }),       // electric locomotives
        ("CatDiesel",      new[] { "s" }),       // diesel locomotives
        ("CatEMU",         new[] { "z" }),       // electric multiple units (EN57, ...)
        ("CatDMU",         new[] { "d" }),       // diesel multiple units
        ("CatCarriagesA",  new[] { "o" }),       // passenger carriages
        ("CatCarriagesB",  new[] { "t" }),       // freight carriages
    };

    // Placeholder coupling-bit labels (couplingData byte). Finished later.
    private static readonly string[] CouplingBits =
        { "Coupler", "Brake pipe", "Brake tank", "Control", "Gangway", "High voltage", "Heating", "Aux" };

    public Depot()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        LoadDatabase();
        LoadSceneries();

        PopulateCategoryCombo();   // -> populates class combo -> builds browser
        PopulateSceneryCombo();    // -> populates consist combo for the first scenery
        RebuildConsist();
    }

    // ----------------------------------------------------------------- loading

    private void LoadDatabase()
    {
        try { _db.Load("databases/vehicles/"); }
        catch { /* leave database empty on failure */ }
    }

    private void LoadSceneries()
    {
        try
        {
            if (!Directory.Exists("scenery/"))
                return;

            foreach (string scn in Directory.GetFiles("scenery/", "*.scn"))
            {
                if (Path.GetFileName(scn).StartsWith("$"))
                    continue;
                try { _sceneries.Add(new Scenery(scn)); }
                catch { /* skip unreadable scenery */ }
            }
        }
        catch { /* no scenery folder */ }
    }

    // ------------------------------------------------------------ category combo

    private void PopulateCategoryCombo()
    {
        _suppress = true;
        categoryCombo.Items.Clear();
        categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc["All"], Tag = null });
        foreach (var (locKey, letters) in CategoryMap)
            categoryCombo.Items.Add(new ComboBoxItem { Content = App.Loc[locKey], Tag = letters });
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

        foreach (string mini in ClassesForCategory(_categoryFilter))
            classCombo.Items.Add(new ComboBoxItem { Content = mini, Tag = mini });

        classCombo.SelectedIndex = 0;
        _suppress = false;

        _classFilter = null;
        BuildBrowser();
    }

    private IEnumerable<string> ClassesForCategory(string[]? letters) =>
        _db.GroupsById.Values
            .Where(g => letters == null ||
                        (g.Category != null && letters.Contains(g.Category)))
            .Select(g => g.Mini)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

    private void CategoryCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _categoryFilter = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string[];
        RebuildClassCombo();
    }

    private void ClassCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _classFilter = (classCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        BuildBrowser();
    }

    // ----------------------------------------------------------- vehicle browser

    private void BuildBrowser()
    {
        browserStack.Children.Clear();

        string search = searchBox.Text?.Trim() ?? "";
        bool hasSearch = search.Length > 0;

        var visible = _db.Textures.Where(t => PassesFilters(t, search, hasSearch)).ToList();

        var grouped = visible
            .GroupBy(t => t.Group ?? "")
            .OrderBy(g => _db.GroupHeader(g.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var list = group.ToList();
            string header = string.IsNullOrEmpty(group.Key)
                ? App.Loc["Others"]
                : _db.GroupHeader(group.Key);

            var content = new StackPanel { Spacing = 2 };
            var expander = new Expander
            {
                Header = $"{header}  ({list.Count})",
                IsExpanded = hasSearch || _classFilter != null,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            expander.Content = content;

            bool populated = false;
            void Populate()
            {
                if (populated) return;
                populated = true;
                foreach (var texture in list)
                    content.Children.Add(BuildBrowserItem(texture));
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
        if (_categoryFilter != null)
        {
            string? cat = GroupCategory(t);
            if (cat == null || !_categoryFilter.Contains(cat))
                return false;
        }

        if (_classFilter != null)
        {
            string? mini = GroupMini(t);
            if (!string.Equals(mini, _classFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (hasSearch && !Matches(t, search))
            return false;

        return true;
    }

    private string? GroupCategory(VehicleTexture t) =>
        t.Group != null && _db.GroupsById.TryGetValue(t.Group, out var g) ? g.Category : null;

    private string? GroupMini(VehicleTexture t) =>
        t.Group != null && _db.GroupsById.TryGetValue(t.Group, out var g) ? g.Mini : null;

    private bool Matches(VehicleTexture t, string f)
    {
        bool C(string? s) => !string.IsNullOrEmpty(s) &&
                             s.Contains(f, StringComparison.OrdinalIgnoreCase);
        return C(t.Skinfile) || C(t.Model) || C(t.TextureMini) || C(t.MiniRef)
               || C(t.Meta?.Vehicle) || C(t.Meta?.Operator) || C(_db.GroupHeader(t.Group));
    }

    private Control BuildBrowserItem(VehicleTexture texture)
    {
        var img = new Image { Height = 40, Width = 66, Stretch = Stretch.Uniform };
        var bmp = GetMiniBitmap(_db.ResolveMiniName(texture));
        if (bmp != null) img.Source = bmp;

        var label = new TextBlock
        {
            Text = BrowserLabel(texture),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(img);
        stack.Children.Add(label);

        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = _hand
        };
        button.Classes.Add("Basic");
        button.Click += (_, _) => AddFromTexture(texture);
        ToolTip.SetTip(button, texture.FullPath);
        return button;
    }

    private string BrowserLabel(VehicleTexture texture)
    {
        string name = !string.IsNullOrEmpty(texture.TextureMini)
            ? texture.TextureMini!
            : Path.GetFileNameWithoutExtension(texture.Skinfile);
        if (!string.IsNullOrEmpty(texture.Meta?.Operator))
            name += $"  ·  {texture.Meta!.Operator}";
        return name;
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

                sceneryConsistCombo.Items.Add(new ComboBoxItem
                {
                    Content = BuildConsistPreview(trainset),
                    Tag = trainset
                });
            }
        }

        sceneryConsistCombo.SelectedItem = null;
        _suppress = false;
    }

    // Combo item: row of mini thumbnails on top, skin names underneath.
    private Control BuildConsistPreview(Trainset trainset)
    {
        var minis = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var names = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var v in trainset.Vehicles)
        {
            var bmp = GetMiniBitmap(v.MiniName) ?? GetMiniBitmap(v.SkinFile);
            if (bmp != null)
                minis.Children.Add(new Image { Source = bmp, Height = 26, Stretch = Stretch.Uniform });
            else
                minis.Children.Add(new Border
                {
                    Height = 26, Width = 44,
                    Background = new SolidColorBrush(Color.Parse("#22808080")),
                    CornerRadius = new CornerRadius(3)
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

    private void SceneryConsistCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (sceneryConsistCombo.SelectedItem is not ComboBoxItem { Tag: Trainset trainset })
            return;

        _consist.Clear();
        foreach (var vehicle in trainset.Vehicles)
            _consist.Add(vehicle.Clone());
        _selected = _consist.FirstOrDefault();
        RebuildConsist();
    }

    // ------------------------------------------------------------ consist edits

    private void AddFromTexture(VehicleTexture texture)
    {
        string skin = Path.GetFileNameWithoutExtension(texture.Skinfile);
        var dyn = new Dynamic
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
        _consist.Add(dyn);
        _selected = dyn;
        RebuildConsist();
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

    private void MoveLeft(Dynamic d)
    {
        int i = _consist.IndexOf(d);
        if (i > 0)
        {
            (_consist[i - 1], _consist[i]) = (_consist[i], _consist[i - 1]);
            RebuildConsist();
        }
    }

    private void MoveRight(Dynamic d)
    {
        int i = _consist.IndexOf(d);
        if (i >= 0 && i < _consist.Count - 1)
        {
            (_consist[i + 1], _consist[i]) = (_consist[i], _consist[i + 1]);
            RebuildConsist();
        }
    }

    private void RemoveVehicle(Dynamic d)
    {
        _consist.Remove(d);
        if (ReferenceEquals(_selected, d)) _selected = null;
        RebuildConsist();
    }

    private void FlipVehicle(Dynamic d)
    {
        d.Flipped = !d.Flipped;
        RebuildConsist();
    }

    private void CycleDriver(Dynamic d)
    {
        d.DriverType = d.DriverType switch
        {
            eDriverType.Nobody => eDriverType.Headdriver,
            eDriverType.Headdriver => eDriverType.Reardriver,
            eDriverType.Reardriver => eDriverType.Passenger,
            _ => eDriverType.Nobody
        };
        RebuildConsist();
    }

    // ----------------------------------------------------------- consist render

    private void RebuildConsist()
    {
        consistStack.Children.Clear();
        for (int i = 0; i < _consist.Count; i++)
        {
            var d = _consist[i];
            consistStack.Children.Add(BuildCard(d));
            if (i < _consist.Count - 1)
                consistStack.Children.Add(BuildCoupler(d));
        }

        emptyHint.IsVisible = _consist.Count == 0;
        UpdateSummary();
        UpdateDetails();
    }

    private Control BuildCard(Dynamic d)
    {
        bool selected = ReferenceEquals(_selected, d);

        Control visual;
        var bmp = GetMiniBitmap(d.MiniName) ?? GetMiniBitmap(d.SkinFile);
        if (bmp != null)
        {
            var img = new Image { Height = 48, Stretch = Stretch.Uniform };
            img.Source = bmp;
            if (d.Flipped)
            {
                img.RenderTransform = new ScaleTransform(-1, 1);
                img.RenderTransformOrigin = RelativePoint.Center;
            }
            visual = img;
        }
        else
        {
            visual = new Border
            {
                Height = 48,
                Width = 96,
                Background = new SolidColorBrush(Color.Parse("#22808080")),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = d.SkinFile,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        var nameBlock = new TextBlock
        {
            Text = d.Name,
            FontSize = 11,
            MaxWidth = 110,
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
        controls.Children.Add(SmallButton("◀", App.Loc["TipMoveLeft"], () => MoveLeft(d)));
        controls.Children.Add(DriverButton(d));
        controls.Children.Add(SmallButton("⇄", App.Loc["TipFlip"], () => FlipVehicle(d)));
        controls.Children.Add(SmallButton("✕", App.Loc["TipRemove"], () => RemoveVehicle(d)));
        controls.Children.Add(SmallButton("▶", App.Loc["TipMoveRight"], () => MoveRight(d)));

        var inner = new StackPanel { Spacing = 4, Width = 122 };
        inner.Children.Add(visual);
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
            Child = inner
        };
        card.PointerPressed += (_, _) =>
        {
            _selected = d;
            RebuildConsist();
        };
        return card;
    }

    // Coupling indicator between two vehicles. Hovering shows the 8-bit coupling
    // editor (placeholder for now; full editing comes later).
    private Control BuildCoupler(Dynamic d)
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
        ToolTip.SetTip(coupler, BuildCouplingBox(d));
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

    private Button DriverButton(Dynamic d)
    {
        (string glyph, string color) = d.DriverType switch
        {
            eDriverType.Headdriver => ("1", "#4CAF50"),
            eDriverType.Reardriver => ("2", "#FF9800"),
            eDriverType.Passenger => ("P", "#2196F3"),
            _ => ("–", "#888888")
        };

        var btn = SmallButton(glyph, App.Loc["TipDriver"], () => CycleDriver(d));
        btn.Foreground = new SolidColorBrush(Color.Parse(color));
        btn.FontWeight = FontWeight.Bold;
        return btn;
    }

    private void UpdateSummary()
    {
        int crewed = _consist.Count(v => v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver);
        summaryLabel.Text = $"{_consist.Count} {App.Loc["Vehicles"]}  ·  {crewed} {App.Loc["Crew"]}";
    }

    // Brakes / Loads tabs are placeholders bound to the selected vehicle.
    private void UpdateDetails()
    {
        if (_selected is null)
        {
            brakesContent.Text = App.Loc["SelectVehicleHint"];
            loadsContent.Text = App.Loc["SelectVehicleHint"];
            return;
        }

        var d = _selected;
        brakesContent.Text =
            $"{App.Loc["Vehicle"]}: {d.Name}\n" +
            $"coupling: {d.couplingData}\n\n" +
            $"{App.Loc["ComingSoon"]}";
        loadsContent.Text =
            $"{App.Loc["Vehicle"]}: {d.Name}\n\n" +
            $"{App.Loc["ComingSoon"]}";
    }

    // ------------------------------------------------------------------- minis

    private Bitmap? GetMiniBitmap(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (_miniCache.TryGetValue(name, out var cached))
            return cached;

        string? path = VehicleDatabase.MiniPath(name);
        Bitmap? bmp = null;
        if (path != null)
        {
            try { bmp = new Bitmap(path); }
            catch { bmp = null; }
        }
        _miniCache[name] = bmp;
        return bmp;
    }

    // ------------------------------------------------------------------ events

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        BuildBrowser();
    }

    private void NewConsist_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _consist.Clear();
        _selected = null;
        sceneryConsistCombo.SelectedItem = null;
        RebuildConsist();
    }
}
