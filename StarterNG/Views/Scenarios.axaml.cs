using System.Collections.Generic;
using System.Diagnostics;
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
    public List<Scenery> sceneries;

    public Scenarios()
    {
        InitializeComponent();

        // Sceneries are parsed once at startup (behind the splash).
        sceneries = GameData.Instance.Sceneries;

        var groupNodes = new Dictionary<string, TreeViewItem>();

        for (int i = 0; i < sceneries.Count; i++)
        {
            var scenery = sceneries[i];

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
                    IsExpanded = true
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
        // editing the consist in the depot)
        AttachedToVisualTree += (_, _) => RefreshSelectedConsist();
    }

    private void SceneryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = sceneryList.SelectedItem as TreeViewItem;
        if (item?.Tag is not int tag)
            return;
        Scenery selectedScn = sceneries[tag];
        vehicleList.Items.Clear();

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

    }

    private void VehicleList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // get selected scenery
        var tItem = sceneryList.SelectedItem as TreeViewItem;
        if (tItem?.Tag is not int tTag)
            return;
        Scenery selectedScn = sceneries[tTag];

        // get selected trainset
        ListBoxItem? vItem = vehicleList.SelectedItem as ListBoxItem;
        if (vItem?.Tag is not int vTag)
            return;
        Trainset selectedTrainset = selectedScn.Trainsets[vTag];

        // share the selection so the depot edits this consist in place
        AppState.Instance.CurrentScenery = selectedScn;
        AppState.Instance.CurrentTrainset = selectedTrainset;

        ShowConsist(selectedTrainset);
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
                Height = 45,
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

    // Exports the (possibly depot-modified) scenery to a $-prefixed copy and
    // launches the game on it with the selected consist's driven vehicle.
    private void StartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var scenery = AppState.Instance.CurrentScenery;
        if (scenery is null)
            return;
        var trainset = AppState.Instance.CurrentTrainset;

        // write scenery/$<name>.scn with the replaced trainsets
        string dir = System.IO.Path.GetDirectoryName(scenery.Path) ?? "scenery";
        string exportName = "$" + System.IO.Path.GetFileName(scenery.Path);
        string exportPath = System.IO.Path.Combine(dir, exportName);
        try
        {
            File.WriteAllText(exportPath, scenery.BuildExportContent(), Encoding.GetEncoding(1250));
        }
        catch
        {
            return; // couldn't write the scenery file
        }

        // the player's vehicle is the node name of the driven car in the consist
        string? vehicle = trainset?.Vehicles
            .FirstOrDefault(v => v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver)?.Name
            ?? trainset?.Vehicles.FirstOrDefault()?.Name;

        // make sure the latest settings are on disk before the sim reads them
        StarterNG.Classes.Settings.Instance.CaptureAndSave();

        // launch the game: -s $<name>.scn -v <vehicle>
        try
        {
            Process.Start(new ProcessStartInfo(StarterNG.Classes.Settings.Instance.ExecutablePath)
            {
                Arguments = $"-s {exportName} -v {vehicle}",
                UseShellExecute = true
            });
        }
        catch
        {
            // executable not found / not configured yet
        }
    }
}