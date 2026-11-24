using StarterNG.Application;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

/// <summary>The settings page on a start with no eu07.ini of the player's own yet.</summary>
[Collection(AvaloniaControls.Name)]
public class SettingsFirstStartTests
{
    [Fact]
    public void A_first_start_has_nothing_to_save_before_the_simulator_starts()
    {
        var files = new InMemoryFileSystem();
        files.WriteAllText(TestInstallation.At("eu07.ini"), "width 1920\nheight 1080\nendconfig\n");
        var installation = new TestInstallation(files);
        installation.Environment.AsWindows();
        var services = AppServices.Initialize(installation.Paths, installation.Files, installation.Clock,
                                              installation.Environment);
        services.SettingsStore.Load();
        services.SettingsStore.StartAtHighWithoutOwnSettings();

        HeadlessDrawing.Ensure();
        // The page reads the graphics cards away from the window and comes back to it, as in the application.
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Avalonia.Threading.AvaloniaSynchronizationContext());
        Avalonia.Controls.Window? window = null;
        try
        {
            var page = new StarterNG.Views.Settings();
            window = Controls.Show(page, 1600, 1000, themed: true);
            Assert.False(page.HasUnsavedChanges);
        }
        finally
        {
            window?.Close();
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
}
