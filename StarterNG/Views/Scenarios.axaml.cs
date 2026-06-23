using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

    public Scenarios()
    {
        InitializeComponent();

        // Sceneries are parsed once at startup (behind the splash).
        Sceneries = GameData.Instance.Sceneries;

        var groupNodes = new Dictionary<string, TreeViewItem>();

        for (int i = 0; i < Sceneries.Count; i++)
        {
            var scenery = Sceneries[i];

            // case 1: no group - put without parent
            if (string.IsNullOrEmpty(scenery.Group))
            {
                sceneryList.Items.Add(new TreeViewItem
                {
                    Header = Path.GetFileNameWithoutExtension(scenery.Path),
                    Tag = i
                });

                continue;
            }

            // case 2 - group scenery with others
            if (!groupNodes.TryGetValue(scenery.Group, out var groupNode))
            {
                groupNode = new TreeViewItem
                {
                    Header = scenery.Group,
                    IsExpanded = false
                };

                groupNodes[scenery.Group] = groupNode;
                sceneryList.Items.Add(groupNode);
            }

            groupNode.Items.Add(new TreeViewItem
            {
                Header = Path.GetFileNameWithoutExtension(scenery.Path),
                Tag = i
            });
        }

        // refresh the consist preview when the view is shown again (e.g. after
        // editing the consist in the depot). Switching tabs only toggles IsVisible
        // (the view stays in the tree), so listen for that too.
        AttachedToVisualTree += (_, _) => RefreshSelectedConsist();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                RefreshSelectedConsist();
        };
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
        
        vehicleList.Items.Clear();

        // scenery-level info (distinct from the per-consist scenario description)
        ShowSceneryInfo(selectedScn);
        ShowWeather(selectedScn);

        // no consist chosen yet for this scenery
        missionDescription.Text = "";
        timetableContent.Text = App.Loc["NoTimetable"];

        // add all trainsets to list
        for (int i = 0; i < selectedScn.Trainsets.Count; i++)
        {
            string trainsetName = string.Join(
                " + ",
                selectedScn.Trainsets[i].Vehicles.Select(dyn => dyn.Name)
            );
            if (string.IsNullOrWhiteSpace(trainsetName)) continue; // skip empty
            vehicleList.Items.Add(new ListBoxItem
            {
                Content = trainsetName,
                Tag = i
            });
        }

        if (vehicleList.Items.Count > 0)
        {
            vehicleList.SelectedIndex = 0;
        }
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
            weatherTime.Text = scenery.WeatherTime;
            weatherDay.Value = scenery.Day;
            weatherTemp.Value = scenery.Temperature;
            weatherFog.Value = System.Math.Clamp(scenery.FogEnd, 50, 2500);
            SelectByTag(weatherSeason, scenery.Day.ToString());
            SelectByTag(weatherOvercast, scenery.Overcast.ToString(CultureInfo.InvariantCulture));
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
        if (!string.IsNullOrWhiteSpace(weatherTime.Text))
            scn.WeatherTime = weatherTime.Text!.Trim();
        scn.Day = (int)(weatherDay.Value ?? 0);
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
    private void Weather_OnChanged(object? sender, RoutedEventArgs e) => CaptureWeather();

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
            _loadingWeather = true;
            weatherDay.Value = day;
            _loadingWeather = false;
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
