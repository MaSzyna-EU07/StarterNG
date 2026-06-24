using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using StarterNG.Classes;

namespace StarterNG.Views;

public partial class Scenarios : UserControl
{
    public List<Scenery> Sceneries;

    // scenery whose weather the form is currently editing
    private Scenery? _currentScenery;
    // suppresses weather change handlers while the form is being populated
    private bool _loadingWeather;
    // "Today" season: the day field only previews the current day-of-year; the
    // stored Day stays 0 so the scenery keeps using the live date.
    private bool _todaySeason;

    // suppresses the list-filter handlers while their state is restored at startup
    private bool _loadingFilters;

    public Scenarios()
    {
        InitializeComponent();

        // Sceneries are parsed once at startup (behind the splash).
        Sceneries = GameData.Instance.Sceneries;

        // Restore the persisted list filters before building the tree/list. Guarded
        // so assigning IsChecked here doesn't run the rebuild handlers prematurely.
        _loadingFilters = true;
        showAiCheck.IsChecked = StarterNG.Classes.Settings.Instance.ShowAiVehicles;
        drivableOnlyCheck.IsChecked = StarterNG.Classes.Settings.Instance.DrivableOnly;
        archivalSwitch.IsChecked = StarterNG.Classes.Settings.Instance.ShowArchivalSceneries;
        _loadingFilters = false;

        BuildSceneryTree();

        // refresh the consist preview when the view is shown again (e.g. after
        // editing the consist in the depot). Switching tabs only toggles IsVisible
        // (the view stays in the tree), so listen for that too.
        AttachedToVisualTree += (_, _) => OnReentered();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                OnReentered();
        };
    }

    // Called whenever this view is shown again (tab switch / re-attach). Edits made
    // in the depot mutate the same Trainset objects, so refresh both the consist
    // preview and the vehicle-list captions that depend on them.
    private void OnReentered()
    {
        RefreshVehicleLabels();
        RefreshSelectedConsist();
    }

    // (Re)builds the scenery tree, honouring the "show archival" switch. Archival
    // sceneries (those declaring //$a) are skipped when the switch is off; group
    // folders that end up empty are omitted. The Tag keeps each node's original
    // scenery index, so the vehicles list stays aligned. Top-level nodes and group
    // children are sorted alphabetically.
    private void BuildSceneryTree()
    {
        sceneryList.Items.Clear();

        bool showArchival = archivalSwitch.IsChecked ?? true;
        var groupNodes = new Dictionary<string, TreeViewItem>();
        var topLevel = new List<TreeViewItem>();

        for (int i = 0; i < Sceneries.Count; i++)
        {
            var scenery = Sceneries[i];
            if (scenery.Archival && !showArchival)
                continue;

            if (string.IsNullOrEmpty(scenery.Group))
            {
                topLevel.Add(new TreeViewItem
                {
                    Header = Path.GetFileNameWithoutExtension(scenery.Path),
                    Tag = i
                });
                continue;
            }

            if (!groupNodes.TryGetValue(scenery.Group, out var groupNode))
            {
                groupNode = new TreeViewItem { Header = scenery.Group, IsExpanded = false };
                groupNodes[scenery.Group] = groupNode;
                topLevel.Add(groupNode);
            }

            groupNode.Items.Add(new TreeViewItem
            {
                Header = Path.GetFileNameWithoutExtension(scenery.Path),
                Tag = i
            });
        }

        foreach (var node in groupNodes.Values)
        {
            var children = node.Items.Cast<TreeViewItem>()
                .OrderBy(c => c.Header as string, StringComparer.OrdinalIgnoreCase)
                .ToList();
            node.Items.Clear();
            foreach (var child in children)
                node.Items.Add(child);
        }

        foreach (var item in topLevel.OrderBy(t => t.Header as string, StringComparer.OrdinalIgnoreCase))
            sceneryList.Items.Add(item);
    }

    // Fills the Vehicles list for a scenery, honouring the two filters:
    //  - "AI vehicles" off hides consists whose //$o description starts with '-';
    //  - "Drivable only" on hides decoration consists (the //$decor flag).
    private void PopulateVehicleList(Scenery scn)
    {
        vehicleList.Items.Clear();

        bool showAi = showAiCheck.IsChecked ?? true;
        bool drivableOnly = drivableOnlyCheck.IsChecked ?? true;

        for (int i = 0; i < scn.Trainsets.Count; i++)
        {
            var trainset = scn.Trainsets[i];
            string trainsetName = string.Join(" + ", trainset.Vehicles.Select(dyn => dyn.Name));
            if (string.IsNullOrWhiteSpace(trainsetName)) continue; // skip empty

            if (IsAiTrainset(trainset) && !showAi) continue;
            if (trainset.Decor && drivableOnly) continue;

            var listItem = new ListBoxItem
            {
                Content = trainsetName,
                Tag = i
            };
            // right-click: save/load to the warehouse + clipboard copy/paste
            listItem.ContextMenu = ConsistContextMenu.Build(
                listItem,
                () => trainset,
                vehicles => ApplyConsist(listItem, trainset, vehicles));
            vehicleList.Items.Add(listItem);
        }

        if (vehicleList.Items.Count > 0)
            vehicleList.SelectedIndex = 0;
    }

    // A computer-driven consist marks its //$o description with a leading '-'.
    private static bool IsAiTrainset(Trainset trainset) =>
        !string.IsNullOrEmpty(trainset.Description) &&
        trainset.Description.TrimStart().StartsWith("-", StringComparison.Ordinal);

    // AI/player vehicle filter toggled: persist the choice and rebuild the list
    // for the currently selected scenery.
    private void VehicleFilter_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingFilters) return;

        StarterNG.Classes.Settings.Instance.ShowAiVehicles = showAiCheck.IsChecked ?? true;
        StarterNG.Classes.Settings.Instance.DrivableOnly = drivableOnlyCheck.IsChecked ?? true;
        StarterNG.Classes.Settings.Instance.Save();

        if (sceneryList.SelectedItem is TreeViewItem { Tag: int tag } && tag < Sceneries.Count)
            PopulateVehicleList(Sceneries[tag]);
    }

    // Archival-scenery switch toggled: persist the choice and rebuild the tree.
    private void ArchivalSwitch_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingFilters) return;

        StarterNG.Classes.Settings.Instance.ShowArchivalSceneries = archivalSwitch.IsChecked ?? true;
        StarterNG.Classes.Settings.Instance.Save();

        BuildSceneryTree();
    }

    // Replaces a consist's vehicles (from a loaded warehouse preset or a pasted
    // clipboard consist) and refreshes the row caption / consist preview.
    private void ApplyConsist(ListBoxItem item, Trainset trainset, List<Dynamic> vehicles)
    {
        trainset.Vehicles = vehicles;
        item.Content = string.Join(" + ", trainset.Vehicles.Select(d => d.Name));
        if (ReferenceEquals(AppState.Instance.CurrentTrainset, trainset))
            ShowConsist(trainset);
    }

    // Rebuilds the caption of each entry in the Vehicles list from its trainset's
    // current vehicles, so a consist edited in the depot is reflected here too.
    // Selection and order are preserved (only the text is updated).
    private void RefreshVehicleLabels()
    {
        if (sceneryList.SelectedItem is not TreeViewItem { Tag: int tag } || tag >= Sceneries.Count)
            return;

        var scn = Sceneries[tag];
        foreach (var obj in vehicleList.Items)
        {
            if (obj is ListBoxItem { Tag: int vi } lbi && vi < scn.Trainsets.Count)
                lbi.Content = string.Join(" + ", scn.Trainsets[vi].Vehicles.Select(d => d.Name));
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

        // scenery-level info (distinct from the per-consist scenario description)
        ShowSceneryInfo(selectedScn);
        ShowWeather(selectedScn);

        // no consist chosen yet for this scenery
        missionDescription.Text = "";
        timetableContent.Text = App.Loc["NoTimetable"];

        // add the (filtered) trainsets to the Vehicles list
        PopulateVehicleList(selectedScn);
    }

    // Shows the selected scenery's description (//$d) and 1:1 image (//$i).
    private void ShowSceneryInfo(Scenery scenery)
    {
        sceneryDescription.Text = scenery.Description;

        string? imagePath = scenery.ImagePath;
        if (imagePath is not null)
        {
            try
            {
                sceneryImage.Source = new Bitmap(imagePath);
            }
            catch
            {
                sceneryImage.Source = null; // unreadable / unsupported image
            }
        }
        else
        {
            sceneryImage.Source = null;
        }
    }

    private void VehicleList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // get selected scenery
        var tItem = sceneryList.SelectedItem as TreeViewItem;
        if (tItem?.Tag is not int tTag)
            return;
        Scenery selectedScn = Sceneries[tTag];

        // get selected trainset
        ListBoxItem? vItem = vehicleList.SelectedItem as ListBoxItem;
        if (vItem?.Tag is not int vTag)
            return;
        Trainset selectedTrainset = selectedScn.Trainsets[vTag];

        // share the selection so the depot edits this consist in place
        AppState.Instance.CurrentScenery = selectedScn;
        AppState.Instance.CurrentTrainset = selectedTrainset;

        ShowConsist(selectedTrainset);
        ShowTimetable(selectedScn, selectedTrainset);
    }

    // Loads the scenery's weather into the editable form (Weather tab). Editing a
    // control writes the value back onto the scenery and marks it dirty, so the
    // change is injected into the exported .scn's config block on launch.
    private void ShowWeather(Scenery scenery)
    {
        _currentScenery = scenery;
        _loadingWeather = true;
        try
        {
            weatherForm.IsEnabled = true;
            weatherTime.SelectedTime = ParseTime(scenery.WeatherTime);
            weatherDay.Value = scenery.Day;
            weatherTemp.Value = scenery.Temperature;
            weatherFog.Value = System.Math.Clamp(scenery.FogEnd, 0, 10000);
            SelectByTag(weatherSeason, scenery.Day.ToString());
            SelectByTag(weatherOvercast, scenery.Overcast.ToString(CultureInfo.InvariantCulture));
            // Day 0 maps to the "Today" season entry: preview the live day-of-year.
            ApplyTodayPreview(scenery.Day == 0);
            UpdateWeatherLabels();
        }
        finally
        {
            _loadingWeather = false;
        }
    }

    // Reads the form back into the current scenery and flags it for export rewrite.
    private void CaptureWeather()
    {
        if (_loadingWeather || _currentScenery is null)
            return;

        var scn = _currentScenery;
        if (weatherTime.SelectedTime is { } t)
            scn.WeatherTime = $"{t.Hours:D2}:{t.Minutes:D2}";
        // In "Today" mode the day field is only a preview, so keep the stored day
        // at 0 (the scenery then follows the live date).
        scn.Day = _todaySeason ? 0 : (int)(weatherDay.Value ?? 0);
        scn.Temperature = weatherTemp.Value;
        scn.FogEnd = (int)weatherFog.Value;
        if ((weatherOvercast.SelectedItem as ComboBoxItem)?.Tag is string ov &&
            double.TryParse(ov, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double oc))
            scn.Overcast = oc;

        scn.WeatherDirty = true;
        UpdateWeatherLabels();
    }

    private void UpdateWeatherLabels()
    {
        weatherTempLabel.Text = $"{(int)weatherTemp.Value} °C";
        weatherFogLabel.Text = $"{(int)weatherFog.Value} m";
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem item && (item.Tag as string) == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        combo.SelectedItem = null;
    }

    // --- weather form event handlers ---
    private void WeatherTime_OnChanged(object? sender, TimePickerSelectedValueChangedEventArgs e) => CaptureWeather();

    // "Current time" button: drop the live wall-clock time into the picker (the
    // SelectedTimeChanged handler then captures it onto the scenery).
    private void WeatherTimeNow_OnClick(object? sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        weatherTime.SelectedTime = new TimeSpan(now.Hour, now.Minute, 0);
    }

    // Parses a stored "hh:mm" string into a TimeSpan, or null when it is missing
    // or malformed (so the picker simply shows no selection).
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

    // Toggles "Today" preview: the day field shows the live day-of-year (read-only)
    // and CaptureWeather keeps the stored Day at 0; any other season re-enables it.
    private void ApplyTodayPreview(bool today)
    {
        _todaySeason = today;
        weatherDay.IsEnabled = !today;
        if (today)
        {
            bool prev = _loadingWeather;
            _loadingWeather = true;
            weatherDay.Value = DateTime.Now.DayOfYear;
            _loadingWeather = prev;
        }
    }

    private void Weather_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => CaptureWeather();

    private void Weather_OnSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => CaptureWeather();

    private void WeatherDay_OnChanged(object? sender, NumericUpDownValueChangedEventArgs e) => CaptureWeather();

    // Picking a season presets the day-of-year, then captures the form.
    private void WeatherSeason_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingWeather)
            return;
        if ((weatherSeason.SelectedItem as ComboBoxItem)?.Tag is string tag &&
            int.TryParse(tag, out int day))
        {
            if (day == 0) // "Today" - preview the live day-of-year, keep stored day at 0
            {
                ApplyTodayPreview(true);
            }
            else
            {
                ApplyTodayPreview(false);
                _loadingWeather = true;
                weatherDay.Value = day;
                _loadingWeather = false;
            }
        }
        CaptureWeather();
    }

    // Reads and shows the timetable file referenced by the trainset (its first
    // "trainset" token is the timetable name). Shows a note when none is set or
    // the file cannot be found.
    private void ShowTimetable(Scenery scenery, Trainset trainset)
    {
        string? path = ResolveTimetablePath(scenery, trainset.Name);
        if (path is null)
        {
            timetableContent.Text = App.Loc["NoTimetable"];
            return;
        }

        try
        {
            timetableContent.Text = File.ReadAllText(path, Encoding.GetEncoding(1250));
        }
        catch
        {
            timetableContent.Text = App.Loc["NoTimetable"];
        }
    }

    // Probes the usual locations for a timetable file named after the trainset.
    private static string? ResolveTimetablePath(Scenery scenery, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Equals("none", System.StringComparison.OrdinalIgnoreCase))
            return null;

        string scnDir = Path.GetDirectoryName(scenery.Path) ?? ".";   // scenery/
        string root = Path.GetDirectoryName(scnDir) ?? ".";           // MaSzyna root

        var candidates = new[]
        {
            Path.Combine(root, "timetables", name + ".txt"),
            Path.Combine(scnDir, name + ".txt"),
            Path.Combine(root, "scenario", name + ".txt"),
            Path.Combine(root, "timetables", name),
            Path.Combine(scnDir, name),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // Renders the consist preview for a trainset (re-reads its current vehicles
    // so depot edits are reflected).
    private void ShowConsist(Trainset trainset)
    {
        consistStack.Children.Clear();

        var db = GameData.Instance.Vehicles;
        foreach (var train in trainset.Vehicles)
        {
            // thumbnail name comes from the matching texture's texture_mini
            string miniName = db.MiniForSkin(train.SkinFile) ?? train.SkinFile;
            string? path = VehicleDatabase.MiniPath(miniName);
            if (path is null)
                continue; // fallback: no mini

            consistStack.Children.Add(new Image
            {
                Source = new Bitmap(path),
                Height = 32,
                Stretch = Avalonia.Media.Stretch.Uniform,
                Margin = new Thickness(0)
            });
        }
        missionDescription.Text = trainset.Description;
    }

    // When returning to this view, refresh the preview of the selected consist
    // in case it was modified in the depot.
    private void RefreshSelectedConsist()
    {
        if (AppState.Instance.CurrentTrainset is { } trainset)
            ShowConsist(trainset);
    }
}
