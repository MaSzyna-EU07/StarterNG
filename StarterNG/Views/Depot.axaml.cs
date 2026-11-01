using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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

    private readonly ConsistHistory _history = new();

    /// <summary>Set while an undo or redo puts a state back, which is not a change of its own.</summary>
    private bool _restoring;

    private readonly WarehouseEdit _warehouseEdit = new();

    private bool EditingWarehouse =>
        _warehouseEdit.Trainset is not null && ReferenceEquals(_consist.EditingTrainset, _warehouseEdit.Trainset);

    public Depot()
    {
        _info = new VehicleInfo(_db, AppServices.Current.Physics);
        _consist = new Consist(_db, _info);
        _cargo = new Cargo(_info, _rng);
        _consist.Changed += RebuildConsist;

        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        _warehouse = new Warehouse(warehouseList, removeFromWarehouseButton, _hand,
            () => AppServices.Current.State.CurrentTrainset, ApplyFromWarehouse);
        _warehouse.SelectionChanged += () =>
        {
            if (WarehouseTabOpen)
                EditWarehouseSelection();
        };
        _warehouse.Renamed += _warehouseEdit.Renamed;

        _browser = new VehicleBrowser(categoryCombo, classCombo, vehicleListBox, searchBox,
            hideArchivalCheck, miniPreview, addVehicleButton, _db, _minis, OpenVehicleBase)
        {
            AddMany = (texture, count) => AddTexture(texture, count),
            Replace = ReplaceSelected
        };

        _drag = new ConsistDragging(consistStack, consistScroll, consistOverlay,
            consistDropTarget, vehicleListBox, miniPreviewPanel,
            _consist, () => _browser.Selected, InsertTextureAt, _browser.SelectInBrowser);

        _cards = new VehicleCards(consistStack, _db, _consist, _cargo, _minis, _hand,
            RebuildConsist, () => _details.Refresh(), _browser.SelectInBrowser, _drag.ArmCardDrag)
        {
            AddCopies = AddCopiesAfter,
            BrowserSelection = () => _browser.Selected,
            ReplaceWith = (item, texture) =>
            {
                _consist.Selected = item;
                ReplaceSelected(texture);
            }
        };

        // Hidden still scrolls - it only drops the bar itself.
        MiniTextures.Sharp(miniPreview);

        _details = new VehicleDetails(
            generalPanel, consistTexturePanel, couplerPanel, couplerActions,
            brakesPanel, loadsPanel, damagePanel, removeVehicleButton,
            _info, _consist, _cargo, _hand,
            RebuildConsist, _cards.RefreshMemberChrome, _browser.ShowTextureInfo)
        {
            // A warehouse consist is kept in its file; a scenery one is the list itself.
            Edited = () =>
            {
                if (EditingWarehouse)
                    _warehouseEdit.Save();
            }
        };

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
            if (e.Property != IsVisibleProperty)
                return;
            if (!IsVisible)
            {
                RememberConsistOnLeaving();
                return;
            }
            if (ConsistMovedSinceLeaving())
                ShowSceneryTab();
            SyncFromSelection();
            _warehouse.Refresh();
        };
    }

    private Trainset? _leftTrainset;

    private List<Dynamic> _leftVehicles = new();

    private void RememberConsistOnLeaving()
    {
        _leftTrainset = AppServices.Current.State.CurrentTrainset;
        _leftVehicles = _leftTrainset?.Vehicles.ToList() ?? new List<Dynamic>();
    }

    /// <summary>Another consist was picked or this one changed on another page: the warehouse tab left open would hide it.</summary>
    private bool ConsistMovedSinceLeaving() =>
        AppServices.Current.State.CurrentTrainset is { } trainset &&
        (!ReferenceEquals(trainset, _leftTrainset) || !trainset.Vehicles.SequenceEqual(_leftVehicles));

    private void AddToWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _warehouse.SaveCurrent((Control)sender!);

    private void RemoveFromWarehouseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = _warehouse.RemoveSelectedAsync();

    private bool WarehouseTabOpen => ReferenceEquals(consistSourceTabs.SelectedItem, warehouseTab);

    private void ConsistSourceTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The lists inside the tabs raise the same event, and it bubbles up here.
        if (!ReferenceEquals(e.Source, consistSourceTabs) || Design.IsDesignMode)
            return;

        if (WarehouseTabOpen)
        {
            // A consist saved from a menu elsewhere went in while this list was not shown.
            _warehouse.Refresh();
            EditWarehouseSelection();
        }
        else
            EditSceneryConsist();
    }

    /// <summary>The strip takes the warehouse consist selected, or goes empty without one.</summary>
    private void EditWarehouseSelection()
    {
        if (_warehouse.SelectedPreset() is not { } preset)
        {
            _warehouseEdit.Close();
            _consist.Unload();
            return;
        }

        var trainset = _warehouseEdit.Open(preset);
        if (ReferenceEquals(_consist.EditingTrainset, trainset))
            RebuildConsist();
        else
            _consist.LoadFrom(trainset);
    }

    /// <summary>Back on the scenery tab, the strip edits the consist that will be started.</summary>
    private void EditSceneryConsist()
    {
        _warehouseEdit.Close();
        if (AppServices.Current.State.CurrentTrainset is { } trainset)
            _consist.LoadFrom(trainset);
        else
            _consist.Unload();
    }

    private void ShowSceneryTab()
    {
        if (WarehouseTabOpen)
            consistSourceTabs.SelectedItem = sceneryConsistsTab;
    }

    /// <summary>From the warehouse's menu: shown where it went, on the scenery tab.</summary>
    private void ApplyFromWarehouse(Trainset target, List<Dynamic> vehicles)
    {
        ShowSceneryTab();
        ApplyConsist(target, vehicles);
    }

    private bool IsSceneryTrainset(Trainset trainset) =>
        _listScenery?.Trainsets.Contains(trainset) == true;

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

    /// <summary>The scenery files changed under the page in view; out of view it catches up on its way back.</summary>
    public void FollowSceneryFiles()
    {
        if (IsVisible)
            SyncFromSelection();
    }

    private void SyncFromSelection()
    {

        _browser.SyncHideArchival();

        PopulateSceneryConsists();

        var trainset = AppServices.Current.State.CurrentTrainset;
        if (WarehouseTabOpen)
            RebuildConsist();
        else if (trainset is null)
        {
            // Another scenery, with no consist picked: the old one's is not edited on.
            if (_consist.EditingTrainset != null)
                _consist.Unload();
        }
        else if (!ReferenceEquals(trainset, _consist.EditingTrainset))
            _consist.LoadFrom(trainset);
        else
        {
            ReloadIfEditedElsewhere();
            RebuildConsist();
        }
    }

    /// <summary>
    /// The strip holds the vehicles written back last. A list that no longer holds
    /// them was changed on the scenarios page or by a tool, and the next write-back
    /// would put the strip's old vehicles over that change.
    /// </summary>
    private void ReloadIfEditedElsewhere()
    {
        if (_consist.EditingTrainset is { } trainset && !_consist.Flatten().SequenceEqual(trainset.Vehicles))
            _consist.LoadFrom(trainset, _consist.Selected is { } selected ? Math.Max(_consist.IndexOf(selected), 0) : 0);
    }

    public void RebuildAfterLanguageChange()
    {
        _browser.PopulateCategoryCombo();
        PopulateSceneryConsists();
        ReloadIfEditedElsewhere();
        RebuildConsist();
        _details.Refresh();
    }

    /// <summary>Redraws what depends on the thumbnail size: the consist and the class list.</summary>
    public void RefreshConsistView()
    {
        _browser.InitClassComboTemplates();
        ReloadIfEditedElsewhere();
        RebuildConsist();
    }

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
        emptyHint.Text = _consist.EditingTrainset is not null
            ? App.Loc["ConsistEmpty"]
            : App.Loc[WarehouseTabOpen ? "WarehousePickHint" : "ConsistPickHint"];

        // A selection the consist moved on its own - after a removal, say - is only
        // highlighted; activating it runs what a click would have run.
        if (_consist.Selected is { } moved &&
            (_consist.SelectedCar is null || !moved.Cars.Contains(_consist.SelectedCar)))
            _cards.Activate(moved);

        // The stats are read off the trainset, so it is written back first.
        WriteBackToScenery();
        RecordHistory();
        _details.Refresh();
        UpdateTrainStats();
        UpdateConsistChrome();

        // The cards are thrown away and rebuilt on every change, so a keyboard-driven
        // edit has to be handed its card back or focus escapes the strip.
        _cards.FocusSelectedCard(onlyIfRequested: true);
    }

    private void WriteBackToScenery()
    {
        if (_consist.EditingTrainset is null)
            return;

        _consist.EditingTrainset.Vehicles = _consist.Flatten();
        if (EditingWarehouse)
        {
            _warehouseEdit.Save();
            return;
        }

        RefreshConsistListLabels();

        // The start button reads the crew off the trainset, which was not written
        // back yet when the selection named the vehicle to start in - without this
        // it went on saying "no crew" until a vehicle was clicked on the scenarios tab.
        if (ReferenceEquals(_consist.EditingTrainset, AppServices.Current.State.CurrentTrainset))
            AppServices.Current.State.NotifyChanged();
    }

    /// <summary>
    /// The title says which consist the strip edits; the scenery file's tools and
    /// "add to warehouse" are for a scenery consist only.
    /// </summary>
    private void UpdateConsistChrome()
    {
        bool warehouse = EditingWarehouse;
        consistGroup.Header = warehouse
            ? string.Format(App.Loc["ConsistFromWarehouse"], _warehouseEdit.Name)
            : App.Loc["Consist"];

        var trainset = _consist.EditingTrainset;
        bool scenery = trainset is not null && !warehouse && IsSceneryTrainset(trainset);
        bool modified = scenery && SceneryTrainsetText.IsModified(trainset!);
        bool sceneryTab = !WarehouseTabOpen;
        saveToSceneryButton.IsVisible = sceneryTab;
        revertSceneryButton.IsVisible = sceneryTab;
        addToWarehouseToolButton.IsVisible = sceneryTab;
        saveToSceneryButton.IsEnabled = modified;
        revertSceneryButton.IsEnabled = modified;

        reversedBadge.IsVisible = trainset?.Reversed == true;
    }

    private void SaveToSceneryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = SaveToSceneryAsync(_consist.EditingTrainset);

    private void RevertSceneryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RevertFromScenery(_consist.EditingTrainset);

    public void KeySaveToScenery()
    {
        if (!EditingWarehouse)
            _ = SaveToSceneryAsync(_consist.EditingTrainset);
    }

    /// <summary>
    /// Writes the consist into the scenery file for good. Asked first only while
    /// the file has no backup yet - the first time it is about to change.
    /// </summary>
    private async Task SaveToSceneryAsync(Trainset? trainset)
    {
        if (trainset is null || _listScenery is not { } scenery || !IsSceneryTrainset(trainset) ||
            !SceneryTrainsetText.IsModified(trainset) || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        // A consist from an included file is saved there - shared, maybe, by other scenarios.
        string path = SaveSceneryTrainset.FileOf(scenery, trainset);
        string file = trainset.SourceFile ?? Path.GetFileName(scenery.Path);
        if (!AppServices.Current.Files.FileExists(path + SaveSceneryTrainset.BackupSuffix) &&
            !await MessageBox.Show(owner,
                string.Format(App.Loc["SaveToSceneryConfirm"], file, file + SaveSceneryTrainset.BackupSuffix),
                App.Loc["SaveToScenery"], MessageBoxButtons.YesNo))
            return;

        var (outcome, detail) = AppServices.Current.SaveTrainset.Execute(scenery, trainset);
        switch (outcome)
        {
            case SceneryTrainsetSaveOutcome.NotFound:
                await StarterNG.Infrastructure.Diagnostics.ReportAsync(
                    string.Format(App.Loc["SaveToSceneryNotFound"], file));
                break;
            case SceneryTrainsetSaveOutcome.Failed:
                await StarterNG.Infrastructure.Diagnostics.ReportAsync(
                    $"{path}{Environment.NewLine}{Environment.NewLine}{App.Loc["FaultDetail"]} {detail}");
                break;
        }

        RefreshConsistListLabels();
        UpdateConsistChrome();
    }

    /// <summary>The consist as its scenery file has it - one more step to undo.</summary>
    private void RevertFromScenery(Trainset? trainset)
    {
        if (trainset is null || !IsSceneryTrainset(trainset) || !SceneryTrainsetText.IsModified(trainset))
            return;

        var saved = new Trainset(trainset.RawEntry);
        trainset.Vehicles = saved.Vehicles;
        trainset.Velocity = saved.Velocity;
        if (ReferenceEquals(_consist.EditingTrainset, trainset))
            _consist.LoadFrom(trainset);
        else
            RefreshConsistListLabels();
    }

    private int SelectedIndex() =>
        _consist.Selected is { } item ? _consist.IndexOf(item) : 0;

    private void RecordHistory()
    {
        if (_consist.EditingTrainset is { } trainset)
            _history.Record(trainset, SelectedIndex(), rebase: _restoring);

        var editing = _consist.EditingTrainset;
        undoButton.IsEnabled = editing is not null && _history.CanUndo(editing);
        redoButton.IsEnabled = editing is not null && _history.CanRedo(editing);
    }

    public void Undo() => Restore(_history.Undo);

    public void Redo() => Restore(_history.Redo);

    private void Restore(Func<Trainset, int, ConsistHistory.State?> step)
    {
        if (_consist.EditingTrainset is not { } trainset ||
            step(trainset, SelectedIndex()) is not { } state)
            return;

        trainset.Vehicles = state.Vehicles.ToList();
        _restoring = true;
        try
        {
            _consist.LoadFrom(trainset, state.Selected);
        }
        finally
        {
            _restoring = false;
        }
    }

    private void UndoButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Undo();

    private void RedoButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Redo();

    private void ClearConsistButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        KeyClearConsist();

    private void ReplaceVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_browser.Selected != null)
            ReplaceSelected(_browser.Selected);
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
            _info.CategoryOf,
            AppServices.Current.LoadWeights.Table.WeightOf));
    }

    private void RemoveVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        KeyRemoveVehicle();

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
                if (!TrainsetDisplay.ListedIn(trainset, showAi, drivableOnly, AppServices.Current.State.CurrentTrainset))
                    continue;

                var entry = new ListBoxItem
                {
                    Content = ConsistListContent(trainset),
                    Tag = trainset
                };

                var ts = trainset;

                // The random tools work on the consist being edited, which is not
                // necessarily the entry the menu was opened on.
                void Edit(Action run)
                {
                    if (!ReferenceEquals(_consist.EditingTrainset, ts))
                        sceneryConsistList.SelectedItem = entry;
                    run();
                }

                entry.ContextMenu = ConsistContextMenu.Build(
                    entry,
                    () => ts,
                    vehicles => ApplyConsist(ts, vehicles),
                    () => RemoveAllVehicles(ts),
                    randomOrder: () => Edit(() => _consist.RandomizeOrder(_rng)),
                    randomTurn: () => Edit(() => _consist.RandomizeOrientation(_rng)),
                    randomTextures: () => Edit(() => _ = RunTextureRandomizer()),
                    removeAllGesture: new KeyGesture(Key.Delete, KeyModifiers.Control),
                    sceneryFile: new ConsistContextMenu.SceneryFileActions(
                        () => SceneryTrainsetText.IsModified(ts),
                        () => _ = SaveToSceneryAsync(ts),
                        () => RevertFromScenery(ts)));
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
        EditTrainset(target);
    }

    private void RemoveAllVehicles(Trainset target)
    {
        target.Vehicles = new List<Dynamic>();
        EditTrainset(target);
    }

    /// <summary>
    /// A scenery consist becomes the one started as well; a warehouse consist is
    /// only edited.
    /// </summary>
    private void EditTrainset(Trainset target)
    {
        if (!IsSceneryTrainset(target))
        {
            _consist.LoadFrom(target);
            return;
        }

        AppServices.Current.State.CurrentScenery = _listScenery;
        AppServices.Current.State.CurrentTrainset = target;
        _consist.LoadFrom(target);
        PopulateSceneryConsists();
    }

    /// <summary>The composition, after a dot for a consist the scenery file does not have yet.</summary>
    private static Control ConsistListContent(Trainset trainset)
    {
        var label = Scenarios.TrainsetListContent(trainset);
        if (!SceneryTrainsetText.IsModified(trainset))
            return label;

        var dot = new TextBlock
        {
            Text = "●",
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = new SolidColorBrush(Color.Parse("#E0A030"))
        };
        ToolTip.SetTip(dot, App.Loc["ConsistUnsaved"]);
        var row = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(label);
        return row;
    }

    private void RefreshConsistListLabels()
    {
        foreach (var obj in sceneryConsistList.Items)
        {
            if (obj is ListBoxItem { Tag: Trainset ts } item)
                item.Content = ConsistListContent(ts);
        }
    }

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
        // On a row only: the list's scrollbar takes double clicks too.
        if (_browser.Selected != null &&
            (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            ReplaceSelected(_browser.Selected);
    }

    private void AddVehicleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        KeyAddVehicle();

    /// <summary>Keyboard shortcut entry points. See <see cref="Infrastructure.Shortcuts"/>.</summary>
    public void KeyAddVehicle()
    {
        if (_browser.Selected != null)
            AddTexture(_browser.Selected);
    }

    public void KeyRemoveVehicle()
    {
        if (_consist.Selected is { } item)
            _consist.Remove(item);
    }

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

    // The tools are reached from any page, and this view catches up with the
    // selection only when shown - so they do that first, or they would work on the
    // scenery it showed last. They are about the scenery, so never on the warehouse's.
    private void PrepareForTool()
    {
        ShowSceneryTab();
        SyncFromSelection();
    }

    /// <summary>What the browser lists, for a tool to add; with nothing listed it says what to pick.</summary>
    private List<VehicleTexture> TexturesForTool()
    {
        var all = _browser.CurrentTextures();
        if (all.Count == 0)
            _ = StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["PickCategoryFirst"]);
        return all;
    }

    public void ToolAddAllCategory()
    {
        PrepareForTool();
        foreach (var t in TexturesForTool())
            InsertTextureAt(t, _consist.Count);
    }

    public void ToolAddUniqueMmd()
    {
        PrepareForTool();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TexturesForTool())
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

    public async Task ToolRemoveAllTrainsAsync()
    {
        PrepareForTool();
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        // Not the first one on the list: a scenery nobody picked is not emptied.
        var state = AppServices.Current.State;
        if (state.CurrentScenery is not { } scenery)
        {
            await StarterNG.Infrastructure.Diagnostics.ReportAsync(App.Loc["NoSceneryConsists"]);
            return;
        }

        if (!await MessageBox.Show(owner, App.Loc["RemoveAllTrainsConfirm"],
                                   App.Loc["RemoveAllTrains"], MessageBoxButtons.YesNo))
            return;

        foreach (var ts in scenery.Trainsets)
            ts.RemoveVehicles();

        // As in the old starter nothing stays picked, so a start is the free camera.
        state.StartingVehicleName = null;
        state.CurrentTrainset = null;
        _consist.Unload();
        PopulateSceneryConsists();
        RebuildConsist();
        state.NotifyChanged();
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
            Grouped = unit.Count > 1
        };
        _consist.Replace(i, item);
        _consist.Selected = item;
        _consist.AutoConnectAll();
        RebuildConsist();
    }

    private void AddTexture(VehicleTexture texture, int count = 1)
    {
        int at = _consist.Count;
        if (_consist.Selected != null)
        {
            int si = _consist.IndexOf(_consist.Selected);
            if (si >= 0) at = si + 1;
        }
        InsertTexturesAt(texture, at, count);
    }

    /// <summary>Copies of the vehicle on a card - a whole unit for one of a set - right after it.</summary>
    private void AddCopiesAfter(ConsistItem item, int count)
    {
        int index = _consist.IndexOf(item);
        if (index < 0 || item.Cars.Count == 0 || _info.TextureFor(item.Cars[0]) is not { } texture)
            return;

        InsertTexturesAt(texture, index + 1, count);
    }

    private void InsertTextureAt(VehicleTexture texture, int at) => InsertTexturesAt(texture, at, 1);

    /// <summary>The vehicle count times in a row from at - each staffed as added - and one redraw.</summary>
    private void InsertTexturesAt(VehicleTexture texture, int at, int count)
    {
        // With no consist picked the vehicles would be nowhere but on the strip, and
        // gone with its next load. The strip's hint says what to pick.
        if (_consist.EditingTrainset is null)
            return;

        var set = _db.ResolveSet(texture);
        var unit = set ?? new List<VehicleTexture> { texture };

        at = Math.Clamp(at, 0, _consist.Count);
        for (int n = 0; n < count; n++, at++)
        {
            var item = new ConsistItem
            {
                Cars = unit.Select(NewVehicle).ToList(),
                Grouped = unit.Count > 1,
                Driver = eDriverType.Nobody,
                Flipped = false
            };

            _consist.Staff(item, at);
            _consist.Insert(at, item);
            _consist.Selected = item;
        }

        _consist.AutoConnectAll();
        RebuildConsist();
    }

    private void TextureBaseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenVehicleBase();

    private async void OpenVehicleBase()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var win = new VehicleBaseWindow();
        await win.ShowDialog(owner);
        _browser.SyncHideArchival();
        if (win.Picked is null)
            return;

        if (win.PickedToAdd)
            AddTexture(win.Picked);
        else
            ReplaceSelected(win.Picked);
    }
}
