using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StarterNG.Application;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Views;

/// <summary>
/// The developer tab: what the starter read out of the installation, and the state
/// of the JSON copy it keeps of it. Read-only apart from rebuilding that copy.
/// </summary>
public partial class Developer : UserControl
{
    public Developer()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Fills the panel from the catalogue as it stands right now.</summary>
    public void Refresh()
    {
        var catalog = AppServices.Current.Library.Vehicles;
        var status = AppServices.Current.VehicleCache.Status;

        VehiclesValue.Text = Count(catalog.TextureByUuid.Values
                                          .Select(texture => texture.Directory)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .Count());
        LiveriesValue.Text = Wrecks(catalog);
        GroupsValue.Text = Count(catalog.GroupsById.Count);
        SetsValue.Text = Count(catalog.Sets.Count);
        FilesValue.Text = Count(status.Sources);
        SourceValue.Text = App.Loc[status.ServedFromCopy ? "DevSourceCopy" : "DevSourceTextures"];
        WrittenValue.Text = status.Written is { } written
            ? written.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";
        LocationValue.Text = status.Directory;
        RebuildStatus.Text = "";
    }

    /// <summary>
    /// Rebuilds the copy on disk without disturbing the catalogue the other tabs
    /// are already drawn from - the vehicles on screen keep pointing at the entries
    /// they were built with.
    /// </summary>
    private void RebuildButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RebuildButton.IsEnabled = false;
        try
        {
            int liveries = AppServices.Current.VehicleCache
                                      .Rebuild(new VehicleCatalog(AppServices.Current.MiniTextures));
            Refresh();
            RebuildStatus.Text = $"{App.Loc["DevRebuilt"]} ({liveries})";
        }
        finally
        {
            RebuildButton.IsEnabled = true;
        }
    }

    /// <summary>Liveries offered, saying so when wrecks are being held back.</summary>
    private static string Wrecks(VehicleCatalog catalog)
    {
        int wrecks = catalog.TextureByUuid.Count - catalog.Textures.Count;
        return wrecks > 0
            ? $"{Count(catalog.Textures.Count)}  (+{Count(wrecks)} {App.Loc["DevWrecks"]})"
            : Count(catalog.Textures.Count);
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
