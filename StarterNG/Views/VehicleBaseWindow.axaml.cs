using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StarterNG.Application;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Views;

/// <summary>
/// Every livery of the installation, to look through by what its credit line says.
/// Whatever is typed filters straight away; a column's head sorts by it.
/// </summary>
public partial class VehicleBaseWindow : Window
{
    private const int PreviewHeight = 40;

    private sealed record Column(TextBlock Head, string LocKey, Func<VehicleBaseRow, string> Key);

    private readonly VehicleCatalog _db = AppServices.Current.Library.Vehicles;
    private readonly MiniTextures _minis = new();
    private readonly List<VehicleBaseRow> _all = new();
    private readonly TextBox[] _filters;
    private readonly Column[] _columns;
    private Column _sortBy;
    private bool _descending;
    private bool _clearing;

    public VehicleTexture? Picked { get; private set; }

    /// <summary>The vehicle picked goes behind the selected one instead of taking its place.</summary>
    public bool PickedToAdd { get; private set; }

    public VehicleBaseWindow()
    {
        InitializeComponent();

        foreach (var texture in _db.Textures)
            if (!_db.IsSetFollower(texture))
                _all.Add(new VehicleBaseRow(texture));

        revFromLabel.Text = App.Loc["RevisionFrom"].TrimEnd(':', ' ');
        revToLabel.Text = App.Loc["RevisionTo"].TrimEnd(':', ' ');

        _columns = new[]
        {
            new Column(headSkin, "TextureInfo", r => r.Skin),
            new Column(headClass, "Class", r => r.Class),
            new Column(headOperator, "TexOperator", r => r.Operator),
            new Column(headRevision, "TexRevision", r => r.DateKey),
            new Column(headAuthor, "TexAuthor", r => r.Author)
        };
        _sortBy = _columns[1];
        ShowSort();

        _filters = new[] { fName, fClass, fOperator, fAuthor, fModel, fStation, fPhoto, fRevFrom, fRevTo };
        foreach (var box in _filters)
            box.TextChanged += (_, _) => Refresh();

        hideArchivalCheck.IsChecked = AppServices.Current.Settings.HideArchivalVehicles;
        hideArchivalCheck.IsCheckedChanged += HideArchivalCheck_OnChanged;

        FillCategories();
        categoryList.SelectionChanged += (_, _) => { FillClasses(); Refresh(); };
        classList.SelectionChanged += (_, _) => Refresh();

        vehicleList.ContextRequested += VehicleList_OnContextRequested;
        vehicleList.AddHandler(PointerReleasedEvent, VehicleList_OnPointerReleased, handledEventsToo: true);

        ShowSelected();
        Refresh();
        Opened += (_, _) => fName.Focus();
    }

    private void FillCategories()
    {
        categoryList.Items.Add(new ListBoxItem { Content = App.Loc["CatAll"], Tag = null });
        foreach (var (locKey, match) in VehicleBrowser.CategoryDefs)
            if (_all.Any(r => match(r.Category)))
                categoryList.Items.Add(new ListBoxItem { Content = App.Loc[locKey], Tag = match });

        categoryList.SelectedIndex = 0;
        FillClasses();
    }

    private bool Offered(VehicleBaseRow row) =>
        hideArchivalCheck.IsChecked != true || !row.Texture.ResolvedArchived;

    private bool InCategory(VehicleBaseRow row) =>
        (categoryList.SelectedItem as ListBoxItem)?.Tag is not Func<string?, bool> match || match(row.Category);

    /// <summary>The classes of the category, keeping the one picked if it is still among them.</summary>
    private void FillClasses()
    {
        string? keep = (classList.SelectedItem as VehicleClassRow)?.Name;

        var rows = VehicleBaseQuery.ClassCounts(_all.Where(r => Offered(r) && InCategory(r)));

        classList.ItemsSource = rows;
        classList.SelectedItem = rows.FirstOrDefault(c => string.Equals(c.Name, keep, StringComparison.OrdinalIgnoreCase));
    }

    private void Refresh()
    {
        if (_clearing)
            return;

        var list = VehicleBaseQuery.Select(
            _all.Where(r => Offered(r) && InCategory(r)),
            new VehicleBaseFilters
            {
                Name = fName.Text,
                Class = fClass.Text,
                Operator = fOperator.Text,
                Author = fAuthor.Text,
                Model = fModel.Text,
                Station = fStation.Text,
                Photo = fPhoto.Text,
                RevisionFrom = fRevFrom.Text,
                RevisionTo = fRevTo.Text,
                PickedClass = (classList.SelectedItem as VehicleClassRow)?.Name
            },
            _sortBy.Key, _descending);

        var keep = vehicleList.SelectedItem;
        vehicleList.ItemsSource = list;
        if (keep is VehicleBaseRow row && list.Contains(row))
            vehicleList.SelectedItem = row;

        emptyText.IsVisible = list.Count == 0;
        statusText.Text = string.Format(App.Loc["TextureBaseCount"], list.Count, _all.Count(Offered));
    }

    private void Head_OnTapped(object? sender, TappedEventArgs e)
    {
        var column = _columns.First(c => ReferenceEquals(c.Head, sender));
        _descending = ReferenceEquals(column, _sortBy) && !_descending;
        _sortBy = column;
        ShowSort();
        Refresh();
    }

    private void ShowSort()
    {
        foreach (var column in _columns)
            column.Head.Text = App.Loc[column.LocKey] +
                               (!ReferenceEquals(column, _sortBy) ? "" : _descending ? " ▼" : " ▲");
    }

    private void ClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _clearing = true;
        foreach (var box in _filters)
            box.Text = "";
        categoryList.SelectedIndex = 0;
        classList.SelectedItem = null;
        _clearing = false;
        Refresh();
    }

    private void HideArchivalCheck_OnChanged(object? sender, RoutedEventArgs e)
    {
        AppServices.Current.Settings.HideArchivalVehicles = hideArchivalCheck.IsChecked ?? true;
        AppServices.Current.SettingsStore.Save();
        FillClasses();
        Refresh();
    }

    private void VehicleList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowSelected();

    private void ShowSelected()
    {
        var row = vehicleList.SelectedItem as VehicleBaseRow;
        replaceButton.IsEnabled = row is not null;
        addButton.IsEnabled = row is not null;

        if (row is null)
        {
            preview.Source = null;
            detailTitle.Text = "";
            detailText.Text = App.Loc["TextureBasePick"];
            ToolTip.SetTip(detailText, null);
            return;
        }

        preview.Source = _minis.Get(_db.ResolveMiniName(row.Texture), PreviewHeight);
        detailTitle.Text = string.Join(" · ", new[] { row.Skin, row.Class, row.Operator }.Where(s => s.Length > 0));

        var lines = new[]
            {
                (App.Loc["Model"], row.Model), (App.Loc["TexStation"], row.Station),
                (App.Loc["TexRevision"], row.Revision), (App.Loc["TexPhoto"], row.Photo)
            }
            .Where(p => p.Item2.Length > 0)
            .Select(p => $"{p.Item1}: {p.Item2}")
            .ToList();
        detailText.Text = string.Join("  ·  ", lines);
        ToolTip.SetTip(detailText, string.Join("\n", lines));
    }

    private void Take(VehicleBaseRow row, bool add)
    {
        Picked = row.Texture;
        PickedToAdd = add;
    }

    /// <returns>Whether there was one selected to take.</returns>
    private bool TakeSelected(bool add)
    {
        if (vehicleList.SelectedItem is not VehicleBaseRow row) return false;
        Take(row, add);
        Close();
        return true;
    }

    private void ReplaceButton_OnClick(object? sender, RoutedEventArgs e) => TakeSelected(add: false);

    private void AddButton_OnClick(object? sender, RoutedEventArgs e) => TakeSelected(add: true);

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void VehicleList_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TakeSelected(add: false))
            e.Handled = true;
    }

    private void VehicleList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (vehicleList.SelectedItem is VehicleBaseRow row)
            Take(row, add: false);
    }

    // DoubleTapped is raised from the second press, while the list still holds the
    // pointer. Closing there leaves the capture on a dead window and on Windows the
    // owner stops taking clicks. The release ends the gesture and drops the capture,
    // so that is where the window goes.
    private void VehicleList_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Picked != null)
            Close();
    }

    private void VehicleList_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (vehicleList.SelectedItem is not VehicleBaseRow row) return;

        var menu = new ContextMenu();

        void Pick(string locKey, bool add) => menu.AddItem(App.Loc[locKey], () =>
        {
            Take(row, add);
            Dispatcher.UIThread.Post(Close);
        });

        Pick("ReplaceVehicle", add: false);
        Pick("AddVehicle", add: true);
        menu.Items.Add(new Separator());

        TextureMenuItems.Add(menu, vehicleList, row.Texture);
        menu.Open(vehicleList);
        e.Handled = true;
    }
}
