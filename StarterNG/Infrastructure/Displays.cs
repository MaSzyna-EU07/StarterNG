using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure;

/// <summary>A screen, and the fastest it refreshes.</summary>
/// <param name="Name">The connector on Linux (eDP-1, HDMI-A-1); empty for the primary screen on Windows.</param>
public sealed record Display(string Name, int RefreshRate);

/// <summary>
/// The screens attached, for a frame rate limit to match: on Linux from the EDID
/// each connected connector of /sys/class/drm passes on - the most the panel can
/// do, which the desktop may not be using - and on Windows the primary screen's
/// current mode.
/// </summary>
public sealed class Displays
{
    private const string DrmClass = "/sys/class/drm";

    private static readonly Regex Connector = new(@"^card\d+-(.+)$", RegexOptions.CultureInvariant);

    private readonly IFileSystem _files;
    private readonly IEnvironment _environment;

    public Displays(IFileSystem files, IEnvironment environment)
    {
        _files = files;
        _environment = environment;
    }

    /// <summary>Every screen whose refresh rate could be read; empty where none could.</summary>
    public IReadOnlyList<Display> List()
    {
        try
        {
            if (_environment.IsLinux)
                return FromDrm();
            if (_environment.IsWindows && OperatingSystem.IsWindows())
                return PrimaryOnWindows() is { } rate ? new[] { new Display(string.Empty, rate) } : Array.Empty<Display>();
        }
        catch (Exception)
        {
            // A screen we cannot read leaves the limit to the player, as before.
        }

        return Array.Empty<Display>();
    }

    private IReadOnlyList<Display> FromDrm()
    {
        var displays = new List<Display>();
        foreach (string directory in _files.GetDirectories(DrmClass))
        {
            var match = Connector.Match(Path.GetFileName(directory));
            if (!match.Success || match.Groups[1].Value.StartsWith("Writeback", StringComparison.Ordinal))
                continue;

            try
            {
                string status = Path.Combine(directory, "status");
                if (!_files.FileExists(status) || _files.ReadAllText(status).Trim() != "connected")
                    continue;
                int rate = Edid.HighestRefreshRate(_files.ReadAllBytes(Path.Combine(directory, "edid")));
                if (rate > 0)
                    displays.Add(new Display(match.Groups[1].Value, rate));
            }
            catch (Exception)
            {
                // One connector we cannot read says nothing of the others.
            }
        }

        return displays;
    }

    // DEVMODEW: dmSize at 68, dmDisplayFrequency at 184, 220 bytes with no driver data.
    private const int DevModeSize = 220;
    private const int DevModeSizeOffset = 68;
    private const int DisplayFrequencyOffset = 184;
    private const int CurrentSettings = -1;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int EnumDisplaySettingsW(IntPtr deviceName, int modeNum, IntPtr devMode);

    private static int? PrimaryOnWindows()
    {
        IntPtr mode = Marshal.AllocHGlobal(DevModeSize);
        try
        {
            for (int i = 0; i < DevModeSize; i++)
                Marshal.WriteByte(mode, i, 0);
            Marshal.WriteInt16(mode, DevModeSizeOffset, DevModeSize);
            if (EnumDisplaySettingsW(IntPtr.Zero, CurrentSettings, mode) == 0)
                return null;
            int rate = Marshal.ReadInt32(mode, DisplayFrequencyOffset);
            // 0 and 1 stand for the hardware's default rate.
            return rate > 1 ? rate : null;
        }
        finally
        {
            Marshal.FreeHGlobal(mode);
        }
    }
}

/// <summary>The timings an EDID lists, as far as a refresh rate goes.</summary>
public static class Edid
{
    /// <summary>The fastest refresh rate among the detailed timings, rounded; 0 for none.</summary>
    public static int HighestRefreshRate(byte[] edid)
    {
        if (edid.Length < 128)
            return 0;

        // The base block's four descriptors.
        double best = 0;
        for (int at = 54; at + 18 <= 126; at += 18)
            best = Math.Max(best, DetailedTiming(edid, at));

        for (int block = 128; block + 128 <= edid.Length; block += 128)
        {
            switch (edid[block])
            {
                // CTA-861: detailed timings from the offset in its third byte on.
                case 0x02 when edid[block + 2] >= 4:
                    for (int at = block + edid[block + 2]; at + 18 <= block + 127; at += 18)
                        best = Math.Max(best, DetailedTiming(edid, at));
                    break;
                // DisplayID, where a panel faster than 60 Hz often keeps its only fast mode.
                case 0x70:
                    best = Math.Max(best, DisplayId(edid, block));
                    break;
            }
        }

        return (int)Math.Round(best);
    }

    // An 18-byte detailed timing; a pixel clock of zero marks some other descriptor.
    private static double DetailedTiming(byte[] e, int at)
    {
        int clock = e[at] | e[at + 1] << 8;
        if (clock == 0)
            return 0;

        int hActive = e[at + 2] | (e[at + 4] & 0xF0) << 4, hBlank = e[at + 3] | (e[at + 4] & 0x0F) << 8;
        int vActive = e[at + 5] | (e[at + 7] & 0xF0) << 4, vBlank = e[at + 6] | (e[at + 7] & 0x0F) << 8;
        return Rate(clock * 10_000.0, hActive + hBlank, vActive + vBlank);
    }

    // Data blocks from the sixth byte on; type I timings (DisplayID 1.3) count the
    // pixel clock in 10 kHz, type VII (2.0) in kHz, and both store each size less one.
    private static double DisplayId(byte[] e, int block)
    {
        double best = 0;
        int end = Math.Min(block + 5 + e[block + 2], block + 127);
        for (int at = block + 5; at + 3 <= end;)
        {
            int tag = e[at], length = e[at + 2];
            if (tag == 0 && length == 0)
                break;

            int payload = at + 3;
            if (tag is 0x03 or 0x22)
            {
                for (int t = payload; t + 20 <= Math.Min(payload + length, end); t += 20)
                {
                    double clock = (e[t] | e[t + 1] << 8 | e[t + 2] << 16) + 1;
                    clock *= tag == 0x03 ? 10_000 : 1_000;
                    int hTotal = (e[t + 4] | e[t + 5] << 8) + 1 + (e[t + 6] | e[t + 7] << 8) + 1;
                    int vTotal = (e[t + 12] | e[t + 13] << 8) + 1 + (e[t + 14] | e[t + 15] << 8) + 1;
                    best = Math.Max(best, Rate(clock, hTotal, vTotal));
                }
            }

            at = payload + length;
        }

        return best;
    }

    private static double Rate(double pixelClock, int hTotal, int vTotal) =>
        hTotal > 0 && vTotal > 0 ? pixelClock / ((double)hTotal * vTotal) : 0;
}
