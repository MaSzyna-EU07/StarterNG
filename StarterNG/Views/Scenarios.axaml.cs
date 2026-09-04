using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Application;
using StarterNG.Domain.Sceneries;
using StarterNG.Presentation.Scenarios;

namespace StarterNG.Views;

public partial class Scenarios : UserControl
{
    public List<Scenery> Sceneries;

    private Scenery? _currentScenery;

    private bool _loadingWeather;

    private bool _todaySeason;

    private bool _loadingFilters;

    public Scenarios()
    {
        InitializeComponent();

        Sceneries = AppServices.Current.Library.Sceneries;

        _loadingFilters = true;
        archivalSwitch.IsChecked = !AppServices.Current.Settings.ShowArchivalSceneries;
        _loadingFilters = false;

        BuildSceneryTree();
        RestoreLastScenery();

        AttachedToVisualTree += (_, _) => OnReentered();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                OnReentered();
        };
    }

    private void OnReentered()
    {
        RefreshVehicleLabels();
        RefreshSelectedConsist();
    }

    private void BuildSceneryTree()
    {
        sceneryList.Items.Clear();

        bool includeArchival = archivalSwitch.IsChecked != true;
        bool expandGroups = AppServices.Current.Settings.AutoExpandSceneryTree;

        var nodes = new SceneryTreeBuilder(AppServices.Current.SceneryTexts)
            .Build(Sceneries, includeArchival, App.Loc.CurrentLangCode);

        foreach (var node in nodes)
            sceneryList.Items.Add(ToTreeItem(node, expandGroups));
    }

    private static TreeViewItem ToTreeItem(SceneryTreeNode node, bool expandGroups)
    {
        var item = new TreeViewItem
        {
            Header = node.Label,
            Tag = node.IsGroup ? null : node.SceneryIndex,
            IsExpanded = node.IsGroup && expandGroups
        };

        foreach (var child in node.Children)
            item.Items.Add(ToTreeItem(child, expandGroups));

        return item;
    }

    private void RestoreLastScenery()
    {
        string last = AppServices.Current.Settings.LastScenery;
        string want = Path.GetFileNameWithoutExtension(last);

        TreeViewItem? MatchLeaf(TreeViewItem node)
        {
            if (node.Tag is not int idx || idx < 0 || idx >= Sceneries.Count)
                return null;
            var scn = Sceneries[idx];
            if (string.IsNullOrWhiteSpace(want)) return null;
            if (string.Equals(scn.DisplayName, want, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(scn.Path), want, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Header as string, want, StringComparison.OrdinalIgnoreCase))
                return node;
            return null;
        }

        TreeViewItem? firstLeaf = null;
        foreach (var obj in sceneryList.Items)
        {
            if (obj is not TreeViewItem node)
                continue;

            if (node.Tag is int)
            {
                firstLeaf ??= node;
                if (MatchLeaf(node) is { } hit)
                {
                    ExpandAncestors(hit);
                    sceneryList.SelectedItem = hit;
                    return;
                }
            }

            foreach (var childObj in node.Items)
            {
                if (childObj is not TreeViewItem { Tag: int } child)
                    continue;
                firstLeaf ??= child;
                if (MatchLeaf(child) is { } hit)
                {
                    node.IsExpanded = true;
                    sceneryList.SelectedItem = hit;
                    return;
                }
            }
        }

        if (firstLeaf != null)
        {
            if (firstLeaf.Parent is TreeViewItem parent)
                parent.IsExpanded = true;
            sceneryList.SelectedItem = firstLeaf;
        }
    }

    private static void ExpandAncestors(TreeViewItem node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is TreeViewItem tvi)
                tvi.IsExpanded = true;
            parent = parent.Parent;
        }
    }

    private void PersistLastScenery(Scenery scenery)
    {

        string name = scenery.DisplayName;
        if (string.Equals(AppServices.Current.Settings.LastScenery, name, StringComparison.OrdinalIgnoreCase))
            return;
        AppServices.Current.Settings.LastScenery = name;
        AppServices.Current.SettingsStore.Save();
    }

    private static string TrainsetListLabel(Trainset trainset) =>
        TrainsetDisplay.CompositionText(trainset)
        ?? string.Format(App.Loc["EmptyTrainset"], trainset.Track ?? "");

    private static Control TrainsetListContent(Trainset trainset) =>
        new Avalonia.Controls.TextBlock
        {
            Text = TrainsetListLabel(trainset),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };

    private void PopulateVehicleList(Scenery scn)
    {
        vehicleList.Items.Clear();

        bool showAi = AppServices.Current.Settings.ShowAiVehicles;
        bool drivableOnly = AppServices.Current.Settings.DrivableOnly;

        for (int i = 0; i < scn.Trainsets.Count; i++)
        {
            var trainset = scn.Trainsets[i];

            if (IsAiTrainset(trainset) && !showAi) continue;
            if (drivableOnly && !UData.StaffedTrain(trainset)) continue;

            var listItem = new ListBoxItem
            {
                Content = TrainsetListContent(trainset),
                Tag = i
            };

            listItem.ContextMenu = ConsistContextMenu.Build(
                listItem,
                () => trainset,
                vehicles => ApplyConsist(listItem, trainset, vehicles));
            vehicleList.Items.Add(listItem);
        }

        if (vehicleList.Items.Count > 0)
            vehicleList.SelectedIndex = 0;
    }

    private static bool IsAiTrainset(Trainset trainset) =>
        !string.IsNullOrEmpty(trainset.Description) &&
        trainset.Description.TrimStart().StartsWith("-", StringComparison.Ordinal);

    private void ArchivalSwitch_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingFilters) return;

        AppServices.Current.Settings.ShowArchivalSceneries = archivalSwitch.IsChecked != true;
        AppServices.Current.SettingsStore.Save();

        BuildSceneryTree();
        RestoreLastScenery();
    }

    private void ApplyConsist(ListBoxItem item, Trainset trainset, List<Dynamic> vehicles)
    {
        trainset.Vehicles = vehicles;
        item.Content = TrainsetListContent(trainset);
        if (ReferenceEquals(AppServices.Current.State.CurrentTrainset, trainset))
            ShowConsist(trainset);
    }

    private void RefreshVehicleLabels()
    {
        if (sceneryList.SelectedItem is not TreeViewItem { Tag: int tag } || tag >= Sceneries.Count)
            return;

        var scn = Sceneries[tag];
        foreach (var obj in vehicleList.Items)
        {
            if (obj is ListBoxItem { Tag: int vi } lbi && vi < scn.Trainsets.Count)
                lbi.Content = TrainsetListContent(scn.Trainsets[vi]);
        }
    }

    private void SceneryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = sceneryList.SelectedItem as TreeViewItem;
        if (item?.Tag is not int tag)
        {
            foreach (TreeViewItem? toCollapse in sceneryList.Items)
            {
                if (toCollapse != null)
                    toCollapse.IsExpanded = false;
            }

            if (item != null)
                item.IsExpanded = true;
            if (item is { Items.Count: > 0 })
                sceneryList.SelectedItem = item.Items[0];
            return;
        }
        Scenery selectedScn = Sceneries[tag];

        ShowSceneryInfo(selectedScn);
        ShowWeather(selectedScn);
        PersistLastScenery(selectedScn);

        missionDescription.Text = "";
        timetableContent.Text = App.Loc["NoTimetable"];

        PopulateVehicleList(selectedScn);
    }

    private void ShowSceneryInfo(Scenery scenery)
    {
        AppServices.Current.SceneryTexts.LoadFor(scenery, App.Loc.CurrentLangCode);
        sceneryDescription.Text = scenery.LocalizedDescription(AppServices.Current.SceneryTexts);

        string? imagePath = AppServices.Current.SceneryImages.Resolve(scenery);
        if (imagePath is not null)
        {
            try
            {
                sceneryImage.Source = new Bitmap(imagePath);
            }
            catch (Exception ex)
            {
                sceneryImage.Source = null;
                StarterNG.Infrastructure.Diagnostics.Log($"Scenery image {imagePath}", ex);
            }
        }
        else
        {
            sceneryImage.Source = null;
        }

        PopulateAttachments(scenery);
        PopulateFaults(scenery);
    }

    private void PopulateFaults(Scenery scenery)
    {
        var lines = new List<string>();
        lines.AddRange(scenery.Faults);

        var db = AppServices.Current.Library.Vehicles;
        foreach (var ts in scenery.Trainsets)
        {
            if (!ts.Parsed)
                lines.Add($"# unparsed trainset: {ts.Name}");
            foreach (var v in ts.Vehicles)
            {
                if (string.IsNullOrEmpty(v.SkinFile)) continue;
                if (db.TextureForSkin(v.SkinFile) is null)
                    lines.Add($"# unknown texture: {v.Name} ({v.SkinFile})");
            }
        }

        foreach (var v in scenery.LooseVehicles)
        {
            if (db.TextureForSkin(v.SkinFile) is null)
                lines.Add($"# unknown loose texture: {v.Name} ({v.SkinFile})");
        }

        if (lines.Count > 0)
            StarterNG.Infrastructure.Diagnostics.Log(lines);
    }

    private void PopulateAttachments(Scenery scenery)
    {
        attachmentsPanel.Children.Clear();
        foreach (var att in scenery.Attachments)
        {
            var btn = new Button
            {
                Content = att.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = att.FilePath
            };
            btn.Classes.Add("Flat");
            ToolTip.SetTip(btn, att.FilePath);
            btn.Click += (_, _) => OpenAttachment(att.FilePath);
            attachmentsPanel.Children.Add(btn);
        }
    }

    private static void OpenAttachment(string relativePath)
    {
        string full = Path.GetFullPath(relativePath);
        if (!File.Exists(full))
            full = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
        if (!File.Exists(full))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log($"opening {full}", ex);
        }
    }

    public void RebuildAfterLanguageChange()
    {
        string? selected = AppServices.Current.State.CurrentScenery?.Path;
        BuildSceneryTree();
        if (!string.IsNullOrEmpty(selected))
        {
            AppServices.Current.Settings.LastScenery =
                Path.GetFileNameWithoutExtension(selected);
            RestoreLastScenery();
        }
        if (AppServices.Current.State.CurrentScenery != null)
            ShowSceneryInfo(AppServices.Current.State.CurrentScenery);
        RefreshSelectedConsist();
    }

    private bool _reloading;

    private async void ReloadSceneries_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            string? selected = AppServices.Current.State.CurrentScenery?.Path;
            await Task.Run(() => AppServices.Current.Library.ReloadSceneries());
            Sceneries = AppServices.Current.Library.Sceneries;
            BuildSceneryTree();
            if (!string.IsNullOrEmpty(selected))
            {
                AppServices.Current.Settings.LastScenery =
                    Path.GetFileNameWithoutExtension(selected);
            }
            RestoreLastScenery();
        }
        finally
        {
            _reloading = false;
        }
    }

    public event Action<Scenery>? RandomizeTexturesRequested;

    private void RandomTexturesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (AppServices.Current.State.CurrentScenery is { } scenery)
            RandomizeTexturesRequested?.Invoke(scenery);
    }

    public void RefreshConsistView()
    {
        RefreshVehicleLabels();
        if (AppServices.Current.State.CurrentTrainset is { } trainset)
            ShowConsist(trainset);
    }

    private static readonly Avalonia.Media.IBrush SelectedVehicleBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#41C400"));

    private static readonly Avalonia.Media.IBrush MissingMiniBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22808080"));

    private static Control MissingMiniBox(string? label, int height) => new Border
    {
        Height = height,
        Width = height * 2,
        Background = MissingMiniBrush,
        CornerRadius = new Avalonia.CornerRadius(4),
        Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center
        }
    };

    private readonly Dictionary<string, Bitmap?> _miniCache = new();

    private Bitmap? LoadMini(string? name, int height)
    {
        string key = $"{name}@{height}";
        if (_miniCache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? bmp = null;
        string? path = AppServices.Current.MiniTextures.PathFor(name);
        if (path != null)
        {
            try
            {
                using var fs = File.OpenRead(path);
                bmp = Bitmap.DecodeToHeight(fs, height);
            }
            catch (Exception ex)
            {
                StarterNG.Infrastructure.Diagnostics.Log($"Mini {path}", ex);
            }
        }
        _miniCache[key] = bmp;
        return bmp;
    }

    private void OpenSceneryDir_OnClick(object? sender, RoutedEventArgs e)
    {
        var scn = AppServices.Current.State.CurrentScenery;
        string dir = scn != null
            ? (Path.GetDirectoryName(Path.GetFullPath(scn.Path)) ?? "scenery")
            : Path.GetFullPath("scenery");
        if (!Directory.Exists(dir))
        {
            StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(string.Format(App.Loc["FaultFileNotFound"], dir));
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.ReportOnUiThread(
                $"{dir}{Environment.NewLine}{Environment.NewLine}{App.Loc["FaultDetail"]} {ex.Message}");
        }
    }

    private void VehicleList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {

        var tItem = sceneryList.SelectedItem as TreeViewItem;
        if (tItem?.Tag is not int tTag)
            return;
        Scenery selectedScn = Sceneries[tTag];

        ListBoxItem? vItem = vehicleList.SelectedItem as ListBoxItem;
        if (vItem?.Tag is not int vTag)
            return;
        Trainset selectedTrainset = selectedScn.Trainsets[vTag];

        AppServices.Current.State.CurrentScenery = selectedScn;
        AppServices.Current.State.CurrentTrainset = selectedTrainset;
        AppServices.Current.State.StartingVehicleName = TrainsetDisplay.DefaultStartingVehicle(selectedTrainset);

        ShowConsist(selectedTrainset);
        ShowTimetable(selectedScn, selectedTrainset);
    }

    private void ShowWeather(Scenery scenery)
    {
        _currentScenery = scenery;
        _loadingWeather = true;
        try
        {
            weatherForm.IsEnabled = true;
            var weather = scenery.Weather;
            weatherTime.SelectedTime = ParseTime(weather.ScenarioTimeOverride);
            weatherDate.SelectedDate = Seasons.DateOf(weather.Day);
            weatherTemp.Value = weather.Temperature;
            weatherFog.Value = FogScale.ToSlider(weather.FogEnd);
            SelectByTag(weatherSeason, weather.Day.ToString(), excludeFromNearest: "0");
            SelectByTag(weatherOvercast, weather.OvercastRandom
                ? RandomTag
                : weather.Overcast.ToString(CultureInfo.InvariantCulture));

            ApplyTodayPreview(weather.Day == 0);
            UpdateWeatherLabels();
        }
        finally
        {
            _loadingWeather = false;
        }
    }

    private void CaptureWeather()
    {
        if (_loadingWeather || _currentScenery is null)
            return;

        var weather = _currentScenery.Weather;
        if (weatherTime.SelectedTime is { } t)
            weather.ScenarioTimeOverride = $"{t.Hours:D2}:{t.Minutes:D2}";

        weather.Day = _todaySeason ? 0 : SelectedDayOfYear();
        weather.Temperature = weatherTemp.Value;
        weather.FogEnd = FogScale.ToMetres(weatherFog.Value);
        if ((weatherOvercast.SelectedItem as ComboBoxItem)?.Tag is string ov)
        {
            weather.OvercastRandom = ov == RandomTag;
            if (!weather.OvercastRandom &&
                double.TryParse(ov, NumberStyles.Float, CultureInfo.InvariantCulture, out double oc))
                weather.Overcast = oc;
        }

        weather.Dirty = true;
        UpdateWeatherLabels();
    }

    private void UpdateWeatherLabels()
    {
        weatherTempLabel.Text = $"{(int)weatherTemp.Value} °C";
        weatherFogLabel.Text = FogScale2(FogScale.ToMetres(weatherFog.Value));
        weatherDayDate.Text = string.Format(LanguageCulture, App.Loc["DayOfYear"], SelectedDayOfYear());

        weatherRestore.IsEnabled = _currentScenery?.Weather.Changed ?? false;
    }


    private static CultureInfo LanguageCulture
    {
        get
        {
            string code = App.Loc.CurrentLangCode;
            if (code is "en" or "")
                return CultureInfo.InvariantCulture;
            try
            {
                return CultureInfo.GetCultureInfo(code == "cz" ? "cs" : code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }

    /// <summary>Day-of-year arithmetic for the weather tab's date and season pickers.</summary>
    private static SeasonDates Seasons => new(AppServices.Current.Clock);

    /// <summary>Fog read-out in the language the user is reading the rest in.</summary>
    private static string FogScale2(int metres) => FogScale.Format(metres, LanguageCulture);

    private const string RandomTag = "random";

    private static void SelectByTag(ComboBox combo, string tag, string? excludeFromNearest = null)
    {
        ComboBoxItem? first = null;
        ComboBoxItem? nearest = null;
        double bestDistance = double.MaxValue;
        bool haveValue = double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture,
                                         out double value);

        foreach (var obj in combo.Items)
        {
            if (obj is not ComboBoxItem item)
                continue;
            first ??= item;
            if ((item.Tag as string) == tag)
            {
                combo.SelectedItem = item;
                return;
            }
            if (haveValue && item.Tag is string itemTag && itemTag != excludeFromNearest &&
                double.TryParse(itemTag, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double itemValue))
            {
                double distance = System.Math.Abs(itemValue - value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = item;
                }
            }
        }

        combo.SelectedItem = nearest ?? first;
    }

    private void WeatherTime_OnChanged(object? sender, TimePickerSelectedValueChangedEventArgs e) => CaptureWeather();

    private void WeatherTimeNow_OnClick(object? sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        weatherTime.SelectedTime = new TimeSpan(now.Hour, now.Minute, 0);
    }

    private void WeatherRestore_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentScenery is null) return;
        _currentScenery.Weather.Restore();
        ShowWeather(_currentScenery);
    }

    private static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var m = Regex.Match(text, @"^\s*(\d{1,2})[:.](\d{2})");
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out int h) && h is >= 0 and <= 23 &&
            int.TryParse(m.Groups[2].Value, out int min) && min is >= 0 and <= 59)
            return new TimeSpan(h, min, 0);
        return null;
    }

    private void ApplyTodayPreview(bool today)
    {
        _todaySeason = today;
        if (today)
        {
            bool prev = _loadingWeather;
            _loadingWeather = true;
            weatherDate.SelectedDate = Seasons.DateOf(0);
            _loadingWeather = prev;
        }
    }

    private void Weather_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => CaptureWeather();

    private void Weather_OnSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => CaptureWeather();

    private void WeatherDate_OnChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (!_loadingWeather)
        {
            _todaySeason = false;
            _loadingWeather = true;
            SelectByTag(weatherSeason, SelectedDayOfYear().ToString(), excludeFromNearest: "0");
            _loadingWeather = false;
        }
        CaptureWeather();
    }

    private int SelectedDayOfYear() =>
        weatherDate.SelectedDate is { } date ? date.DayOfYear : Seasons.Today;

    private void WeatherSeason_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingWeather)
            return;
        if ((weatherSeason.SelectedItem as ComboBoxItem)?.Tag is string tag &&
            int.TryParse(tag, out int day))
        {
            if (day == 0)
            {
                ApplyTodayPreview(true);
            }
            else
            {
                ApplyTodayPreview(false);
                _loadingWeather = true;
                weatherDate.SelectedDate = Seasons.DateOf(day);
                _loadingWeather = false;
            }
        }
        CaptureWeather();
    }

    private void ShowTimetable(Scenery scenery, Trainset trainset)
    {
        string? path = AppServices.Current.Timetables.Resolve(scenery, trainset.Name);
        if (path is null)
        {
            timetableContent.Text = App.Loc["NoTimetable"];
            UpdateTimetableTab(false);
            return;
        }

        try
        {
            timetableContent.Text = File.ReadAllText(path, Encoding.GetEncoding(1250));
            UpdateTimetableTab(true);
        }
        catch
        {
            timetableContent.Text = App.Loc["NoTimetable"];
            UpdateTimetableTab(false);
        }
    }

    private void UpdateTimetableTab(bool hasTimetable)
    {
        bool usable = hasTimetable || attachmentsPanel.Children.Count > 0;

        timetableTab.IsEnabled = usable;
        ToolTip.SetTip(timetableTab, usable ? null : App.Loc["NoTimetable"]);

        if (!usable && timetableTab.IsSelected && timetableTab.Parent is TabControl tabs)
            tabs.SelectedIndex = 0;
    }

    private const double ConsistDragThreshold = 6;

    private Trainset? _stripTrainset;
    private int _pressIndex = -1;
    private Point _pressPoint;
    private bool _dragging;
    private bool _stripWired;

    private int _dragBlockStart = -1;
    private int _dragBlockLength;
    private double _dragBlockWidth = 32;
    private double _grabOffset;
    private Control? _carried;

    private readonly List<int> _reducedToVehicle = new();
    private readonly List<int> _unitStarts = new();

    private readonly Infrastructure.DropGap _dropGap = new();
    private readonly Infrastructure.DropIndexTracker _dropTracker = new();
    private readonly Infrastructure.EdgeScroller _edgeScroller = new();

    private void ShowConsist(Trainset trainset)
    {
        consistStack.Children.Clear();
        _stripTrainset = trainset;
        _dropGap.Forget();

        if (!_stripWired)
        {
            _stripWired = true;
            consistStack.PointerMoved += Strip_OnPointerMoved;
            consistStack.PointerReleased += Strip_OnPointerReleased;
            consistStack.PointerCaptureLost += (_, _) => CancelConsistDrag();
        }

        ContextMenu VehicleMenu(Dynamic vehicle) => ConsistContextMenu.Build(
            consistStack,
            () => trainset,
            vehicles =>
            {
                trainset.Vehicles = vehicles;
                RefreshVehicleLabels();
                ShowConsist(trainset);
                UpdateTrainStats(trainset);
            },
            removeAll: null,
            getVehicle: () => vehicle,
            removeVehicle: v =>
            {
                int gap = trainset.Vehicles.IndexOf(v);
                trainset.Vehicles.Remove(v);

                bool wasPicked = !string.IsNullOrEmpty(v.Name) &&
                    string.Equals(v.Name, AppServices.Current.State.StartingVehicleName,
                                  StringComparison.OrdinalIgnoreCase);
                if (wasPicked && trainset.Vehicles.Count > 0)
                {
                    int next = Math.Clamp(gap, 0, trainset.Vehicles.Count - 1);
                    AppServices.Current.State.StartingVehicleName = trainset.Vehicles[next].Name;
                    AppServices.Current.State.NotifyChanged();
                }

                RefreshVehicleLabels();
                ShowConsist(trainset);
                UpdateTrainStats(trainset);
            });

        var db = AppServices.Current.Library.Vehicles;
        foreach (var train in trainset.Vehicles)
        {

            string miniName = db.MiniForSkin(train.SkinFile) ?? train.SkinFile;
            if (!AppServices.Current.MiniTextures.Has(miniName) && AppServices.Current.MiniTextures.Has(train.SkinFile))
                miniName = train.SkinFile;
            int thumbH = AppServices.Current.Settings.LargeThumbnails ? 64 : 32;

            var bmp = LoadMini(miniName, thumbH);
            Control visual = bmp is not null
                ? MiniTextures.Sharp(new Image
                {
                    Source = bmp,
                    Height = thumbH,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    Margin = new Thickness(0)
                })
                : MissingMiniBox(train.SkinFile, thumbH);

            bool selected = !string.IsNullOrEmpty(train.Name) &&
                string.Equals(train.Name, AppServices.Current.State.StartingVehicleName,
                              StringComparison.OrdinalIgnoreCase);

            var slot = new Border
            {
                Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(120)
                    }
                },
                Child = visual,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0, 2),
                BorderBrush = selected ? SelectedVehicleBrush : Avalonia.Media.Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                ContextMenu = VehicleMenu(train)
            };
            ToolTip.SetTip(slot, train.Name);

            int slotIndex = consistStack.Children.Count;

            slot.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(slot).Properties.IsLeftButtonPressed) return;
                _pressIndex = slotIndex;
                _pressPoint = ev.GetPosition(consistStack);
                _dragging = false;
            };

            consistStack.Children.Add(slot);
        }
        string desc = trainset.Description ?? "";
        if (desc.TrimStart().StartsWith('-'))
            desc = desc.TrimStart()[1..].TrimStart();
        missionDescription.Text = AppServices.Current.SceneryTexts.Translate(desc);
        UpdateTrainStats(trainset);
    }

    private void Strip_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(consistStack);

        if (!_dragging)
        {
            if (_pressIndex < 0 || !e.GetCurrentPoint(consistStack).Properties.IsLeftButtonPressed)
                return;

            var delta = point - _pressPoint;
            if (Math.Abs(delta.X) < ConsistDragThreshold && Math.Abs(delta.Y) < ConsistDragThreshold)
                return;

            BeginConsistDrag(e);
            if (!_dragging) return;
        }

        Canvas.SetLeft(_carried!, point.X - _grabOffset);

        double pointerX = e.GetPosition(consistScroll).X;
        if (_dropTracker.Update(pointerX + consistScroll.Offset.X))
        {
            int reduced = ReducedTarget();
            _dropGap.Show(consistStack,
                          reduced < _reducedToVehicle.Count
                              ? _reducedToVehicle[reduced]
                              : consistStack.Children.Count,
                          _dragBlockWidth);
        }

        _edgeScroller.Update(consistScroll, pointerX);
    }

    private void Strip_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            int index = _pressIndex;
            _pressIndex = -1;

            if (e.InitialPressMouseButton != MouseButton.Left ||
                index < 0 || _stripTrainset is not { } clicked ||
                index >= clicked.Vehicles.Count)
                return;

            AppServices.Current.State.StartingVehicleName = clicked.Vehicles[index].Name;
            AppServices.Current.State.NotifyChanged();
            ShowConsist(clicked);
            return;
        }

        int target = _dropTracker.Index >= 0 ? ReducedTarget() : -1;
        int start = _dragBlockStart;
        int length = _dragBlockLength;
        var trainset = _stripTrainset;

        EndConsistDrag();

        if (trainset != null && start >= 0 && target >= 0)
            MoveConsistBlock(trainset, start, length, target);
    }

    private void BeginConsistDrag(PointerEventArgs e)
    {
        if (_stripTrainset is not { } trainset || _pressIndex < 0 ||
            _pressIndex >= trainset.Vehicles.Count)
            return;

        _dragBlockStart = SetStart(trainset.Vehicles, _pressIndex);
        _dragBlockLength = SetLength(trainset.Vehicles, _dragBlockStart);
        if (_dragBlockStart + _dragBlockLength > consistStack.Children.Count)
            return;

        var first = consistStack.Children[_dragBlockStart];
        var blockBounds = first.Bounds;
        _dragBlockWidth = 0;
        for (int i = _dragBlockStart; i < _dragBlockStart + _dragBlockLength; i++)
            _dragBlockWidth += consistStack.Children[i].Bounds.Width;
        _dragBlockWidth = Math.Max(_dragBlockWidth, 16);
        _grabOffset = Math.Clamp(_pressPoint.X - blockBounds.Left, 0, _dragBlockWidth);

        CaptureDropTargets(trainset);

        _carried = BuildCarriedVisual(blockBounds.Height);
        Canvas.SetTop(_carried, blockBounds.Top);
        Canvas.SetLeft(_carried, blockBounds.Left);
        consistOverlay.Children.Add(_carried);

        for (int i = _dragBlockStart; i < _dragBlockStart + _dragBlockLength; i++)
            consistStack.Children[i].IsVisible = false;

        _dragging = true;
        e.Pointer.Capture(consistStack);
    }

    private void CaptureDropTargets(Trainset trainset)
    {
        var vehicles = trainset.Vehicles;

        _reducedToVehicle.Clear();
        for (int i = 0; i < vehicles.Count && i < consistStack.Children.Count; i++)
            if (i < _dragBlockStart || i >= _dragBlockStart + _dragBlockLength)
                _reducedToVehicle.Add(i);

        _unitStarts.Clear();
        var midpoints = new List<double>();

        int r = 0;
        while (r < _reducedToVehicle.Count)
        {
            int length = 1;
            while (r + length < _reducedToVehicle.Count &&
                   _reducedToVehicle[r + length] == _reducedToVehicle[r + length - 1] + 1 &&
                   HoldsNext(vehicles[_reducedToVehicle[r + length - 1]]))
                length++;

            double left = ChildEdge(_reducedToVehicle[r], right: false);
            double right = ChildEdge(_reducedToVehicle[r + length - 1], right: true);
            midpoints.Add((left + right) / 2);
            _unitStarts.Add(r);
            r += length;
        }

        _dropTracker.Capture(midpoints);
    }

    private double ChildEdge(int childIndex, bool right)
    {
        var bounds = consistStack.Children[childIndex].Bounds;
        double edge = right ? bounds.Right : bounds.Left;
        return childIndex > _dragBlockStart ? edge - _dragBlockWidth : edge;
    }

    private int ReducedTarget()
    {
        int unit = _dropTracker.Index;
        if (unit < 0) return -1;
        return unit < _unitStarts.Count ? _unitStarts[unit] : _reducedToVehicle.Count;
    }

    private Control BuildCarriedVisual(double height)
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };

        for (int i = _dragBlockStart; i < _dragBlockStart + _dragBlockLength; i++)
        {
            var source = consistStack.Children[i];
            Control copy = source is Border { Child: Image image }
                ? new Image { Source = image.Source, Height = image.Height, Stretch = image.Stretch }
                : new Border
                {
                    Width = source.Bounds.Width,
                    Height = source.Bounds.Height,
                    Background = MissingMiniBrush
                };
            panel.Children.Add(copy);
        }

        return new Border
        {
            Child = panel,
            Height = height,
            Opacity = 0.85,
            BorderThickness = new Thickness(0, 2),
            BorderBrush = SelectedVehicleBrush
        };
    }

    private void CancelConsistDrag()
    {
        if (!_dragging) return;
        EndConsistDrag();
    }

    private void EndConsistDrag()
    {
        for (int i = _dragBlockStart; i >= 0 && i < _dragBlockStart + _dragBlockLength; i++)
            if (i < consistStack.Children.Count)
                consistStack.Children[i].IsVisible = true;

        if (_carried != null)
            consistOverlay.Children.Remove(_carried);
        _carried = null;

        _dropTracker.Reset();
        _edgeScroller.Stop();
        _dropGap.Hide(consistStack);

        _dragBlockStart = -1;
        _dragBlockLength = 0;
        _pressIndex = -1;
        _dragging = false;
    }

    private void MoveConsistBlock(Trainset trainset, int start, int length, int target)
    {
        var vehicles = trainset.Vehicles;
        if (start < 0 || length <= 0 || start + length > vehicles.Count)
            return;

        var block = vehicles.GetRange(start, length);
        vehicles.RemoveRange(start, length);

        target = Math.Clamp(target, 0, vehicles.Count);
        if (target == start)
        {
            vehicles.InsertRange(start, block);
            return;
        }

        vehicles.InsertRange(target, block);

        RefreshVehicleLabels();
        ShowConsist(trainset);
        UpdateTrainStats(trainset);
    }

    private static bool HoldsNext(Dynamic car) =>
        car.Coupling.Has(Coupling.WorkshopLock) || car.Coupling.Locked;

    private static int SetStart(List<Dynamic> vehicles, int index)
    {
        while (index > 0 && HoldsNext(vehicles[index - 1]))
            index--;
        return index;
    }

    private static int SetLength(List<Dynamic> vehicles, int start)
    {
        int length = 1;
        while (start + length < vehicles.Count && HoldsNext(vehicles[start + length - 1]))
            length++;
        return length;
    }

    private void UpdateTrainStats(Trainset? trainset)
    {
        if (trainset is null)
        {
            trainStats.Children.Clear();
            return;
        }

        var db = AppServices.Current.Library.Vehicles;
        StarterNG.Infrastructure.StatsBar.Fill(trainStats, TrainsetDisplay.StatsFields(
            trainset,
            car => AppServices.Current.Physics.For(car.DataFolder, db.TextureForSkin(car.SkinFile)?.Model)
                   ?? AppServices.Current.Physics.For(car.DataFolder, car.MmdFile)
                   ?? AppServices.Current.Physics.For(car.DataFolder, car.SkinFile),
            car => db.TextureForSkin(car.SkinFile)?.ResolvedCategory));
    }

    private void RefreshSelectedConsist()
    {
        if (AppServices.Current.State.CurrentTrainset is { } trainset)
            ShowConsist(trainset);
    }
}
