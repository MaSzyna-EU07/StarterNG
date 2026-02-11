using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
        sceneries = new List<Scenery>();
        
        // load sceneries
        List<string> scnFiles = Directory.GetFiles("scenery/", "*.scn").ToList();
        foreach (string scnFile in scnFiles)
        {
            // skip temp sceneries
            if (Path.GetFileName(scnFile).StartsWith("$"))
                continue;
            
            // Parse all files
            sceneries.Add(new Scenery(scnFile));
        }
        
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
        consistStack.Children.Clear();
        
        // get selected scenery
        var tItem = sceneryList.SelectedItem as TreeViewItem;
        if (tItem?.Tag is not int tTag)
            return;
        Scenery selectedScn = sceneries[tTag];
        
        // get selected trainset
        ListBoxItem vItem = vehicleList.SelectedItem as ListBoxItem;
        if (vItem?.Tag is not int vTag)
            return;
        Trainset selectedTrainset = selectedScn.Trainsets[vTag];
        foreach (var train in selectedTrainset.Vehicles)
        {
            string path = Path.Combine(
                "textures",
                "mini",
                train.SkinFile + ".bmp"
            );

            if (!File.Exists(path))
            {
                // fallback
                continue;
            }            
            
            var bitmap = new Bitmap(path);
            var image = new Image
            {
                Source = bitmap,
                Height = 45,
                Stretch = Avalonia.Media.Stretch.Uniform,
                Margin = new Thickness(0)
            };
            consistStack.Children.Add(image);
        }
        missionDescription.Text = selectedTrainset.Description;
    }
}