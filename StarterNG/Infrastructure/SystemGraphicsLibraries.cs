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
public sealed class SystemGraphicsLibraries
{
    public delegate bool Loader(string path, out IntPtr handle);

    // dxgi ahead of d3d11, which asks for it.
    public static readonly IReadOnlyList<string> Names = new[] { "dxgi", "d3d11", "d3d9", "opengl32" };

    private readonly Dictionary<string, IntPtr> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _shadowed = new();

    private SystemGraphicsLibraries()
    {
    }

    /// <summary>The files of these names that lay beside the starter.</summary>
    public IReadOnlyList<string> Shadowed => _shadowed;

    /// <summary>What goes to the log about them, or null when none lay there.</summary>
    public string? Note => _shadowed.Count > 0
        ? $"Graphics libraries beside the starter, left to the simulator: {string.Join(", ", _shadowed)}"
        : null;

    /// <summary>The libraries of this Windows, or null on any other system.</summary>
    public static SystemGraphicsLibraries? TakeOnWindows() =>
        OperatingSystem.IsWindows()
            ? Take(Environment.SystemDirectory, AppContext.BaseDirectory, File.Exists, NativeLibrary.TryLoad)
            : null;

    public static SystemGraphicsLibraries Take(string systemDirectory, string ownDirectory,
                                               Func<string, bool> exists, Loader load)
    {
        var taken = new SystemGraphicsLibraries();
        foreach (string name in Names)
        {
            string file = name + ".dll";
            if (exists(Path.Combine(ownDirectory, file)))
                taken._shadowed.Add(file);
            if (load(Path.Combine(systemDirectory, file), out IntPtr handle))
                taken._loaded[name] = handle;
        }
        return taken;
    }

    /// <summary>The system library for an import of one of these names; zero leaves it to the usual search.</summary>
    public IntPtr Resolve(string libraryName)
    {
        string name = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName[..^4]
            : libraryName;
        return _loaded.TryGetValue(name, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    /// <summary>
    /// The assembly's imports of these names get the system libraries. False when it
    /// has a resolver of its own already; that one decides.
    /// </summary>
    public bool ServeTo(Assembly importer)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(importer, (name, _, _) => Resolve(name));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
