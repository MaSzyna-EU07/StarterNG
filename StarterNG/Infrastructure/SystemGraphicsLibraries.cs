using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace StarterNG.Infrastructure;

/// <summary>
/// The starter sits in the simulator's folder, and so do the graphics wrappers a player
/// puts there for the simulator - ReShade and the like, named after the system library
/// they stand in for. Windows looks in the program's own folder first, so the starter's
/// window drew through them too, and with some of them it drew nothing at all.
/// Loaded from System32 by full path before anything asks for them, these names are
/// taken: a native library that asks by name gets the system one. .NET looks beside the
/// program by full path before it asks by name, so the window code's own imports are
/// handed the same libraries. The simulator is a process of its own and still finds its
/// wrappers.
/// Elsewhere there is nothing of the kind to keep out. Under Wine or Proton the system
/// folder is the prefix's, so what is taken there is what would have been taken anyway.
/// </summary>
public static class SystemGraphicsLibraries
{
    // dxgi ahead of d3d11, which asks for it.
    private static readonly string[] Names = { "dxgi", "d3d11", "d3d9", "opengl32" };

    private static readonly Dictionary<string, IntPtr> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> Shadowed = new();

    /// <param name="importers">The assemblies whose imports of these libraries are to get the system ones.</param>
    public static void LoadFirst(params Assembly[] importers)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string system = Environment.SystemDirectory;
        string own = AppContext.BaseDirectory;

        foreach (string name in Names)
        {
            string file = name + ".dll";
            if (File.Exists(Path.Combine(own, file)))
                Shadowed.Add(file);
            if (NativeLibrary.TryLoad(Path.Combine(system, file), out IntPtr handle))
                Loaded[name] = handle;
        }

        foreach (var importer in importers)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(importer, Resolve);
            }
            catch (InvalidOperationException)
            {
                // It has a resolver of its own already; that one decides.
            }
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        string name = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName[..^4]
            : libraryName;
        // Zero leaves the library to the usual search.
        return Loaded.TryGetValue(name, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    /// <summary>Which of them lay beside the starter, once the log can take it.</summary>
    public static void LogShadowed()
    {
        if (Shadowed.Count > 0)
            Diagnostics.Log($"Graphics libraries beside the starter, left to the simulator: {string.Join(", ", Shadowed)}");
    }
}
