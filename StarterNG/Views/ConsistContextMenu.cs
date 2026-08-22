using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Classes;

namespace StarterNG.Views;

/// <summary>
/// Right-click menu shared by the consist lists (the "Vehicles" list in Scenarios
/// and the "Map consists" list in the depot). Offers: save to warehouse, load from
/// warehouse (each entry deletable via an "✕"), copy/paste via the clipboard, and -
/// when <c>removeAll</c> is supplied - clearing the consist (Shift+Delete).
/// </summary>
public static class ConsistContextMenu
{
    /// <param name="owner">Control the menu/flyouts anchor to (for clipboard + popups).</param>
    /// <param name="getTarget">Supplies the trainset to act on when the menu opens.</param>
    /// <param name="applyVehicles">Replaces the target's vehicles and refreshes the view.</param>
    /// <param name="removeAll">When non-null, adds a "remove all vehicles" entry.</param>
    public static ContextMenu Build(
        Control owner,
        Func<Trainset?> getTarget,
        Action<List<Dynamic>> applyVehicles,
        Action? removeAll = null)
    {
        var menu = new ContextMenu();
        // Rebuild the entries each time it opens so the warehouse list stays fresh.
        menu.Opening += (_, _) => Populate(menu, owner, getTarget, applyVehicles, removeAll);
        return menu;
    }

    private static void Populate(ContextMenu menu, Control owner,
        Func<Trainset?> getTarget, Action<List<Dynamic>> applyVehicles, Action? removeAll)
    {
        menu.Items.Clear();
        var target = getTarget();
        bool hasVehicles = target is { Vehicles.Count: > 0 };

        // ── Save current consist to the warehouse ──────────────────────────────
        var save = new MenuItem { Header = App.Loc["PresetSave"], IsEnabled = hasVehicles };
        save.Click += (_, _) => ShowSaveFlyout(owner, target);
        menu.Items.Add(save);

        // ── Load from the warehouse (submenu; each entry deletable) ────────────
        var load = new MenuItem { Header = App.Loc["PresetLoad"] };
        var presets = PresetStore.All();
        if (presets.Count == 0)
        {
            load.Items.Add(new MenuItem { Header = App.Loc["PresetEmpty"], IsEnabled = false });
        }
        else
        {
            foreach (var preset in presets)
                load.Items.Add(BuildPresetItem(menu, preset, applyVehicles));
        }
        menu.Items.Add(load);

        var sort = new MenuItem { Header = App.Loc["PresetSort"] };
        var sortName = new MenuItem
        {
            Header = (PresetStore.SortMode == PresetSort.Name ? "● " : "○ ") + App.Loc["PresetSortName"]
        };
        sortName.Click += (_, _) => PresetStore.SortMode = PresetSort.Name;
        var sortTrack = new MenuItem
        {
            Header = (PresetStore.SortMode == PresetSort.Track ? "● " : "○ ") + App.Loc["PresetSortTrack"]
        };
        sortTrack.Click += (_, _) => PresetStore.SortMode = PresetSort.Track;
        sort.Items.Add(sortName);
        sort.Items.Add(sortTrack);
        menu.Items.Add(sort);

        var import = new MenuItem { Header = App.Loc["PresetImportMagazyn"] };
        import.Click += (_, _) => PresetStore.ImportMagazyn();
        menu.Items.Add(import);

        menu.Items.Add(new Separator());

        // ── Clipboard (full trainset text) ─────────────────────────────────────
        var copy = new MenuItem { Header = App.Loc["ConsistCopy"], IsEnabled = hasVehicles };
        copy.Click += async (_, _) =>
        {
            if (target != null && TopLevel.GetTopLevel(owner)?.Clipboard is { } cb)
                await cb.SetTextAsync(ConsistText.Serialize(target));
        };
        menu.Items.Add(copy);

        var paste = new MenuItem { Header = App.Loc["ConsistPaste"] };
        paste.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } cb)
                return;
            string? text = await cb.TryGetTextAsync();
            var vehicles = ConsistText.VehiclesFrom(text);
            if (vehicles != null)
                applyVehicles(vehicles);
        };
        menu.Items.Add(paste);

        // ── Remove all vehicles (depot only) ───────────────────────────────────
        if (removeAll != null)
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem
            {
                Header = App.Loc["ConsistRemoveAll"],
                InputGesture = new KeyGesture(Key.Delete, KeyModifiers.Shift),
                IsEnabled = hasVehicles
            };
            clear.Click += (_, _) => removeAll();
            menu.Items.Add(clear);
        }
    }

    // A warehouse entry: the name loads the preset on click, the trailing "✕"
    // deletes it from the warehouse.
    private static MenuItem BuildPresetItem(ContextMenu menu, TrainsetPreset preset,
        Action<List<Dynamic>> applyVehicles)
    {
        var del = new Button
        {
            Content = "✕",
            Padding = new Thickness(6, 0),
            MinWidth = 0,
            Foreground = new SolidColorBrush(Color.Parse("#D05050"))
        };
        del.Classes.Add("Basic");
        ToolTip.SetTip(del, App.Loc["PresetDelete"]);
        del.Click += (_, e) =>
        {
            e.Handled = true;          // don't let the click also load the preset
            PresetStore.Delete(preset.Name);
            menu.Close();              // reopen to see the updated warehouse
        };

        var label = new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center };

        var dock = new DockPanel { MinWidth = 180 };
        DockPanel.SetDock(del, Dock.Right);
        dock.Children.Add(del);
        dock.Children.Add(label);

        var item = new MenuItem { Header = dock };
        item.Click += (_, _) =>
        {
            var vehicles = ConsistText.VehiclesFrom(preset.Entry);
            if (vehicles != null)
                applyVehicles(vehicles);
        };
        return item;
    }

    // Small popup to name the preset before saving it to the warehouse.
    private static void ShowSaveFlyout(Control owner, Trainset? target)
    {
        if (target is null || target.Vehicles.Count == 0)
            return;

        var nameBox = new TextBox
        {
            Watermark = App.Loc["PresetNamePrompt"],
            Text = DefaultName(target),
            MinWidth = 220
        };

        var ok = new Button { Content = App.Loc["PresetSave"], HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("Flat");
        ok.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 240 };
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["PresetNamePrompt"], FontWeight = FontWeight.Bold, FontSize = 12
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };

        void Commit()
        {
            PresetStore.Save(nameBox.Text, ConsistText.Serialize(target));
            flyout.Hide();
        }

        ok.Click += (_, _) => Commit();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Commit();
            }
        };

        flyout.ShowAt(owner, showAtPointer: true);
        nameBox.Focus();
        nameBox.SelectAll();
    }

    private static string DefaultName(Trainset trainset)
    {
        string joined = string.Join(" + ", trainset.Vehicles.Select(v => v.Name));
        return joined.Length > 40 ? joined[..40] : joined;
    }
}
