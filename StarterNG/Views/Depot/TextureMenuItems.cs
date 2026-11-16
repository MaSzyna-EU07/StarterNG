using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using StarterNG.Application;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;
using StarterNG.Infrastructure.Vehicles;

namespace StarterNG.Views;

/// <summary>What the lists of liveries offer about a livery itself.</summary>
internal static class TextureMenuItems
{
    /// <param name="owner">A control of the window, for its clipboard.</param>
    public static void Add(ContextMenu menu, Control owner, VehicleTexture texture)
    {
        menu.AddItem(App.Loc["CopyTextureName"], async () =>
        {
            if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(Consist.Base(texture.Skinfile));
        });
        menu.AddItem(App.Loc["OpenTextureFolder"], () => Infrastructure.Diagnostics.OpenOrReport(
            Path.GetFullPath(TextureFolder.Of(AppServices.Current.Files, AppServices.Current.Paths, texture))));
    }
}
