using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using StarterNG.Classes;

namespace StarterNG.Views;

public sealed class Warehouse
{
    private readonly ListBox _list;
    private readonly Button _removeButton;
    private readonly Cursor _hand;
    private readonly Func<Trainset?> _target;
    private readonly Action<Trainset, List<Dynamic>> _apply;

    public Warehouse(ListBox list, Button removeButton, Cursor hand,
                         Func<Trainset?> target, Action<Trainset, List<Dynamic>> apply)
    {
        _list = list;
        _removeButton = removeButton;
        _hand = hand;
        _target = target;
        _apply = apply;
    }

    public void Refresh()
    {
        string? current = SelectedName();
        _list.Items.Clear();

        foreach (var preset in PresetStore.All())
        {
            var entry = new ListBoxItem
            {
                Tag = preset.Name,
                Content = new TextBlock
                {
                    Text = preset.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            _list.Items.Add(entry);
            if (current is not null && string.Equals(current, preset.Name, StringComparison.Ordinal))
                _list.SelectedItem = entry;
        }

        _list.ContextMenu ??= BuildMenu();
        _removeButton.IsEnabled = _list.Items.Count > 0;
    }

    private string? SelectedName() => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    private static TrainsetPreset? PresetNamed(string name) =>
        PresetStore.All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            bool hasPreset = SelectedName() != null;
            bool hasTarget = _target() != null;

            void Add(string key, Action run, bool enabled = true)
            {
                var mi = new MenuItem { Header = App.Loc[key], IsEnabled = enabled };
                mi.Click += (_, _) => run();
                menu.Items.Add(mi);
            }

            Add("WarehouseApply", ApplySelected, hasPreset && hasTarget);
            Add("WarehouseAppend", AppendSelected, hasPreset && hasTarget);
            Add("ChangeName", RenameSelected, hasPreset);

            menu.Items.Add(new Separator());
            Add("PresetImportMagazyn", () => _ = ImportMagazynAsync());
            Add("WarehouseImportFile", () => _ = ImportFileAsync());

            menu.Items.Add(new Separator());
            var sort = new MenuItem { Header = App.Loc["PresetSort"] };
            sort.Items.Add(SortItem(PresetSort.Name, "PresetSortName"));
            sort.Items.Add(SortItem(PresetSort.Track, "PresetSortTrack"));
            menu.Items.Add(sort);
        };
        return menu;
    }

    private MenuItem SortItem(PresetSort mode, string locKey)
    {
        var item = new MenuItem
        {
            Header = (PresetStore.SortMode == mode ? "● " : "○ ") + App.Loc[locKey]
        };
        item.Click += (_, _) => { PresetStore.SortMode = mode; Refresh(); };
        return item;
    }

    public async Task SaveCurrentAsync()
    {
        if (_target() is not { } trainset)
            return;

        string name = trainset.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = App.Loc["PresetEmpty"];

        try
        {
            PresetStore.Save(name, ConsistText.Serialize(trainset));
        }
        catch (Exception ex)
        {
            await Infrastructure.Diagnostics.ReportAsync(PresetStore.FilePath, ex);
            return;
        }
        Refresh();
    }

    public void ApplySelected()
    {
        if (SelectedName() is not { } name || _target() is not { } target)
            return;

        if (PresetNamed(name) is not { } preset)
            return;

        if (ConsistText.VehiclesFrom(preset.Entry) is { } vehicles)
            _apply(target, vehicles);
    }

    public void AppendSelected()
    {
        if (SelectedName() is not { } name || _target() is not { } target)
            return;

        var vehicles = ConsistText.VehiclesFrom(PresetNamed(name)?.Entry);
        if (vehicles is null || vehicles.Count == 0)
            return;

        var merged = new List<Dynamic>(target.Vehicles);
        merged.AddRange(vehicles);
        _apply(target, merged);
    }

    public void RenameSelected()
    {
        if (SelectedName() is not { } name || PresetNamed(name) is not { } preset)
            return;

        var nameBox = new TextBox { Text = name, MinWidth = 240 };
        var ok = new Button { Content = App.Loc["Ok"], Cursor = _hand };
        ok.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = App.Loc["PresetNamePrompt"],
            FontWeight = FontWeight.Bold,
            FontSize = 12
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            string? fresh = nameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(fresh) && !string.Equals(fresh, name, StringComparison.Ordinal))
            {
                PresetStore.Save(fresh, preset.Entry);
                PresetStore.Delete(name);
                Refresh();
            }
            flyout.Hide();
        };
        flyout.ShowAt(_list);
        nameBox.Focus();
    }

    public async Task ImportFileAsync()
    {
        if (TopLevel.GetTopLevel(_list) is not { } top)
            return;

        var picked = await top.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = App.Loc["WarehouseImportFile"],
                AllowMultiple = false
            });

        string? path = picked.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrEmpty(path))
            return;

        await ReportImport(PresetStore.ImportMagazyn(path));
    }

    public async Task ImportMagazynAsync() => await ReportImport(PresetStore.ImportMagazyn());

    private async Task ReportImport(int added)
    {
        Refresh();
        if (added == 0)
            await Infrastructure.Diagnostics.ReportAsync(App.Loc["WarehouseImportNothing"]);
    }

    public async Task RemoveSelectedAsync()
    {
        if (SelectedName() is not { } name)
            return;

        try
        {
            PresetStore.Delete(name);
        }
        catch (Exception ex)
        {
            await Infrastructure.Diagnostics.ReportAsync(PresetStore.FilePath, ex);
            return;
        }
        Refresh();
    }
}
