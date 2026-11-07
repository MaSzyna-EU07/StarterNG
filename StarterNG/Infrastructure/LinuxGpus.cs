using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure;

/// <summary>
/// The graphics cards of a Linux machine, read from /sys/class/drm - which the
/// Steam Linux Runtime container shows too, unlike some of the host's tools.
/// </summary>
public sealed class LinuxGpus
{
    private const string DrmClass = "/sys/class/drm";

    // card0 and card1, not their connectors (card0-eDP-2) or render nodes.
    private static readonly Regex Card = new(@"^card\d+$", RegexOptions.CultureInvariant);

    // The connectors of a laptop's own screen, listed whether lit or not.
    private static readonly Regex Panel = new(@"^(card\d+)-(eDP|LVDS|DSI)-\d+$", RegexOptions.CultureInvariant);

    private readonly IFileSystem _files;
    private readonly IEnvironment _environment;

    public LinuxGpus(IFileSystem files, IEnvironment environment)
    {
        _files = files;
        _environment = environment;
    }

    /// <summary>Every card with a PCI address and a driver; empty off Linux, or when /sys cannot be read.</summary>
    public IReadOnlyList<GpuDevice> List()
    {
        if (!_environment.IsLinux)
            return Array.Empty<GpuDevice>();

        try
        {
            var gpus = new List<GpuDevice>();
            var entries = _files.GetDirectories(DrmClass).Select(Path.GetFileName).OfType<string>().ToList();
            var withPanel = entries.Select(e => Panel.Match(e)).Where(m => m.Success)
                                   .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (string name in entries.Where(e => Card.IsMatch(e)))
            {
                string card = Path.Combine(DrmClass, name);
                string device = Path.Combine(card, "device");
                string uevent = Path.Combine(device, "uevent");
                if (!_files.FileExists(uevent))
                    continue;

                var fields = _files.ReadAllLines(uevent)
                    .Select(line => line.Split('=', 2))
                    .Where(pair => pair.Length == 2)
                    .ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.Ordinal);
                if (!fields.TryGetValue("PCI_SLOT_NAME", out string? slot) ||
                    !fields.TryGetValue("PCI_ID", out string? id))
                    continue;

                string bootVga = Path.Combine(device, "boot_vga");
                bool boot = _files.FileExists(bootVga) && _files.ReadAllText(bootVga).Trim() == "1";
                string vram = Path.Combine(device, "mem_info_vram_total");
                long memory = _files.FileExists(vram) && long.TryParse(_files.ReadAllText(vram).Trim(), out long bytes)
                    ? bytes
                    : 0;
                gpus.Add(new GpuDevice(slot, id, fields.GetValueOrDefault("DRIVER", ""), boot, withPanel.Contains(name),
                                       memory));
            }

            return gpus.OrderBy(g => g.Slot, StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<GpuDevice>();
        }
    }
}
