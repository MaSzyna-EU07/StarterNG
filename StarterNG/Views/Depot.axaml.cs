using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

namespace StarterNG.Views;

public partial class Depot : UserControl
{

    private readonly VehicleCatalog _db = AppServices.Current.Library.Vehicles;
    private readonly List<Scenery> _sceneries = AppServices.Current.Library.Sceneries;

    private readonly VehicleInfo _info;

    private readonly Consist _consist;

    private readonly Cargo _cargo;

    private Warehouse _warehouse = null!;

    private VehicleDetails _details = null!;

    private VehicleCards _cards = null!;

    private ConsistDragging _drag = null!;

    private VehicleBrowser _browser = null!;

    private bool _suppress;

    private readonly MiniTextures _minis = new();
    private readonly Cursor _hand = new(StandardCursorType.Hand);
    private static readonly Random _rng = new();

    private Scenery? _listScenery;

    public Depot()
    {
        _info = new VehicleInfo(_db);
        _consist = new Consist(_db, _info);
        _cargo = new Cargo(_info, _rng);
        _consist.Changed += RebuildConsist;

        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        _warehouse = new Warehouse(warehouseList, removeFromWarehouseButton, _hand,
            () => _consist.EditingTrainset, ApplyConsist);

        _browser = new VehicleBrowser(categoryCombo, classCombo, vehicleListBox, searchBox,
            hideArchivalCheck, miniPreview, addVehicleButton, _db, _minis,
            () => TextureBaseButton_OnClick(null, null!));

        _drag = new ConsistDragging(consistStack, consistScroll, consistOverlay,
            consistDropTarget, vehicleListBox, miniPreviewPanel,
            _consist, () => _browser.Selected, InsertTextureAt, _browser.SelectInBrowser);

        _cards = new VehicleCards(consistStack, _db, _consist, _cargo, _minis, _hand,
            RebuildConsist, () => _details.Refresh(), _browser.SelectInBrowser, _drag.ArmCardDrag);

        // Hidden still scrolls - it only drops the bar itself.
        MiniTextures.Sharp(miniPreview);

        _details = new VehicleDetails(
            generalPanel, consistTexturePanel, couplerPanel, couplerActions,
            brakesPanel, loadsPanel, damagePanel, removeVehicleButton,
            _db, _info, _consist, _cargo, _hand,
            RebuildConsist, _cards.RefreshMemberChrome, _browser.ShowTextureInfo);

        _browser.TextureSelected = _details.ShowTexture;

        Dispatcher.UIThread.Post(() =>
        {
            hideArchivalCheck.IsChecked = AppServices.Current.Settings.HideArchivalVehicles;
            _browser.InitClassComboTemplates();
            _browser.PopulateCategoryCombo();
            PopulateSceneryConsists();
            _warehouse.Refresh();
            RebuildConsist();
        }, DispatcherPriority.Background);

        _drag.Attach();

        AttachedToVisualTree += (_, _) => SyncFromSelection();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                SyncFromSelection();
                _warehouse.Refresh();
            }
        };
    }

    private void AddToWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = _warehouse.SaveCurrentAsync();

    private void RemoveFromWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = _warehouse.RemoveSelectedAsync();

    private void WarehouseList_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) =>
        _warehouse.ApplySelected();

    private void CategoryCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _browser.CategoryCombo_OnSelectionChanged(sender, e);

    private void ClassCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _browser.ClassCombo_OnSelectionChanged(sender, e);

    private void Combo_OnPointerWheel(object? sender, PointerWheelEventArgs e) =>
        _browser.Combo_OnPointerWheel(sender, e);

    private void VehicleListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _browser.VehicleListBox_OnSelectionChanged(sender, e);

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        _browser.SearchBox_OnTextChanged(sender, e);

    private void HideArchivalCheck_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _browser.HideArchivalCheck_OnChanged(sender, e);

    private void MiniPreview_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _drag.MiniPreviewPressed(sender, e);

    private void VehicleListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _drag.VehicleListPressed(sender, e);

    /// <summary>
    /// Floor of the consist row with the small thumbnails, one thumbnail height
    /// below the roomy one.
    /// </summary>
    private const double SmallConsistRowHeight = 106;

    private const double LargeConsistRowHeight = 140;

    private void SyncConsistRowHeight() =>
        depotGrid.RowDefinitions[2].MinHeight = AppServices.Current.Settings.LargeThumbnails
            ? LargeConsistRowHeight
            : SmallConsistRowHeight;

    private Dynamic NewVehicle(VehicleTexture texture) =>
        _consist.MakeDynamic(texture, _listScenery ?? AppServices.Current.State.CurrentScenery);

    private void SyncFromSelection()
    {

        _browser.SyncHideArchival();

        PopulateSceneryConsists();

        var trainset = AppServices.Current.State.CurrentTrainset;
        if (trainset != null && !ReferenceEquals(trainset, _consist.EditingTrainset))
            _consist.LoadFrom(trainset);
        else if (_consist.EditingTrainset != null)
            RebuildConsist();
    }

    public void RebuildAfterLanguageChange()
    {
        _browser.PopulateCategoryCombo();
        PopulateSceneryConsists();
        RebuildConsist();
        _details.Refresh();
    }

    /// <summary>Redraws the consist cards, e.g. after the thumbnail size changed.</summary>
    public void RefreshConsistView() => RebuildConsist();

    private void RebuildConsist()
    {
        SyncConsistRowHeight();
        consistStack.Children.Clear();
        _cards.Forget();
        _drag.Reset();
        for (int i = 0; i < _consist.Count; i++)
        {
            var item = _consist[i];
            consistStack.Children.Add(_cards.BuildCard(item, i));

            if (i < _consist.Count - 1)
                consistStack.Children.Add(_cards.BuildCoupler(item));
            else
                consistStack.Children.Add(_cards.BuildCoupler(item, trailing: true));
        }

        emptyHint.IsVisible = _consist.Count == 0;

        // A selection the consist moved on its own - after a removal, say - is only
        // highlighted; activating it runs what a click would have run.
        if (_consist.Selected is { } moved &&
            (_consist.SelectedCar is null || !moved.Cars.Contains(_consist.SelectedCar)))
            _cards.Activate(moved);

        _details.Refresh();
        UpdateTrainStats();
        WriteBackToScenery();

        // The cards are thrown away and rebuilt on every change, so a keyboard-driven
        // edit has to be handed its card back or focus escapes the strip.
        _cards.FocusSelectedCard(onlyIfRequested: true);
    }

    private void WriteBackToScenery()
    {
        if (_consist.EditingTrainset is null)
            return;

        _consist.EditingTrainset.Vehicles = _consist.Flatten();
        RefreshConsistListLabels();
    }

    private void UpdateTrainStats()
    {
        if (_consist.EditingTrainset is null)
        {
            trainStats.Children.Clear();
            return;
        }

        StarterNG.Infrastructure.StatsBar.Fill(trainStats, TrainsetDisplay.StatsFields(
            _consist.EditingTrainset,
            _info.PhysicsFor,
            car => _db.TextureForSkin(car.SkinFile) is { } t ? VehicleInfo.CategoryOf(t) : null,
            AppServices.Current.LoadWeights.Table.WeightOf));
    }

    private void RemoveVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_consist.Selected is { } item)
            _consist.Remove(item);
    }

    private void ConsistFilter_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppress) return;
        AppServices.Current.Settings.ShowAiVehicles = depotShowAiCheck.IsChecked == true;
        AppServices.Current.Settings.DrivableOnly = depotDrivableOnlyCheck.IsChecked == true;
        PopulateSceneryConsists();
    }

    private void PopulateSceneryConsists()
    {
        _suppress = true;
        sceneryConsistList.Items.Clear();

        depotShowAiCheck.IsChecked = AppServices.Current.Settings.ShowAiVehicles;
        depotDrivableOnlyCheck.IsChecked = AppServices.Current.Settings.DrivableOnly;

        _listScenery = AppServices.Current.State.CurrentScenery ?? _sceneries.FirstOrDefault();
        sceneryConsistsTab.Header = App.Loc["SceneryConsists"];
        ToolTip.SetTip(sceneryConsistsTab,
            _listScenery != null ? Path.GetFileNameWithoutExtension(_listScenery.Path) : App.Loc["NoSceneryConsists"]);

        if (_listScenery != null)
        {
            bool showAi = AppServices.Current.Settings.ShowAiVehicles;
            bool drivableOnly = AppServices.Current.Settings.DrivableOnly;

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

                if (ReferenceEquals(trainset, AppServices.Current.State.CurrentTrainset))
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

        AppServices.Current.State.CurrentScenery = _listScenery;
        AppServices.Current.State.CurrentTrainset = trainset;
        AppServices.Current.State.StartingVehicleName = TrainsetDisplay.DefaultStartingVehicle(trainset);
        _consist.LoadFrom(trainset);
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
        AppServices.Current.State.CurrentScenery = _listScenery;
        AppServices.Current.State.CurrentTrainset = target;
        _consist.LoadFrom(target);
        PopulateSceneryConsists();
    }

    private void RemoveAllVehicles(Trainset target)
    {
        target.Vehicles = new List<Dynamic>();
        AppServices.Current.State.CurrentScenery = _listScenery;
        AppServices.Current.State.CurrentTrainset = target;
        _consist.LoadFrom(target);
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

    public async Task RandomizeCurrentTrainsetAsync()
    {
        if (AppServices.Current.State.CurrentTrainset is not { } trainset)
            return;

        if (!ReferenceEquals(trainset, _consist.EditingTrainset))
        {
            PopulateSceneryConsists();
            _consist.LoadFrom(trainset);
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

        var editing = _consist.EditingTrainset;
        foreach (var trainset in scenery.Trainsets)
        {
            _consist.LoadFrom(trainset);
            if (ApplyTextureRandomizer(opts))
                WriteBackToScenery();
        }

        if (editing != null)
            _consist.LoadFrom(editing);
        RebuildConsist();
    }

    private async Task<TextureRandomizerDialog.Options?> AskTextureRandomizerOpts() =>
        TopLevel.GetTopLevel(this) is Window owner
            ? await new TextureRandomizerDialog(_hand).ShowAsync(owner)
            : null;

    private bool ApplyTextureRandomizer(TextureRandomizerDialog.Options opts)
    {
        if (_consist.Count == 0) return false;

        var rnd = new TexRandomizer(_db, _rng)
        {
            WithoutArchival = opts.WithoutArchival,
            RevisionTolerance = opts.RevisionTolerance,
            RevYear = opts.RevYear
        };
        if (rnd.Apply(_consist, NewVehicle) <= 0)
            return false;

        _consist.AutoConnectAll();
        return true;
    }

    private void RandomOrderButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _consist.RandomizeOrder(_rng);

    private void RandomOrientButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _consist.RandomizeOrientation(_rng);

    private void VehicleListBox_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_browser.Selected != null)
            ReplaceSelected(_browser.Selected);
    }

    private void AddVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_browser.Selected != null)
            AddTexture(_browser.Selected);
    }

    /// <summary>Keyboard shortcut entry points. See <see cref="Infrastructure.Shortcuts"/>.</summary>
    public void KeyAddVehicle() => AddVehicleButton_OnClick(null, null!);

    public void KeyRemoveVehicle() => RemoveVehicleButton_OnClick(null, null!);

    public void KeyClearConsist()
    {
        if (_consist.EditingTrainset is { } ts && ts.Vehicles.Count > 0)
            RemoveAllVehicles(ts);
    }

    public void KeySetWagonNumber() => _cards.ShowNumberFlyoutForSelection();

    public void KeyFocusSearch()
    {
        searchBox.Focus();
        searchBox.SelectAll();
    }

    public void ToolAddAllCategory() => AddAllCategoryButton_OnClick(null, null!);

    public void ToolAddUniqueMmd() => AddUniqueMmdButton_OnClick(null, null!);

    public void ToolRemoveAllTrains() => RemoveAllTrainsButton_OnClick(null, null!);

    private async void AddAllCategoryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var all = _browser.CurrentTextures();
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
        var all = _browser.CurrentTextures();
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

        if (_consist.EditingTrainset != null)
            _consist.LoadFrom(_consist.EditingTrainset);
        PopulateSceneryConsists();
        RebuildConsist();
    }

    private void ReplaceSelected(VehicleTexture texture)
    {
        if (_consist.Selected is null)
        {
            AddTexture(texture);
            return;
        }
        int i = _consist.IndexOf(_consist.Selected);
        if (i < 0)
        {
            AddTexture(texture);
            return;
        }

        var set = _db.ResolveSet(texture);
        var unit = set ?? new List<VehicleTexture> { texture };
        var item = new ConsistItem
        {
            Cars = unit.Select(NewVehicle).ToList(),
            Grouped = unit.Count > 1,
            Driver = _consist.Selected.Driver,
            Flipped = _consist.Selected.Flipped
        };
        _consist[i] = item;
        _consist.Selected = item;
        _consist.AutoConnectAll();
        RebuildConsist();
    }

    private void AddTexture(VehicleTexture texture)
    {
        int at = _consist.Count;
        if (_consist.Selected != null)
        {
            int si = _consist.IndexOf(_consist.Selected);
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
            Cars = unit.Select(NewVehicle).ToList(),
            Grouped = unit.Count > 1,
            Driver = eDriverType.Nobody,
            Flipped = false
        };

        at = Math.Clamp(at, 0, _consist.Count);
        MatchOccupancy(item, at);
        _consist.Insert(at, item);
        _consist.Selected = item;
        _consist.AutoConnectAll();
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
        string? cat = tex != null ? VehicleInfo.CategoryOf(tex) : null;
        if (!VehicleInfo.IsPoweredCategory(cat))
            return;

        bool staffed = _consist.Any(i =>
            i.Driver is eDriverType.Headdriver or eDriverType.Reardriver);
        if (position == 0 || !staffed)
        {
            item.Driver = eDriverType.Headdriver;
            lead.DriverType = eDriverType.Headdriver;
        }
    }

    private async void TextureBaseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var win = new TextureBaseWindow();
        await win.ShowDialog(owner);
        if (win.Picked != null)
            ReplaceSelected(win.Picked);
    }
}
