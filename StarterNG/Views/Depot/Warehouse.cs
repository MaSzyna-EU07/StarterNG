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

/// <summary>
/// The warehouse tab: consists kept for later. The one selected is the one the
/// consist strip edits, and the menu puts it into the scenery's selected consist.
/// </summary>
public sealed class Warehouse
{
    private readonly ListBox _list;
    private readonly Button _removeButton;
    private readonly Cursor _hand;
    private readonly Func<Trainset?> _target;
    private readonly Action<Trainset, List<Dynamic>> _apply;

    /// <summary>Set while the list is rebuilt, which clears and restores the selection.</summary>
    private bool _refreshing;

    /// <summary>Another consist is selected, or none - not raised mid-rebuild.</summary>
    public event Action? SelectionChanged;

    /// <summary>A consist got a new name: the old one, then the new one.</summary>
    public event Action<string, string>? Renamed;

    /// <param name="target">The scenery consist the menu's actions put a warehouse consist into.</param>
    public Warehouse(ListBox list, Button removeButton, Cursor hand,
                         Func<Trainset?> target, Action<Trainset, List<Dynamic>> apply)
    {
        _list = list;
        _removeButton = removeButton;
        _hand = hand;
        _target = target;
        _apply = apply;

        _list.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
                SelectionChanged?.Invoke();
        };
    }

    /// <param name="select">The consist to select; the one selected now otherwise.</param>
    public void Refresh(string? select = null)
    {
        string? current = select ?? SelectedName();
        string? before = SelectedName();
        _refreshing = true;
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
        _refreshing = false;

        if (!string.Equals(before, SelectedName(), StringComparison.Ordinal))
            SelectionChanged?.Invoke();
    }

    private string? SelectedName() => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    public TrainsetPreset? SelectedPreset() => SelectedName() is { } name ? PresetNamed(name) : null;

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

            menu.AddItem(App.Loc["WarehouseUseInScenery"], ApplySelected, hasPreset && hasTarget);
            menu.AddItem(App.Loc["WarehouseAppendToScenery"], AppendSelected, hasPreset && hasTarget);
            menu.AddItem(App.Loc["ChangeName"], RenameSelected, hasPreset);
            menu.AddItem(App.Loc["RemoveFromWarehouse"], () => _ = RemoveSelectedAsync(), hasPreset);

            menu.Items.Add(new Separator());
            menu.AddItem(App.Loc["PresetImportMagazyn"], () => _ = ImportMagazynAsync());
            menu.AddItem(App.Loc["WarehouseImportFile"], () => _ = ImportFileAsync());

            menu.Items.Add(new Separator());
            var sort = new MenuItem { Header = App.Loc["PresetSort"] };
            sort.Items.Add(SortItem(PresetSort.Name, "PresetSortName"));
            sort.Items.Add(SortItem(PresetSort.Track, "PresetSortTrack"));
            menu.Items.Add(sort);
        };
        return menu;
    }

    private MenuItem SortItem(PresetSort mode, string locKey) =>
        MenuItems.Item((PresetStore.SortMode == mode ? "● " : "○ ") + App.Loc[locKey],
                       () => { PresetStore.SortMode = mode; Refresh(); });

    /// <summary>Asks for a name at the button pressed, and says where the consist went.</summary>
    public void SaveCurrent(Control anchor) =>
        ConsistContextMenu.ShowSaveFlyout(anchor, _target(), atPointer: false, saved: name => Refresh(name));

    public void ApplySelected()
    {
        if (_target() is not { } target || SelectedPreset() is not { } preset)
            return;

        if (ConsistText.VehiclesFrom(preset.Entry) is { } vehicles)
            _apply(target, ConsistContextMenu.KeepCrew(target, vehicles));
    }

    public void AppendSelected()
    {
        if (_target() is not { } target)
            return;

        var vehicles = ConsistText.VehiclesFrom(SelectedPreset()?.Entry);
        if (vehicles is null || vehicles.Count == 0)
            return;

        _apply(target, Domain.Consist.Joined(target.Vehicles, vehicles));
    }

    public void RenameSelected()
    {
        if (SelectedPreset() is not { } preset)
            return;

        string name = preset.Name;
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
        ok.Click += async (_, _) =>
        {
            string? fresh = nameBox.Text?.Trim();
            flyout.Hide();
            if (string.IsNullOrEmpty(fresh) || string.Equals(fresh, name, StringComparison.Ordinal))
                return;

            // Differing only in case is this very entry, not another one written over.
            if (!string.Equals(fresh, name, StringComparison.OrdinalIgnoreCase) &&
                !await ConsistContextMenu.MayOverwritePresetAsync(_list, fresh))
                return;

            // Deleted first: a new name differing only in case is the same entry.
            PresetStore.Delete(name);
            PresetStore.Save(fresh, preset.Entry);
            Renamed?.Invoke(name, fresh);
            Refresh(fresh);
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
        if (SelectedName() is not { } name || !await ConsistContextMenu.MayRemovePresetAsync(_list, name))
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
