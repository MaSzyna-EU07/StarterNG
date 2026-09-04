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
using StarterNG.Controls;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Application;

namespace StarterNG.Views;

public sealed class VehicleDetails
{
    private readonly StackPanel generalPanel;
    private readonly StackPanel consistTexturePanel;
    private readonly StackPanel couplerPanel;
    private readonly Panel couplerActions;
    private readonly StackPanel brakesPanel;
    private readonly StackPanel loadsPanel;
    private readonly StackPanel damagePanel;
    private readonly Button removeVehicleButton;

    private readonly VehicleCatalog _db;
    private readonly VehicleInfo _info;
    private readonly Consist _consist;
    private readonly Cargo _cargo;
    private readonly Cursor _hand;

    private readonly Action _redraw;
    private readonly Action _refreshChrome;
    private readonly Action<VehicleTexture, StackPanel> _showTextureInfo;

    public VehicleDetails(
        StackPanel generalPanel, StackPanel consistTexturePanel, StackPanel couplerPanel,
        Panel couplerActions,
        StackPanel brakesPanel, StackPanel loadsPanel, StackPanel damagePanel,
        Button removeVehicleButton,
        VehicleCatalog db, VehicleInfo info, Consist consist, Cargo cargo, Cursor hand,
        Action redraw, Action refreshChrome, Action<VehicleTexture, StackPanel> showTextureInfo)
    {
        this.generalPanel = generalPanel;
        this.consistTexturePanel = consistTexturePanel;
        this.couplerPanel = couplerPanel;
        this.couplerActions = couplerActions;
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
        couplerActions.Children.Clear();
        brakesPanel.Children.Clear();
        loadsPanel.Children.Clear();
        damagePanel.Children.Clear();

        if (_consist.Selected != null && !_consist.Selected.Cars.Contains(_consist.SelectedCar!))
            _consist.SelectedCar = _consist.Selected.Cars[0];

        _refreshChrome();

        if (_consist.Selected is null)
        {
            generalPanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            consistTexturePanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            couplerPanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            brakesPanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            loadsPanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            damagePanel.Children.Add(PanelNote(App.Loc["SelectVehicleHint"]));
            removeVehicleButton.IsEnabled = false;
            // The whole-consist tools still apply with nothing selected.
            AddConsistLoadSection();
            return;
        }

        BuildGeneralInfo(_consist.Selected);

        if (_db.TextureForSkin(_consist.ActiveCar(_consist.Selected).SkinFile) is { } tex)
            _showTextureInfo(tex, consistTexturePanel);
        else
            consistTexturePanel.Children.Add(PanelNote(App.Loc["NoTextureInfo"]));

        removeVehicleButton.IsEnabled = true;

        if (_consist.Selected.Cars.Count > 1)
        {
            couplerPanel.Children.Add(PanelNote(App.Loc["CouplingUnitRear"]));
            brakesPanel.Children.Add(PanelNote(App.Loc["AppliesToUnit"]));
        }

        BuildCouplerEditor(_consist.Selected);
        BuildBrakesEditor(_consist.Selected);
        BuildDamageEditor(_consist.Selected);

        // Configuration tab: what this vehicle is, what it carries, then the actions
        // that reach past it - each under a header that says which is which.
        loadsPanel.Children.Add(
            Section(App.Loc["Vehicle"], BuildVehicleSection(_consist.Selected)));
        loadsPanel.Children.Add(
            Section(App.Loc["Load"], BuildLoadSection(_consist.Selected)));
        AddConsistLoadSection();
    }

    public void ShowTexture(VehicleTexture texture)
    {
        generalPanel.Children.Clear();
        consistTexturePanel.Children.Clear();

        var inv = CultureInfo.InvariantCulture;
        var physics = _info.PhysicsFor(texture);

        generalPanel.Children.Add(Reading($"{App.Loc["Length"]} [m]",
            physics?.Length.ToString("0.##", inv)));
        generalPanel.Children.Add(Reading($"{App.Loc["Mass"]} [t]",
            physics is null ? null : (physics.Mass / 1000.0).ToString("0.#", inv)));
        generalPanel.Children.Add(Reading($"{App.Loc["VMax"]} [km/h]",
            physics?.VMax.ToString("0", inv)));
        generalPanel.Children.Add(Reading(App.Loc["Model"], texture.Model));
        generalPanel.Children.Add(Reading(App.Loc["Texture"], texture.Skinfile));

        _showTextureInfo(texture, consistTexturePanel);
    }

    private void BuildGeneralInfo(ConsistItem item)
    {
        var car = _consist.ActiveCar(item);
        var inv = CultureInfo.InvariantCulture;

        if (!string.IsNullOrWhiteSpace(car.Name))
            generalPanel.Children.Add(Reading(App.Loc["Name"], car.Name));

        bool unit = item.Cars.Count > 1;
        var members = item.Cars.Select(_info.PhysicsFor).Where(p => p != null).ToList();

        double? length = members.Count == 0 ? null
            : unit ? members.Sum(p => p!.Length) : members[0]!.Length;
        double? mass = members.Count == 0 ? null
            : (unit ? members.Sum(p => p!.Mass) : members[0]!.Mass) / 1000.0;
        double? vmax = members.Count == 0 ? null
            : unit ? members.Min(p => p!.VMax) : members[0]!.VMax;

        generalPanel.Children.Add(Reading($"{App.Loc["Length"]} [m]",
            length?.ToString("0.##", inv)));
        generalPanel.Children.Add(Reading($"{App.Loc["Mass"]} [t]",
            mass?.ToString("0.#", inv)));
        generalPanel.Children.Add(Reading($"{App.Loc["VMax"]} [km/h]",
            vmax?.ToString("0", inv)));

        generalPanel.Children.Add(Reading(App.Loc["Model"], car.MmdFile));
        generalPanel.Children.Add(Reading(App.Loc["Texture"], car.SkinFile));
    }

    private void BuildCouplerEditor(ConsistItem item)
    {
        int unitIdx = _consist.IndexOf(item);
        if (unitIdx < 0)
            return;

        bool isLast = unitIdx >= _consist.Count - 1;

        var d = Consist.TailCar(item);

        // Two columns: the tab is wide enough and the list is short enough
        // to avoid scrolling while still reading left-to-right, top-to-bottom.
        var list = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowSpacing = 2,
            ColumnSpacing = 16
        };
        for (int r = 0; r < (CouplingBits.BitKeys.Length + 1) / 2; r++)
            list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

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
            Grid.SetRow(check, i / 2);
            Grid.SetColumn(check, i % 2);
            list.Children.Add(check);
        }
        couplerPanel.Children.Add(list);

        var copy = new Button
        {
            Content = App.Loc["CopyCoupler"],
            FontSize = 12,
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
            FontSize = 12,
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

        // Laid out horizontally under the checkbox list so the grid can use the full tab width
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 6, 0, 0)
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(auto);
        couplerActions.Children.Add(buttons);
    }

    /// <summary>
    /// The vehicle's own settings. Every write path is unchanged: crew and wagon number
    /// go through the unit's active car, the coolant flag to its first car only.
    /// </summary>
    private Control BuildVehicleSection(ConsistItem item)
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
            AppServices.Current.State.NotifyChanged();
        };
        driver.MinWidth = 0;
        driver.HorizontalAlignment = HorizontalAlignment.Stretch;

        var rows = new StackPanel();
        rows.Children.Add(Row(App.Loc["CrewLabel"], driver));

        if (IsPassengerVehicle(item))
        {
            // The number rides in each vehicle's own name, so it belongs to the
            // member the user picked inside the unit, not to its first car.
            var numbered = _consist.ActiveCar(item);
            var numBox = new NumericUpDown
            {
                FontSize = 12,
                Minimum = 0,
                Maximum = 99999,
                Increment = 1,
                Value = Consist.GetWagonNumber(numbered),
                FormatString = "0"
            };
            numBox.ValueChanged += (_, _) =>
            {
                _consist.SetWagonNumber(numbered, (int)(numBox.Value ?? 0));
                _redraw();
            };
            rows.Children.Add(Row(App.Loc["WagonNumber"], numBox));
        }

        // Orientation is not here any more: it reads and toggles off the flip button on
        // the vehicle's own card, which lights up when the vehicle sits reversed.
        // The flag rides on the unit's first car, so that is the car that has to own a
        // diesel engine for it to mean anything: the simulator runs its heat model only
        // for DieselEngine and DieselElectric, and on anything else ".TA" is written into
        // the scenario and then ignored. Shown anyway when the flag is already set, so an
        // inherited one stays visible and removable.
        var d = item.Cars[0];
        bool thermoApplies = _info.PhysicsFor(d)?.IsDieselEngine == true
                             || d.Coupling.ThermoDynamic;

        if (thermoApplies)
        {
            var thermo = new CheckBox
            {
                Content = App.Loc["ThermoAmbient"],
                IsChecked = d.Coupling.ThermoDynamic,
                FontSize = 12
            };
            thermo.IsCheckedChanged += (_, _) => d.Coupling.ThermoDynamic = thermo.IsChecked == true;

            // Label-less row, so the box indents to the control column with everything
            // else instead of starting at the panel's left edge on its own.
            rows.Children.Add(Row(content: thermo));
        }

        return rows;
    }

    private bool IsPassengerVehicle(ConsistItem item)
    {
        var lead = _consist.ActiveCar(item);
        var phys = _info.PhysicsFor(lead);
        if (phys == null)
            return false;
        return phys.LoadAccepted.Split(',', ';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Any(s => s.Equals("passengers", StringComparison.OrdinalIgnoreCase));
    }

    private static SettingRow Row(string? label = null, Control? content = null,
                                  string? value = null, string? description = null,
                                  string? tip = null) =>
        DetailRows.Row(label, content, value, description, tip);

    private static SettingRow Reading(string label, string? value) => DetailRows.Reading(label, value);

    private static SettingsSection Section(string header, Control content, Control? meta = null)
    {
        var section = new SettingsSection
        {
            Header = header,
            Content = content,
            // Passive text only: Toggle sits outside the logical tree, so nothing put
            // here can be found later with GetLogicalDescendants.
            Toggle = meta
        };
        section.Classes.Add("Compact");
        return section;
    }

    private static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 3 };
        foreach (var c in children)
            panel.Children.Add(c);
        return panel;
    }

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#9098A0"));

    /// <summary>
    /// A note that governs a whole panel or section rather than one row - same look as a
    /// row's description line. Replaces the old DetailHint/UnitScopeHint pair, which
    /// differed only by a 2 px margin.
    /// </summary>
    private static TextBlock PanelNote(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = DimBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };

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

        brakesPanel.Children.Add(Row(App.Loc["BrakeMode"], modeCombo));
        brakesPanel.Children.Add(Row(App.Loc["BrakeSwitch"], switchCombo));
        brakesPanel.Children.Add(Row(App.Loc["BrakeLoad"], loadCombo));
    }

    private static string BrakeModeLabel(string? code) => code switch
    {
        "G" => App.Loc["BrakeFreight"],
        "P" => App.Loc["BrakePassenger"],
        "R" => App.Loc["BrakeExpress"],
        "M" => App.Loc["BrakeExpressMg"],
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
        "<>" => App.Loc["SwitchEmergencyTest"],
        "0" => App.Loc["SwitchOff"],
        "1" => App.Loc["SwitchOff10"],
        "X" => App.Loc["SwitchAgonal"],
        "E" => App.Loc["SwitchEmptied"],
        "Q" => App.Loc["BrakeNoAir"],
        _ => App.Loc["SwitchNormal"]
    };

    private void BuildDamageEditor(ConsistItem item)
    {
        var w = _consist.ActiveCar(item).Coupling.GetWheels() ?? new WheelSettings();

        NumericUpDown Spin(int value, int max) => new()
        {
            FontSize = 12, Minimum = 0, Maximum = max, Increment = 1,
            Value = value, MinWidth = 0, FormatString = "0"
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

        damagePanel.Children.Add(Row(App.Loc["DamageSway"], sway));
        damagePanel.Children.Add(Row(App.Loc["DamageFlatness"], flat));
        damagePanel.Children.Add(Row(App.Loc["DamageFlatnessRand"], flatRand));
        damagePanel.Children.Add(Row(App.Loc["DamageFlatnessProb"], flatProb));
    }

    /// <summary>
    /// The three actions that fill the selected vehicle from its neighbours. They keep
    /// the primary (filled) look because they act on what you have selected.
    /// </summary>
    private Control BuildVehicleLoadTools(ConsistItem sel)
    {
        bool loadable = _cargo.IsLoadable(sel);

        var prev = LoadToolButton(App.Loc["LoadPrevShort"],
            () => { if (_consist.Selected is { } s) { _cargo.CopyFromPrevious(_consist, s); _redraw(); } });
        prev.IsEnabled = loadable && _consist.IndexOf(sel) > 0;
        ToolTip.SetTip(prev, App.Loc["LoadCopyPrev"]);

        var max = LoadToolButton(App.Loc["LoadMaxShort"],
            () => { if (_consist.Selected is { } s) { _cargo.FillUnit(s); _redraw(); } });
        max.IsEnabled = loadable;
        ToolTip.SetTip(max, App.Loc["LoadMax"]);

        var toAll = LoadToolButton(App.Loc["LoadToAllShort"],
            () => { if (_consist.Selected is { } s) { _cargo.CopyToFollowing(_consist, s); _redraw(); } });
        toAll.IsEnabled = loadable;
        ToolTip.SetTip(toAll, App.Loc["LoadCopyToAll"]);

        return Stack(prev, max, toAll);
    }

    /// <summary>
    /// The whole-consist actions. Secondary styling plus a header that names the scope
    /// and counts the vehicles, because these reach far past the selected card. No
    /// tooltips: theirs used to repeat the button label word for word.
    /// </summary>
    private void AddConsistLoadSection()
    {
        if (_consist.Count == 0)
            return;

        var tools = Stack(
            LoadToolButton(App.Loc["ConsistRandomType"],
                () => { _cargo.RandomTypes(_consist); _redraw(); }, secondary: true),
            LoadToolButton(App.Loc["ConsistMaxAmount"],
                () => { _cargo.MaxAmounts(_consist); _redraw(); }, secondary: true),
            LoadToolButton(App.Loc["ConsistRandomAmount"],
                () => { _cargo.RandomAmounts(_consist); _redraw(); }, secondary: true));

        var buttons = Stack(ReadyToGoRow(), tools);

        var count = new TextBlock
        {
            Text = $"{App.Loc["VehicleCount"]}: {_consist.Count}",
            FontSize = 12,
            Foreground = DimBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        loadsPanel.Children.Add(Section(App.Loc["ConsistLoadTools"], buttons, count));
    }

    private Control ReadyToGoRow()
    {
        var trainset = _consist.EditingTrainset;
        var ready = new CheckBox
        {
            Content = App.Loc["ConsistReady"],
            FontSize = 12,
            IsEnabled = trainset is not null,
            IsChecked = trainset?.ReadyToGo ?? false
        };
        ToolTip.SetTip(ready, App.Loc["ConsistReadyDesc"]);

        ready.IsCheckedChanged += (_, _) =>
        {
            if (trainset is null)
                return;
            trainset.ReadyToGo = ready.IsChecked == true;
            _redraw();
        };

        return Row(content: ready);
    }

    private Button LoadToolButton(string text, Action onClick, bool secondary = false)
    {
        var b = new Button
        {
            Content = text, FontSize = 12,
            Padding = new Thickness(6, 2), Cursor = _hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = _consist.Count > 0
        };
        b.Classes.Add(secondary ? "Outline" : "Flat");
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>
    /// What the selected vehicle carries, and the actions that fill it. A vehicle that
    /// takes no cargo gets the note alone - previously the three actions stayed behind,
    /// greyed out and useless.
    /// </summary>
    private Control BuildLoadSection(ConsistItem item)
    {
        var lead = _consist.ActiveCar(item);
        var available = _cargo.AcceptedTypes(lead);

        if (available.Count == 0 && string.IsNullOrEmpty(lead.LoadType))
            return PanelNote(App.Loc["LoadNotLoadable"]);

        if (!string.IsNullOrEmpty(lead.LoadType) &&
            !available.Contains(lead.LoadType!, StringComparer.OrdinalIgnoreCase))
            available.Insert(0, lead.LoadType!);

        var typeCombo = new ComboBox { FontSize = 12, MinWidth = 120 };
        typeCombo.Items.Add(new ComboBoxItem { Content = App.Loc["None"], Tag = null });
        foreach (var name in available)
            typeCombo.Items.Add(new ComboBoxItem { Content = DescribeLoad(name), Tag = name });

        typeCombo.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(lead.LoadType))
            foreach (var obj in typeCombo.Items)
                if (obj is ComboBoxItem ci && ci.Tag is string t &&
                    string.Equals(t, lead.LoadType, StringComparison.OrdinalIgnoreCase))
                {
                    typeCombo.SelectedItem = ci;
                    break;
                }

        string? SelectedType() => (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        bool IsPant() => Dynamic.IsPantStateType(SelectedType());

        int limit = _cargo.LimitFor(lead);
        var countBox = new NumericUpDown
        {
            FontSize = 12, Minimum = 0, Maximum = limit, Increment = 1,
            Value = Math.Min(lead.LoadCount, limit), FormatString = "0",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var pantStateCombo = new ComboBox
        {
            FontSize = 12, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        pantStateCombo.Items.Add(new ComboBoxItem { Content = App.Loc["PantStateNone"], Tag = 0 });
        pantStateCombo.Items.Add(new ComboBoxItem { Content = "A", Tag = 1 });
        pantStateCombo.Items.Add(new ComboBoxItem { Content = "B", Tag = 2 });
        pantStateCombo.Items.Add(new ComboBoxItem { Content = App.Loc["PantStateBoth"], Tag = 3 });

        var countContainer = new Panel();
        countContainer.Children.Add(countBox);
        countContainer.Children.Add(pantStateCombo);

        string UnitText()
        {
            var phys = _info.PhysicsFor(lead);
            if (phys == null) return App.Loc["LoadUnitGeneric"];
            if (string.Equals(phys.LoadQ, "pieces", StringComparison.OrdinalIgnoreCase))
                return App.Loc["LoadUnitPieces"];
            if (string.Equals(phys.LoadQ, "tonns", StringComparison.OrdinalIgnoreCase))
                return App.Loc["LoadUnitTons"];
            return App.Loc["LoadUnitGeneric"];
        }

        var countRow = Row(App.Loc["LoadCount"], countContainer);

        void SyncPantStateFromCount()
        {
            int value = Math.Clamp((int)(countBox.Value ?? 0), 0, Dynamic.PantStateMax);
            foreach (var obj in pantStateCombo.Items)
                if (obj is ComboBoxItem ci && ci.Tag is int t && t == value)
                {
                    pantStateCombo.SelectedItem = ci;
                    return;
                }
        }

        void SyncCountFromPantState()
        {
            int value = pantStateCombo.SelectedItem is ComboBoxItem { Tag: int t } ? t : 0;
            countBox.Value = Math.Clamp(value, 0, countBox.Maximum);
        }

        void UpdateVisibility()
        {
            bool pant = IsPant();
            countBox.IsVisible = !pant;
            pantStateCombo.IsVisible = pant;
            // The unit slot is reserved by the Compact style even when empty, so the
            // spinner does not jump sideways as the unit comes and goes.
            countRow.Label = pant ? App.Loc["Pantograph"] : App.Loc["LoadCount"];
            countRow.ValueText = pant ? null : UnitText();
            // The "set an amount above 0" advice is only true while the amount is 0;
            // past that it is a line of noise in a panel with no height to spare.
            countRow.Description = pant ? App.Loc["LoadPantStateHint"]
                : GetCount() == 0 ? App.Loc["LoadHint"]
                : null;
        }

        int GetCount()
        {
            if (IsPant())
                return pantStateCombo.SelectedItem is ComboBoxItem { Tag: int t } ? t : 0;
            return (int)(countBox.Value ?? 0);
        }

        void Apply()
        {
            string? type = SelectedType();
            int count = GetCount();
            foreach (var c in _consist.MemberTargets(item))
            {
                c.LoadType = string.IsNullOrEmpty(type) ? null : type;
                c.LoadCount = string.IsNullOrEmpty(type) ? 0 : Math.Min(count, _cargo.LimitFor(c, type));
            }
        }

        // Seed the pantograph combo from the model before any handler is listening.
        SyncPantStateFromCount();
        bool wasPant = IsPant();

        typeCombo.SelectionChanged += (_, _) =>
        {
            countBox.Maximum = _cargo.LimitFor(lead, SelectedType());

            // Both widgets edit the same LoadCount, so the value is carried across only
            // when the mode actually flips. Doing it unconditionally - as UpdateVisibility
            // used to - reset the amount to zero on every rebuild, which is why setting
            // the maximum load appeared to do nothing: the model was written, the panel
            // refreshed, and the refresh immediately wrote zero back.
            bool pant = IsPant();
            if (pant != wasPant)
            {
                if (pant)
                    SyncPantStateFromCount();
                else
                    SyncCountFromPantState();
                wasPant = pant;
            }

            UpdateVisibility();
            Apply();
        };
        pantStateCombo.SelectionChanged += (_, _) => Apply();
        countBox.ValueChanged += (_, _) => { Apply(); UpdateVisibility(); };

        UpdateVisibility();

        return Stack(
            Row(App.Loc["LoadType"], typeCombo),
            countRow,
            BuildVehicleLoadTools(item));
    }

    /// <summary>
    /// Label for a cargo type. The pantograph state is carried as a pseudo load
    /// and gets a translated name rather than its raw token.
    /// </summary>
    private static string DescribeLoad(string name) =>
        string.Equals(name, Dynamic.PantState, StringComparison.OrdinalIgnoreCase)
            ? App.Loc["LoadPantState"]
            : name;

}
