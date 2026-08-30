using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Views;

public sealed class VehicleDetails
{
    private readonly HeaderedContentControl selectedVehiclePanel;
    private readonly StackPanel generalPanel;
    private readonly StackPanel consistTexturePanel;
    private readonly StackPanel couplerPanel;
    private readonly StackPanel brakesPanel;
    private readonly StackPanel loadsPanel;
    private readonly StackPanel damagePanel;
    private readonly Button removeVehicleButton;

    private readonly VehicleDatabase _db;
    private readonly VehicleInfo _info;
    private readonly Consist _consist;
    private readonly Cargo _cargo;
    private readonly Cursor _hand;

    private readonly Action _redraw;
    private readonly Action _refreshChrome;
    private readonly Action<VehicleTexture, StackPanel> _showTextureInfo;

    public VehicleDetails(
        HeaderedContentControl selectedVehiclePanel,
        StackPanel generalPanel, StackPanel consistTexturePanel, StackPanel couplerPanel,
        StackPanel brakesPanel, StackPanel loadsPanel, StackPanel damagePanel,
        Button removeVehicleButton,
        VehicleDatabase db, VehicleInfo info, Consist consist, Cargo cargo, Cursor hand,
        Action redraw, Action refreshChrome, Action<VehicleTexture, StackPanel> showTextureInfo)
    {
        this.selectedVehiclePanel = selectedVehiclePanel;
        this.generalPanel = generalPanel;
        this.consistTexturePanel = consistTexturePanel;
        this.couplerPanel = couplerPanel;
        this.brakesPanel = brakesPanel;
        this.loadsPanel = loadsPanel;
        this.damagePanel = damagePanel;
        this.removeVehicleButton = removeVehicleButton;

        _db = db;
        _info = info;
        _consist = consist;
        _cargo = cargo;
        _hand = hand;
        _redraw = redraw;
        _refreshChrome = refreshChrome;
        _showTextureInfo = showTextureInfo;
    }

    public void Refresh()
    {
        generalPanel.Children.Clear();
        consistTexturePanel.Children.Clear();
        couplerPanel.Children.Clear();
        brakesPanel.Children.Clear();
        loadsPanel.Children.Clear();
        damagePanel.Children.Clear();

        selectedVehiclePanel.Header = _consist.Selected is null
            ? App.Loc["SelectedVehicle"]
            : $"{App.Loc["SelectedVehicle"]}: " +
              (_consist.Selected.Grouped ? Consist.UnitLabel(_consist.Selected) : _consist.Selected.Cars[0].Name);

        if (_consist.Selected != null && !_consist.Selected.Cars.Contains(_consist.SelectedCar!))
            _consist.SelectedCar = _consist.Selected.Cars[0];

        _refreshChrome();

        loadsPanel.Children.Add(BuildConsistLoadTools());

        if (_consist.Selected is null)
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

        BuildGeneralInfo(_consist.Selected);

        if (_db.TextureForSkin(_consist.ActiveCar(_consist.Selected).SkinFile) is { } tex)
            _showTextureInfo(tex, consistTexturePanel);
        else
            consistTexturePanel.Children.Add(DetailHint(App.Loc["NoTextureInfo"]));

        removeVehicleButton.IsEnabled = true;

        if (_consist.Selected.Cars.Count > 1)
        {
            couplerPanel.Children.Add(UnitScopeHint(App.Loc["CouplingUnitRear"]));
            brakesPanel.Children.Add(UnitScopeHint(App.Loc["AppliesToUnit"]));
        }

        BuildCouplerEditor(_consist.Selected);
        BuildBrakesEditor(_consist.Selected);
        BuildLoadsEditor(_consist.Selected);
        BuildConfigExtras(_consist.Selected);
        BuildDamageEditor(_consist.Selected);
    }

    private void BuildGeneralInfo(ConsistItem item)
    {
        var car = _consist.ActiveCar(item);
        var inv = CultureInfo.InvariantCulture;

        bool unit = item.Cars.Count > 1;
        var members = item.Cars.Select(_info.PhysicsFor).Where(p => p != null).ToList();

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

        var d = Consist.TailCar(item);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 4
        };
        for (int i = 0; i < CouplingBits.BitKeys.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = App.Loc[CouplingBits.BitKeys[i]],
                IsChecked = d.Coupling.Has(bit),
                Classes = { "Checklist" }
            };
            check.IsCheckedChanged += (_, _) =>
            {
                d.Coupling.Set(bit, check.IsChecked == true);
                _redraw();
            };
            int half = (CouplingBits.BitKeys.Length + 1) / 2;
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
            d.Coupling.Flags = Consist.TailCar(_consist[unitIdx - 1]).Coupling.Flags;
            _redraw();
            Refresh();
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
            _consist.AutoConnectAll();
            _redraw();
            Refresh();
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
        var crewCar = _consist.ActiveCar(item);

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
            item.Driver = Consist.UnitDriver(item.Cars);
            if (ReferenceEquals(_consist.Selected, item))
                _consist.SyncStartingVehicle();
            _redraw();
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
                _consist.Flip(item);
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
            foreach (var c in Consist.UnitTargets(item)) c.Coupling.SetBrake(b);
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
        var w = _consist.ActiveCar(item).Coupling.GetWheels() ?? new WheelSettings();

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
            foreach (var c in _consist.MemberTargets(item))
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

    private Control BuildConsistLoadTools()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LoadToolButton(App.Loc["ConsistRandomType"],
            () => { _cargo.RandomTypes(_consist); _redraw(); }));
        row.Children.Add(LoadToolButton(App.Loc["ConsistMaxAmount"],
            () => { _cargo.MaxAmounts(_consist); _redraw(); }));
        row.Children.Add(LoadToolButton(App.Loc["ConsistRandomAmount"],
            () => { _cargo.RandomAmounts(_consist); _redraw(); }));
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

    private void BuildLoadsEditor(ConsistItem item)
    {
        var lead = _consist.ActiveCar(item);
        var available = _cargo.AcceptedTypes(lead);

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
            typeCombo.Items.Add(new ComboBoxItem { Content = LoadWeights.Describe(name), Tag = name });

        typeCombo.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(lead.LoadType))
            foreach (var obj in typeCombo.Items)
                if (obj is ComboBoxItem ci && ci.Tag is string t &&
                    string.Equals(t, lead.LoadType, StringComparison.OrdinalIgnoreCase))
                {
                    typeCombo.SelectedItem = ci;
                    break;
                }

        int limit = _cargo.LimitFor(lead);
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
            foreach (var c in _consist.MemberTargets(item))
            {

                c.LoadType = string.IsNullOrEmpty(type) ? null : type;
                c.LoadCount = string.IsNullOrEmpty(type) ? 0 : Math.Min(count, _cargo.LimitFor(c, type));
            }
        }

        typeCombo.SelectionChanged += (_, _) =>
        {
            string? type = SelectedType();
            pantHint.IsVisible = Dynamic.IsPantStateType(type);
            countBox.Maximum = _cargo.LimitFor(lead, type);
            Apply();
        };
        countBox.ValueChanged += (_, _) => Apply();

        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadType"], typeCombo));
        loadsPanel.Children.Add(LabeledRow(App.Loc["LoadCount"], countBox));
        loadsPanel.Children.Add(pantHint);
    }
}
