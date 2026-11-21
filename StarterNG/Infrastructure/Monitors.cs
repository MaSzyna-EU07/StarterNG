using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure;

/// <summary>A monitor as the simulator names it for fullscreenmonitor.</summary>
/// <param name="Id">"name:x,y" - GLFW's name with spaces as underscores, and the monitor's place on the desktop (application.cpp).</param>
/// <param name="RefreshRate">The current mode's rate in Hz, which a full screen of the simulator keeps; 0 where unknown.</param>
public sealed record DisplayMonitor(string Id, string Name, int Width, int Height, bool Primary, int RefreshRate = 0);

/// <summary>
/// The monitors the simulator can start on, named as GLFW names them: on Windows the
/// monitor's device string under its adapter, on Linux the RandR output that xrandr lists.
/// </summary>
public static class Monitors
{
    public static IReadOnlyList<DisplayMonitor> List(IEnvironment environment)
    {
        try
        {
            if (environment.IsWindows && OperatingSystem.IsWindows())
                return OnWindows();
            if (environment.IsLinux)
                return FromXrandr();
        }
        catch (Exception)
        {
            // Without the list the player still has the primary monitor.
        }

        return Array.Empty<DisplayMonitor>();
    }

    /// <summary>The id GLFW gives a monitor: its name with spaces as underscores, then where it starts.</summary>
    public static string IdOf(string name, int x, int y) => $"{name.Replace(' ', '_')}:{x},{y}";

    // ── Windows: as win32_monitor.c of GLFW walks them ───────────────────────

    // DISPLAY_DEVICEW: cb, DeviceName[32], DeviceString[128], StateFlags, DeviceID[128], DeviceKey[128].
    private const int DisplayDeviceSize = 840;
    private const int DeviceNameOffset = 4;
    private const int DeviceStringOffset = 68;
    private const int StateFlagsOffset = 324;
    private const int Active = 0x1;
    private const int PrimaryDevice = 0x4;

    // DEVMODEW: dmSize at 68, dmPosition at 76, dmPelsWidth and dmPelsHeight at 172 and 176,
    // dmDisplayFrequency at 184.
    private const int DevModeSize = 220;
    private const int DevModeSizeOffset = 68;
    private const int PositionOffset = 76;
    private const int WidthOffset = 172;
    private const int HeightOffset = 176;
    private const int DisplayFrequencyOffset = 184;
    private const int CurrentSettings = -1;
    private const int RotatedMode = 0x4;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int EnumDisplayDevicesW(IntPtr device, int index, IntPtr displayDevice, int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int EnumDisplaySettingsExW(IntPtr deviceName, int modeNum, IntPtr devMode, int flags);

    private static IReadOnlyList<DisplayMonitor> OnWindows()
    {
        var monitors = new List<DisplayMonitor>();
        IntPtr adapter = Blank(DisplayDeviceSize, 0, DisplayDeviceSize);
        IntPtr display = Blank(DisplayDeviceSize, 0, DisplayDeviceSize);
        IntPtr mode = Blank(DevModeSize, DevModeSizeOffset, DevModeSize);
        try
        {
            for (int a = 0; EnumDisplayDevicesW(IntPtr.Zero, a, adapter, 0) != 0; a++)
            {
                if ((Marshal.ReadInt32(adapter, StateFlagsOffset) & Active) == 0)
                    continue;

                IntPtr adapterName = adapter + DeviceNameOffset;
                Clear(mode, DevModeSize, DevModeSizeOffset);
                if (EnumDisplaySettingsExW(adapterName, CurrentSettings, mode, RotatedMode) == 0)
                    continue;

                int x = Marshal.ReadInt32(mode, PositionOffset), y = Marshal.ReadInt32(mode, PositionOffset + 4);
                int width = Marshal.ReadInt32(mode, WidthOffset), height = Marshal.ReadInt32(mode, HeightOffset);
                bool primary = (Marshal.ReadInt32(adapter, StateFlagsOffset) & PrimaryDevice) != 0;
                // 0 and 1 stand for the hardware's default rate.
                int rate = Marshal.ReadInt32(mode, DisplayFrequencyOffset);

                // A monitor under the adapter gives its own name; with none, the adapter's stands.
                string name = Marshal.PtrToStringUni(adapter + DeviceStringOffset) ?? string.Empty;
                Clear(display, DisplayDeviceSize, 0);
                Marshal.WriteInt32(display, 0, DisplayDeviceSize);
                for (int d = 0; EnumDisplayDevicesW(adapterName, d, display, 0) != 0; d++)
                {
                    if ((Marshal.ReadInt32(display, StateFlagsOffset) & Active) == 0)
                        continue;
                    name = Marshal.PtrToStringUni(display + DeviceStringOffset) ?? name;
                    break;
                }

                monitors.Add(new DisplayMonitor(IdOf(name, x, y), name, width, height, primary, rate > 1 ? rate : 0));
                Clear(adapter, DisplayDeviceSize, 0);
                Marshal.WriteInt32(adapter, 0, DisplayDeviceSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adapter);
            Marshal.FreeHGlobal(display);
            Marshal.FreeHGlobal(mode);
        }

        return monitors;
    }

    private static IntPtr Blank(int size, int sizeOffset, int sizeValue)
    {
        IntPtr block = Marshal.AllocHGlobal(size);
        Clear(block, size, sizeOffset);
        if (sizeOffset == 0)
            Marshal.WriteInt32(block, 0, sizeValue);
        else
            Marshal.WriteInt16(block, sizeOffset, (short)sizeValue);
        return block;
    }

    private static void Clear(IntPtr block, int size, int sizeOffset)
    {
        for (int i = 0; i < size; i++)
            Marshal.WriteByte(block, i, 0);
        if (sizeOffset > 0)
            Marshal.WriteInt16(block, sizeOffset, (short)size);
    }

    // ── Linux: the connected RandR outputs, as x11_monitor.c of GLFW names them ──

    // A line of xrandr is short; one that takes longer than this to match is no report of monitors.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex Output = new(
        @"^(\S+) connected (primary )?(\d+)x(\d+)\+(\d+)\+(\d+)", RegexOptions.CultureInvariant, MatchTimeout);

    // A mode line under an output: the rate in use is the one marked with a star.
    private static readonly Regex CurrentRate = new(@"^\s+\S+.*?\s(\d+(?:\.\d+)?)\*", RegexOptions.CultureInvariant, MatchTimeout);

    // Where distributions put xrandr: run from there, not from whatever PATH finds first.
    private static readonly string[] XrandrPlaces = { "/usr/bin/xrandr", "/usr/local/bin/xrandr", "/bin/xrandr" };

    private static IReadOnlyList<DisplayMonitor> FromXrandr()
    {
        string? xrandr = Array.Find(XrandrPlaces, System.IO.File.Exists);
        if (xrandr is null)
            return Array.Empty<DisplayMonitor>();
        using var process = Process.Start(new ProcessStartInfo(xrandr, "--query")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        if (process is null)
            return Array.Empty<DisplayMonitor>();
        string text = process.StandardOutput.ReadToEnd();
        process.WaitForExit(2000);
        return Parse(text);
    }

    /// <summary>The monitors of xrandr's report, each with the rate its mode in use runs at.</summary>
    public static IReadOnlyList<DisplayMonitor> Parse(string xrandr)
    {
        var monitors = new List<DisplayMonitor>();
        bool underConnected = false;
        foreach (string line in xrandr.Split('\n'))
        {
            var m = Output.Match(line);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                int width = int.Parse(m.Groups[3].Value), height = int.Parse(m.Groups[4].Value);
                int x = int.Parse(m.Groups[5].Value), y = int.Parse(m.Groups[6].Value);
                monitors.Add(new DisplayMonitor(IdOf(name, x, y), name, width, height, m.Groups[2].Success));
                underConnected = true;
                continue;
            }
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                // Another output, connected without a place on the desktop or not at all.
                underConnected = false;
                continue;
            }

            var rate = CurrentRate.Match(line);
            if (underConnected && rate.Success && monitors[^1].RefreshRate == 0)
                monitors[^1] = monitors[^1] with
                {
                    RefreshRate = (int)Math.Round(double.Parse(rate.Groups[1].Value, CultureInfo.InvariantCulture))
                };
        }
        return monitors;
    }
}
