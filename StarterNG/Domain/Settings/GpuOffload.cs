using System;
using System.Collections.Generic;
using System.Linq;

namespace StarterNG.Domain.Settings;

/// <summary>Which graphics card the simulator is started on, where there are two.</summary>
public enum GpuPreference
{
    /// <summary>The card that is not driving the boot display - the discrete one on a laptop.</summary>
    Discrete,
    /// <summary>Whatever the system gives a program, as a rule the integrated card.</summary>
    System
}

/// <summary>What there is to say about the cards found and the choice made.</summary>
public enum GpuNote
{
    /// <summary>No card could be read.</summary>
    Unread,
    /// <summary>One card leaves nothing to choose.</summary>
    OneCard,
    /// <summary>The system decides, by choice.</summary>
    LeftToSystem,
    /// <summary>The player chose, by what the starter was started with.</summary>
    PlayerChose,
    /// <summary>The simulator would be moved to <see cref="GpuOffload.Target"/>.</summary>
    Moved,
    /// <summary>There is no card to move the simulator to.</summary>
    NoTarget
}

/// <summary>A graphics card as Linux lists it under /sys/class/drm.</summary>
/// <param name="Slot">PCI address, as 0000:01:00.0.</param>
/// <param name="PciId">Vendor and device, as 10DE:2D19.</param>
/// <param name="Driver">The kernel driver: nvidia, amdgpu, i915...</param>
/// <param name="BootVga">The card the firmware started the display on.</param>
/// <param name="InternalPanel">The card has a laptop's built-in screen among its outputs (eDP, LVDS, DSI).</param>
public sealed record GpuDevice(string Slot, string PciId, string Driver, bool BootVga, bool InternalPanel = false)
{
    public string Vendor => PciId.Split(':')[0].ToUpperInvariant() switch
    {
        "10DE" => "NVIDIA",
        "1002" => "AMD",
        "8086" => "Intel",
        _ => PciId
    };

    public override string ToString() => $"{Vendor} {PciId} ({Driver})";
}

/// <summary>
/// The environment that moves a native Linux simulator onto the discrete card,
/// as switcheroo-control would set it. A Windows build asks for it itself
/// (NvOptimusEnablement in windows.cpp); on Linux the choice is made by
/// variables read when libGL loads, so the one starting the program sets them.
/// </summary>
public static class GpuOffload
{
    // Any of these already set means the player chose - prime-run %command% in
    // Steam's launch options, say - and the choice is theirs.
    private static readonly string[] SelectionVariables =
    {
        "__NV_PRIME_RENDER_OFFLOAD", "__GLX_VENDOR_LIBRARY_NAME", "__VK_LAYER_NV_optimus",
        "DRI_PRIME", "MESA_VK_DEVICE_SELECT", "VK_LOADER_DRIVERS_SELECT"
    };

    // Cards Mesa drives, which DRI_PRIME can pick. On one the proprietary NVIDIA
    // driver owns, DRI_PRIME leaves Mesa without a card and on llvmpipe.
    private static readonly HashSet<string> MesaDrivers = new(StringComparer.Ordinal)
    {
        "amdgpu", "radeon", "i915", "xe", "nouveau"
    };

    /// <summary>The card the simulator would be moved to, or null when it is left alone.</summary>
    public static GpuDevice? Target(IReadOnlyList<GpuDevice> gpus, GpuPreference preference,
                                    Func<string, string?> inherited)
    {
        if (preference == GpuPreference.System || gpus.Count < 2)
            return null;
        if (PlayerChoices(inherited).Count > 0)
            return null;

        // Only a laptop's integrated card hands its work to the other one. A desktop
        // runs on the card its monitor is plugged into - moving off it could as well
        // land on the integrated one - and an NVIDIA card driving the screen, through
        // a MUX, is already the discrete one.
        var boot = gpus.FirstOrDefault(g => g.BootVga);
        if (boot is null || !boot.InternalPanel || boot.Vendor == "NVIDIA")
            return null;
        return gpus.FirstOrDefault(g => !g.BootVga && Supported(g));
    }

    /// <summary>The variables that say the player chose a card, of those set.</summary>
    public static IReadOnlyList<string> PlayerChoices(Func<string, string?> inherited) =>
        SelectionVariables.Where(name => !string.IsNullOrEmpty(inherited(name))).ToArray();

    public static GpuNote NoteFor(IReadOnlyList<GpuDevice> gpus, GpuPreference preference,
                                  Func<string, string?> inherited) =>
        gpus.Count switch
        {
            0 => GpuNote.Unread,
            1 => GpuNote.OneCard,
            _ when preference == GpuPreference.System => GpuNote.LeftToSystem,
            _ when PlayerChoices(inherited).Count > 0 => GpuNote.PlayerChose,
            _ => Target(gpus, preference, inherited) is not null ? GpuNote.Moved : GpuNote.NoTarget
        };

    private static bool Supported(GpuDevice gpu) => gpu.Driver == "nvidia" || MesaDrivers.Contains(gpu.Driver);

    /// <summary>The variables that put a program on <paramref name="target"/>.</summary>
    public static IReadOnlyDictionary<string, string> EnvironmentFor(GpuDevice target) =>
        target.Driver == "nvidia"
            ? new Dictionary<string, string>
            {
                ["__NV_PRIME_RENDER_OFFLOAD"] = "1",
                ["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia",
                ["__VK_LAYER_NV_optimus"] = "NVIDIA_only",
                ["VK_LOADER_DRIVERS_SELECT"] = "*nvidia*"
            }
            : new Dictionary<string, string>
            {
                ["DRI_PRIME"] = "pci-" + target.Slot.Replace(':', '_').Replace('.', '_')
            };
}
