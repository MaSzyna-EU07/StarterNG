using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using StarterNG.Application;
using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Views;

public static class ConsistContextMenu
{
    /// <summary>A scenery consist's own file: written for good, or read back.</summary>
    public sealed record SceneryFileActions(Func<bool> IsModified, Action Save, Action Revert);

    public static ContextMenu Build(
        Control owner,
        Func<Trainset?> getTarget,
        Action<List<Dynamic>> applyVehicles,
        Action? removeAll = null,
        Func<Dynamic?>? getVehicle = null,
        Action<Dynamic>? removeVehicle = null,
        Action? randomOrder = null,
        Action? randomTurn = null,
        Action? randomTextures = null,
        SceneryFileActions? sceneryFile = null,
        KeyGesture? removeAllGesture = null)
    {
        var menu = new ContextMenu();

        menu.Opening += (_, _) =>
        {
            Populate(menu, owner, getTarget, applyVehicles, removeAll, removeAllGesture);

            // Changes not in the scenery file yet come first: the reason to open the menu.
            if (sceneryFile != null)
            {
                bool modified = sceneryFile.IsModified();
                menu.Items.Insert(0, MenuItems.Item(App.Loc["SaveToScenery"], sceneryFile.Save, modified,
                                                    new KeyGesture(Key.S, KeyModifiers.Control)));
                menu.Items.Insert(1, MenuItems.Item(App.Loc["RevertFromScenery"], sceneryFile.Revert, modified));
                menu.Items.Insert(2, new Separator());
            }

            // Opened on a vehicle, the menu starts with what concerns that vehicle.
            if (removeVehicle != null && getVehicle?.Invoke() is { } v)
            {
                menu.Items.Insert(0, MenuItems.Item(App.Loc["RemoveVehicle"], () => removeVehicle(v)));
                menu.Items.Insert(1, new Separator());
            }

            // Straight in the menu rather than under a submenu named like the list.
            bool hasVehicles = getTarget() is { Vehicles.Count: > 0 };
            if (randomOrder != null || randomTurn != null || randomTextures != null)
                menu.Items.Add(new Separator());
            AddAction(menu, "RandomizeOrder", randomOrder, hasVehicles);
            AddAction(menu, "RandomizeOrientation", randomTurn, hasVehicles);
            AddAction(menu, "RandomizeTextures", randomTextures, hasVehicles, ellipsis: true);
        };
        return menu;
    }

    private static void AddAction(ContextMenu menu, string key, Action? run, bool enabled, bool ellipsis = false)
    {
        if (run is not null)
            menu.AddItem(App.Loc[key] + (ellipsis ? "…" : ""), run, enabled);
    }

    private static void Populate(ContextMenu menu, Control owner,
        Func<Trainset?> getTarget, Action<List<Dynamic>> applyVehicles, Action? removeAll,
        KeyGesture? removeAllGesture)
    {
        menu.Items.Clear();
        var target = getTarget();
        bool hasVehicles = target is { Vehicles.Count: > 0 };

        menu.AddItem(App.Loc["AddToWarehouseNamed"], () => ShowSaveFlyout(owner, target), hasVehicles);

        var load = new MenuItem { Header = App.Loc["PresetLoad"] };
        var presets = PresetStore.All();
        if (presets.Count == 0)
        {
            load.Items.Add(new MenuItem { Header = App.Loc["PresetEmpty"], IsEnabled = false });
        }
        else
        {
            foreach (var preset in presets)
                load.Items.Add(BuildPresetItem(preset, target, applyVehicles));
        }
        menu.Items.Add(load);

        var append = new MenuItem { Header = App.Loc["WarehouseAppend"] };
        if (presets.Count == 0)
        {
            append.Items.Add(new MenuItem { Header = App.Loc["PresetEmpty"], IsEnabled = false });
        }
        else
        {
            foreach (var preset in presets)
            {
                var entry = preset;
                append.AddItem(entry.Name, () =>
                {
                    var added = ConsistText.VehiclesFrom(entry.Entry);
                    if (added is null || added.Count == 0) return;
                    applyVehicles(target is null ? added : Consist.Joined(target.Vehicles, added));
                });
            }
        }
        menu.Items.Add(append);

        // Sorting and importing the warehouse are the warehouse list's business.
        menu.Items.Add(new Separator());

        menu.AddItem(App.Loc["ConsistCopy"], async () =>
        {
            if (target != null && TopLevel.GetTopLevel(owner)?.Clipboard is { } cb)
                await cb.SetTextAsync(ConsistText.Serialize(target));
        }, hasVehicles);

        menu.AddItem(App.Loc["ConsistPaste"], async () =>
        {
            if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } cb)
                return;
            string? text = await cb.TryGetTextAsync();
            var vehicles = ConsistText.VehiclesFrom(text);
            if (vehicles != null)
                applyVehicles(KeepCrew(target, vehicles));
        });

        if (removeAll != null)
        {
            menu.Items.Add(new Separator());
            menu.AddItem(App.Loc["ConsistRemoveAll"], removeAll, hasVehicles, removeAllGesture);
        }
    }

    /// <summary>Whether a consist may go into the warehouse under name, written over one it has - asked only then.</summary>
    public static async Task<bool> MayOverwritePresetAsync(Control owner, string? name)
    {
        string wanted = name?.Trim() ?? string.Empty;
        if (!PresetStore.All().Any(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)) ||
            TopLevel.GetTopLevel(owner) is not Window window)
            return true;

        return await MessageBox.Show(window, string.Format(App.Loc["WarehouseOverwriteConfirm"], wanted),
                                     App.Loc["AddToWarehouseNamed"], MessageBoxButtons.YesNo);
    }

    /// <summary>Whether a warehouse consist may go for good, which this asks first.</summary>
    public static async Task<bool> MayRemovePresetAsync(Control owner, string name) =>
        TopLevel.GetTopLevel(owner) is not Window window ||
        await MessageBox.Show(window, string.Format(App.Loc["WarehouseRemoveConfirm"], name),
                              App.Loc["RemoveFromWarehouse"], MessageBoxButtons.YesNo);

    /// <summary>Vehicles about to replace those of target, with the kind of crew target has.</summary>
    public static List<Dynamic> KeepCrew(Trainset? target, List<Dynamic> vehicles)
    {
        var library = AppServices.Current.Library;
        return target is null
            ? vehicles
            : Consist.LoadedOver(target.Vehicles, vehicles, library.Vehicles,
                                 new VehicleInfo(library.Vehicles, AppServices.Current.Physics));
    }

    private static MenuItem BuildPresetItem(TrainsetPreset preset, Trainset? target, Action<List<Dynamic>> applyVehicles)
    {
        return MenuItems.Item(preset.Name, () =>
        {
            var vehicles = ConsistText.VehiclesFrom(preset.Entry);
            if (vehicles != null)
                applyVehicles(KeepCrew(target, vehicles));
        });
    }

    /// <summary>A word next to the control, gone on its own after a moment.</summary>
    private static void ShowNote(Control owner, string text, bool atPointer)
    {
        var note = new Flyout { Content = new TextBlock { Text = text, Margin = new Thickness(8, 4) } };
        note.ShowAt(owner, showAtPointer: atPointer);
        DispatcherTimer.RunOnce(note.Hide, TimeSpan.FromSeconds(2.5));
    }

    /// <summary>Asks for a name and puts the consist into the warehouse; <paramref name="saved"/> gets the name it went in under.</summary>
    public static void ShowSaveFlyout(Control owner, Trainset? target, bool atPointer = true, Action<string>? saved = null)
    {
        if (target is null || target.Vehicles.Count == 0)
        {
            ShowNote(owner, App.Loc["WarehouseNothingToAdd"], atPointer);
            return;
        }

        var nameBox = new TextBox
        {
            PlaceholderText = App.Loc["PresetNamePrompt"],
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

        async void Commit()
        {
            string name = nameBox.Text?.Trim() ?? "";
            if (name.Length == 0)
                return;

            flyout.Hide();
            if (!await MayOverwritePresetAsync(owner, name))
                return;

            try
            {
                PresetStore.Save(name, ConsistText.Serialize(target));
            }
            catch (Exception ex)
            {
                await Infrastructure.Diagnostics.ReportAsync(PresetStore.FilePath, ex);
                return;
            }
            saved?.Invoke(name);
            ShowNote(owner, string.Format(App.Loc["WarehouseSaved"], name), atPointer);
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

        flyout.ShowAt(owner, showAtPointer: atPointer);
        nameBox.Focus();
        nameBox.SelectAll();
    }

    private static string DefaultName(Trainset trainset)
    {
        if (!string.IsNullOrWhiteSpace(trainset.Name))
            return trainset.Name;

        string joined = string.Join(" + ", trainset.Vehicles.Select(v => v.Name));
        return joined.Length > 40 ? joined[..40] : joined;
    }
}
