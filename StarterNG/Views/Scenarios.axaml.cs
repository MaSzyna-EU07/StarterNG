using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
}