using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure;

/// <summary>
/// The names and memory of the machine's graphics cards, the one a simulator
/// runs on first. Nothing on macOS, where the simulator does not run natively.
/// </summary>
public sealed class GraphicsCards
{
    private static readonly string[] PciLists =
    {
        "/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids",
        // The host's, from inside the Steam Linux Runtime container.
        "/run/host/usr/share/hwdata/pci.ids", "/run/host/usr/share/misc/pci.ids"
    };

    private const string RegistryMachine = @"\Registry\Machine\";

    private readonly IFileSystem _files;
    private readonly IEnvironment _environment;
    private readonly LinuxGpus _gpus;
    private readonly Func<IReadOnlyList<string>> _nvidiaSmi;

    public GraphicsCards(IFileSystem files, IEnvironment environment, LinuxGpus gpus,
                         Func<IReadOnlyList<string>>? nvidiaSmi = null)
    {
        _files = files;
        _environment = environment;
        _gpus = gpus;
        _nvidiaSmi = nvidiaSmi ?? AskNvidiaSmi;
    }

    /// <summary>Takes a while on Linux, where the PCI list is read: not for the UI thread.</summary>
    public IReadOnlyList<GraphicsCard> List()
    {
        IEnumerable<GraphicsCard> cards = Array.Empty<GraphicsCard>();
        if (_environment.IsLinux)
            cards = FromLinux();
        else if (_environment.IsWindows && OperatingSystem.IsWindows())
            cards = FromRegistry();

        // A program goes to the discrete card, on Windows by its own request.
        return cards.OrderBy(card => card.SharedMemory).ThenByDescending(card => card.MemoryBytes).ToList();
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<GraphicsCard> FromRegistry()
    {
        try
        {
            using var video = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\VIDEO");
            if (video is null)
                return Array.Empty<GraphicsCard>();

            var cards = new List<GraphicsCard>();
            foreach (string name in video.GetValueNames())
            {
                if (!name.StartsWith(@"\Device\Video", StringComparison.OrdinalIgnoreCase) ||
                    video.GetValue(name) is not string path ||
                    !path.StartsWith(RegistryMachine, StringComparison.OrdinalIgnoreCase))
                    continue;

                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path[RegistryMachine.Length..]);
                // The system's basic display driver and remote desktops are no cards.
                if (key?.GetValue("DriverDesc") is not string card ||
                    key.GetValue("MatchingDeviceId") is not string device ||
                    !device.StartsWith(@"pci\", StringComparison.OrdinalIgnoreCase))
                    continue;

                long memory = MemoryIn(key.GetValue("HardwareInformation.qwMemorySize"));
                if (memory == 0)
                    memory = MemoryIn(key.GetValue("HardwareInformation.MemorySize"));
                cards.Add(new GraphicsCard(card, memory));
            }

            // A card is listed once for each of its outputs.
            return cards.DistinctBy(card => card.Name).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Array.Empty<GraphicsCard>();
        }
    }

    private static long MemoryIn(object? value) => value switch
    {
        long qword => qword,
        int dword => (uint)dword,
        byte[] { Length: 8 } bytes => BitConverter.ToInt64(bytes),
        byte[] { Length: 4 } bytes => BitConverter.ToUInt32(bytes),
        _ => 0
    };

    private IEnumerable<GraphicsCard> FromLinux()
    {
        var gpus = _gpus.List();
        if (gpus.Count == 0)
            return Array.Empty<GraphicsCard>();

        IReadOnlyList<string>? pciList = null;
        var nvidiaMemory = gpus.Any(gpu => gpu.Driver == "nvidia")
            ? ParseNvidiaMemory(_nvidiaSmi())
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var cards = new List<GraphicsCard>();
        foreach (var gpu in gpus)
        {
            string? model = NvidiaModel(gpu.Slot);
            if (model is null)
            {
                pciList ??= ReadPciList();
                model = ModelInPciList(pciList, gpu.PciId);
            }

            string name = model is null ? $"{gpu.Vendor} {gpu.PciId}"
                        : model.StartsWith(gpu.Vendor, StringComparison.OrdinalIgnoreCase) ? model
                        : $"{gpu.Vendor} {model}";
            long memory = gpu.MemoryBytes > 0 ? gpu.MemoryBytes : nvidiaMemory.GetValueOrDefault(gpu.Slot);
            cards.Add(new GraphicsCard(name, memory));
        }

        return cards;
    }

    private string? NvidiaModel(string slot)
    {
        string path = $"/proc/driver/nvidia/gpus/{slot}/information";
        try
        {
            if (!_files.FileExists(path))
                return null;

            foreach (string line in _files.ReadAllLines(path))
                if (line.StartsWith("Model:", StringComparison.Ordinal))
                    return line["Model:".Length..].Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private IReadOnlyList<string> ReadPciList()
    {
        foreach (string path in PciLists)
        {
            try
            {
                if (_files.FileExists(path))
                    return _files.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>What a pci.ids list calls a card: the marketing name in brackets, if it has one.</summary>
    public static string? ModelInPciList(IEnumerable<string> lines, string pciId)
    {
        string[] ids = pciId.ToLowerInvariant().Split(':');
        if (ids.Length != 2)
            return null;

        string vendorLine = ids[0] + "  ";
        string deviceLine = "\t" + ids[1] + "  ";
        bool inVendor = false;
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] != '\t')
            {
                if (inVendor)
                    return null;
                inVendor = line.StartsWith(vendorLine, StringComparison.Ordinal);
            }
            else if (inVendor && line.StartsWith(deviceLine, StringComparison.Ordinal))
            {
                string model = line[deviceLine.Length..].Trim();
                int bracket = model.LastIndexOf('[');
                return bracket >= 0 && model.EndsWith(']') ? model[(bracket + 1)..^1] : model;
            }
        }

        return null;
    }

    /// <summary>Lines of "00000000:01:00.0, 8188": the PCI address, as /sys gives it with a shorter domain, and MiB.</summary>
    public static IReadOnlyDictionary<string, long> ParseNvidiaMemory(IEnumerable<string> lines)
    {
        var memory = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string[] fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length == 2 && fields[0].Length >= 12 && long.TryParse(fields[1], out long mib))
                memory[fields[0][^12..]] = mib << 20;
        }

        return memory;
    }

    private static IReadOnlyList<string> AskNvidiaSmi()
    {
        var start = new ProcessStartInfo("nvidia-smi", "--query-gpu=pci.bus_id,memory.total --format=csv,noheader,nounits")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return Array.Empty<string>();

            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return Array.Empty<string>();
            }

            return output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        // Not installed, as it often is not inside the Steam Linux Runtime container.
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return Array.Empty<string>();
        }
    }
}
