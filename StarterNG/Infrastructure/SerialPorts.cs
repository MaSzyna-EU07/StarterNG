using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace StarterNG.Infrastructure;

/// <summary>The serial ports present now, named the way the simulator's uart key wants them.</summary>
public static class SerialPorts
{
    // USB adapters and boards; the legacy ttyS ports exist on every PC whether
    // wired or not, so they would only crowd the list.
    private static readonly string[] UnixPatterns = { "ttyUSB*", "ttyACM*", "ttyAMA*", "rfcomm*", "cu.*" };

    public static IReadOnlyList<string> List()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return FromRegistry();

            return UnixPatterns
                .SelectMany(pattern => Directory.GetFiles("/dev", pattern))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> FromRegistry()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
            return Array.Empty<string>();
        var ports = new List<string>();
        foreach (string name in key.GetValueNames())
            if (key.GetValue(name) is string port)
                ports.Add(port);
        // COM3 before COM10.
        return ports.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
