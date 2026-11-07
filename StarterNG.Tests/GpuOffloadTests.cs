using StarterNG.Domain.Settings;
using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class GpuOffloadTests
{
    // The laptop this was written on: an RTX 5060 beside a Radeon 780M that drives the screen.
    private static readonly GpuDevice Nvidia = new("0000:01:00.0", "10DE:2D19", "nvidia", BootVga: false,
                                                   InternalPanel: true);
    private static readonly GpuDevice Radeon780M = new("0000:65:00.0", "1002:1900", "amdgpu", BootVga: true,
                                                       InternalPanel: true);
    private static readonly GpuDevice RadeonDiscrete = new("0000:03:00.0", "1002:73DF", "amdgpu", BootVga: false);
    private static readonly GpuDevice IntelIntegrated = new("0000:00:02.0", "8086:A7A0", "i915", BootVga: true,
                                                            InternalPanel: true);

    private static string? Unset(string name) => null;

    private static IReadOnlyDictionary<string, string> For(GpuPreference preference, params GpuDevice[] gpus) =>
        Inheriting(Unset, preference, gpus);

    private static IReadOnlyDictionary<string, string> Inheriting(Func<string, string?> inherited,
                                                                  GpuPreference preference,
                                                                  params GpuDevice[] gpus) =>
        GpuOffload.Target(gpus, preference, inherited) is { } target
            ? GpuOffload.EnvironmentFor(target)
            : new Dictionary<string, string>();

    [Fact]
    public void A_proprietary_nvidia_card_gets_prime_render_offload()
    {
        var environment = For(GpuPreference.Discrete, Nvidia, Radeon780M);

        Assert.Equal("1", environment["__NV_PRIME_RENDER_OFFLOAD"]);
        Assert.Equal("nvidia", environment["__GLX_VENDOR_LIBRARY_NAME"]);
        Assert.Equal("NVIDIA_only", environment["__VK_LAYER_NV_optimus"]);
        // DRI_PRIME on a card Mesa does not drive leaves Mesa on llvmpipe.
        Assert.False(environment.ContainsKey("DRI_PRIME"));
    }

    [Fact]
    public void A_card_mesa_drives_is_picked_by_its_pci_address()
    {
        var environment = For(GpuPreference.Discrete, IntelIntegrated, RadeonDiscrete);

        Assert.Equal("pci-0000_03_00_0", Assert.Single(environment).Value);
        Assert.Equal("DRI_PRIME", environment.Keys.Single());
    }

    [Fact]
    public void A_card_on_a_driver_of_unknown_ways_is_left_alone()
    {
        var odd = Nvidia with { Driver = "vfio-pci" };

        Assert.Empty(For(GpuPreference.Discrete, odd, Radeon780M));
    }

    [Fact]
    public void One_card_leaves_nothing_to_choose()
    {
        Assert.Empty(For(GpuPreference.Discrete, Radeon780M));
    }

    [Fact]
    public void The_system_choice_changes_nothing()
    {
        Assert.Empty(For(GpuPreference.System, Nvidia, Radeon780M));
    }

    [Theory]
    [InlineData("DRI_PRIME")]
    [InlineData("__NV_PRIME_RENDER_OFFLOAD")]
    [InlineData("__GLX_VENDOR_LIBRARY_NAME")]
    public void A_choice_the_player_made_stands(string variable)
    {
        // prime-run %command% in Steam's launch options, say.
        var environment = Inheriting(name => name == variable ? "1" : null, GpuPreference.Discrete, Nvidia, Radeon780M);

        Assert.Empty(environment);
    }

    [Fact]
    public void A_discrete_card_already_driving_the_screen_needs_no_offload()
    {
        // A laptop with its MUX switched to the NVIDIA card.
        Assert.Empty(For(GpuPreference.Discrete, Nvidia with { BootVga = true },
                         Radeon780M with { BootVga = false }));
    }

    [Fact]
    public void A_desktop_stays_on_the_card_its_monitor_is_plugged_into()
    {
        // The monitor on a Radeon, the processor's own graphics left on: moving
        // off the boot card would land on the weaker one.
        var radeon = RadeonDiscrete with { BootVga = true };
        var intel = IntelIntegrated with { BootVga = false, InternalPanel = false };

        Assert.Empty(For(GpuPreference.Discrete, radeon, intel));
    }

    // ── What the settings say about it ───────────────────────────────────────

    [Fact]
    public void No_card_read_is_said_whatever_was_chosen()
    {
        Assert.Equal(GpuNote.Unread, GpuOffload.NoteFor(Array.Empty<GpuDevice>(), GpuPreference.Discrete, Unset));
        Assert.Equal(GpuNote.Unread, GpuOffload.NoteFor(Array.Empty<GpuDevice>(), GpuPreference.System, name => "1"));
    }

    [Fact]
    public void One_card_is_said_whatever_was_chosen()
    {
        Assert.Equal(GpuNote.OneCard, GpuOffload.NoteFor(new[] { Radeon780M }, GpuPreference.Discrete, Unset));
        Assert.Equal(GpuNote.OneCard, GpuOffload.NoteFor(new[] { Radeon780M }, GpuPreference.System, name => "1"));
    }

    [Fact]
    public void The_system_choice_comes_before_what_the_player_set()
    {
        Assert.Equal(GpuNote.LeftToSystem,
                     GpuOffload.NoteFor(new[] { Nvidia, Radeon780M }, GpuPreference.System, name => "1"));
    }

    [Fact]
    public void What_the_player_set_comes_before_the_card_it_would_be_moved_to()
    {
        static string? Inherited(string name) => name is "DRI_PRIME" or "__NV_PRIME_RENDER_OFFLOAD" ? "1" : null;

        Assert.Equal(GpuNote.PlayerChose,
                     GpuOffload.NoteFor(new[] { Nvidia, Radeon780M }, GpuPreference.Discrete, Inherited));
        Assert.Equal(new[] { "__NV_PRIME_RENDER_OFFLOAD", "DRI_PRIME" }, GpuOffload.PlayerChoices(Inherited));
    }

    [Fact]
    public void A_variable_set_empty_is_no_choice_of_the_player()
    {
        Assert.Empty(GpuOffload.PlayerChoices(name => string.Empty));
        Assert.Equal(GpuNote.Moved,
                     GpuOffload.NoteFor(new[] { Nvidia, Radeon780M }, GpuPreference.Discrete, name => string.Empty));
    }

    [Fact]
    public void The_note_says_a_move_only_with_a_card_to_move_to()
    {
        Assert.Equal(GpuNote.Moved, GpuOffload.NoteFor(new[] { Nvidia, Radeon780M }, GpuPreference.Discrete, Unset));
        Assert.Equal(GpuNote.NoTarget,
                     GpuOffload.NoteFor(new[] { Nvidia with { Driver = "vfio-pci" }, Radeon780M },
                                        GpuPreference.Discrete, Unset));
    }

    // ── Reading the cards ────────────────────────────────────────────────────

    private static InMemoryFileSystem Card(InMemoryFileSystem files, string card, string driver, string id,
                                           string slot, bool bootVga) =>
        files.WithFile($"/sys/class/drm/{card}/device/uevent",
                       $"DRIVER={driver}\nPCI_CLASS=30000\nPCI_ID={id}\nPCI_SLOT_NAME={slot}\n")
             .WithFile($"/sys/class/drm/{card}/device/boot_vga", bootVga ? "1\n" : "0\n");

    [Fact]
    public void The_cards_are_read_from_sysfs()
    {
        var files = new InMemoryFileSystem(caseSensitive: true);
        Card(files, "card0", "nvidia", "10DE:2D19", "0000:01:00.0", bootVga: false);
        Card(files, "card1", "amdgpu", "1002:1900", "0000:65:00.0", bootVga: true);
        // A connector, not a card.
        files.WithFile("/sys/class/drm/card0-eDP-2/device/uevent", "DRIVER=nvidia\nPCI_ID=10DE:2D19\nPCI_SLOT_NAME=x\n");

        files.WithFile("/sys/class/drm/card1-eDP-1/status", "connected\n");

        var gpus = new LinuxGpus(files, new FakeEnvironment()).List();

        Assert.Equal(new[] { Nvidia, Radeon780M }, gpus);
    }

    [Fact]
    public void Off_linux_there_are_no_cards_to_move_between()
    {
        var files = new InMemoryFileSystem(caseSensitive: true);
        Card(files, "card0", "nvidia", "10DE:2D19", "0000:01:00.0", bootVga: false);

        Assert.Empty(new LinuxGpus(files, new FakeEnvironment().AsWindows()).List());
    }

    [Fact]
    public void Without_sysfs_there_are_no_cards()
    {
        Assert.Empty(new LinuxGpus(new InMemoryFileSystem(), new FakeEnvironment()).List());
    }
}
