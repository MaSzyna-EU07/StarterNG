using Avalonia.Controls;
using StarterNG.Application;
using StarterNG.Controls;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

/// <summary>
/// Values of eu07.ini the starter does not know, or a control cannot show, as the settings page
/// shows them: as the file writes them, with a note, until the player sets them by hand - or the
/// move to the standard mode puts them right.
/// </summary>
[Collection(AvaloniaControls.Name)]
public class SettingsUnknownValueTests
{
    // The standard mode already, unless a test moves to it.
    private const string Standard = "starter.settings.standard yes\n";

    private static void WithPage(string ini, Action<StarterNG.Views.Settings, AppServices, Func<string>> act)
    {
        var installation = new TestInstallation(new InMemoryFileSystem());
        installation.Environment.AsWindows();
        var services = AppServices.Initialize(installation.Paths, installation.Files, installation.Clock,
                                              installation.Environment);
        string path = services.SettingsPaths.UserConfigPath();
        installation.Files.WriteAllText(path, ini);
        services.SettingsStore.Load();

        HeadlessDrawing.Ensure();
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Avalonia.Threading.AvaloniaSynchronizationContext());
        Window? window = null;
        try
        {
            var page = new StarterNG.Views.Settings();
            window = Controls.Show(page, 1600, 1000, themed: true);
            act(page, services, () => installation.Files.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally
        {
            window?.Close();
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static ComboBox List(StarterNG.Views.Settings page, string name) => page.FindControl<ComboBox>(name)!;

    private static string? Shown(ComboBox list) => (list.SelectedItem as ComboBoxItem)?.Content as string;

    private static string? Note(StarterNG.Views.Settings page, string key) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(page).OfType<SettingRow>()
            .First(row => row.ConfigKey == key).UnknownValue;

    [Fact]
    public void A_renderer_word_is_on_the_list_chosen_and_saved_as_it_is()
    {
        WithPage(Standard + "gfxrenderer vulkan\nfieldofview 45\n", (page, _, saved) =>
        {
            Assert.Equal("vulkan", Shown(List(page, "RenderEngineCb")));
            Assert.Contains("vulkan", Note(page, "gfxrenderer"));
            Assert.False(page.HasUnsavedChanges);

            page.Save();

            Assert.Contains("gfxrenderer vulkan\n", saved());
        });
    }

    [Fact]
    public void Another_renderer_picked_from_the_list_replaces_it()
    {
        WithPage(Standard + "gfxrenderer vulkan\n", (page, services, saved) =>
        {
            List(page, "RenderEngineCb").SelectedIndex = 0;

            page.Save();

            Assert.Contains("gfxrenderer default\n", saved());
            Assert.Null(services.Settings.RendererWord);
            Assert.Null(Note(page, "gfxrenderer"));
        });
    }

    [Fact]
    public void A_value_a_list_does_not_offer_is_listed_as_written_and_kept()
    {
        WithPage(Standard + "starter.batterydefault 7\ngfx.shadow.rank.cutoff 9\n", (page, _, saved) =>
        {
            Assert.Equal("7", Shown(List(page, "ReadyOverrideCb")));
            Assert.Equal("9", Shown(List(page, "ShadowDisplayCb")));
            Assert.Contains("7", Note(page, "starter.batterydefault"));
            Assert.Contains("9", Note(page, "gfx.shadow.rank.cutoff"));
            Assert.False(page.HasUnsavedChanges);

            page.Save();

            Assert.Contains("starter.batterydefault 7\n", saved());
            Assert.Contains("gfx.shadow.rank.cutoff 9\n", saved());
        });
    }

    [Fact]
    public void Picking_the_value_the_starter_shows_by_default_replaces_the_files_own()
    {
        // "7" is read as the scenery's choice, the first on the list: picked by hand, it is written.
        WithPage(Standard + "starter.batterydefault 7\n", (page, _, saved) =>
        {
            List(page, "ReadyOverrideCb").SelectedIndex = 0;
            Assert.Null(Note(page, "starter.batterydefault"));

            page.Save();

            Assert.Contains("starter.batterydefault 0\n", saved());
        });
    }

    [Fact]
    public void A_list_put_back_at_the_files_value_keeps_it()
    {
        WithPage(Standard + "starter.batterydefault 7\n", (page, _, saved) =>
        {
            var list = List(page, "ReadyOverrideCb");
            var fileValue = list.SelectedItem;
            list.SelectedIndex = 1;
            list.SelectedItem = fileValue;

            page.Save();

            Assert.Contains("starter.batterydefault 7\n", saved());
        });
    }

    [Fact]
    public void A_control_set_by_hand_to_the_default_replaces_a_value_it_could_not_read()
    {
        WithPage(Standard + "vsync moze\n", (page, _, saved) =>
        {
            Assert.Contains("moze", Note(page, "vsync"));
            var box = page.FindControl<CheckBox>("VSyncCb")!;
            box.IsChecked = true;
            box.IsChecked = false;

            page.Save();

            Assert.Contains("vsync no\n", saved());
        });
    }

    [Fact]
    public void A_level_for_other_settings_leaves_a_value_of_the_file_alone()
    {
        WithPage(Standard + "starter.batterydefault 7\ngfxrenderer vulkan\n", (page, _, saved) =>
        {
            page.FindControl<Panel>("PresetButtons")!.Children.OfType<Button>().First()
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("7", Shown(List(page, "ReadyOverrideCb")));
            page.Save();

            Assert.Contains("starter.batterydefault 7\n", saved());
            Assert.Contains("gfxrenderer vulkan\n", saved());
        });
    }

    [Fact]
    public void The_move_to_the_standard_mode_puts_values_of_the_file_right()
    {
        WithPage("gfxrenderer vulkan\nstarter.batterydefault 7\nvsync moze\ngfx.usegles yes\n", (page, _, saved) =>
        {
            page.MoveToStandardOnce();

            string text = saved();
            Assert.Contains("gfxrenderer default\n", text);
            Assert.Contains("starter.batterydefault 0\n", text);
            Assert.Contains("vsync no\n", text);
            // What lets some machines run the simulator at all stays.
            Assert.Contains("gfx.usegles yes\n", text);
            Assert.Null(Note(page, "gfxrenderer"));
            Assert.NotEqual("7", Shown(List(page, "ReadyOverrideCb")));
        });
    }
}
