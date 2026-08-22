using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StarterNG.Classes;

public enum LoadPhase
{
    Vehicles,
    Sceneries,
    Done
}

/// <summary>A single progress update emitted while the game data loads.</summary>
public readonly record struct LoadStatus(double Fraction, LoadPhase Phase, string? Detail);

/// <summary>
/// Loads and holds the data shared across the launcher (vehicle database and
/// parsed sceneries) so it is parsed once, at startup, behind the splash —
/// instead of inside each view's constructor.
/// </summary>
public sealed class GameData
{
    public static GameData Instance { get; } = new();

    public VehicleDatabase Vehicles { get; } = new();
    public List<Scenery> Sceneries { get; } = new();

    public bool Loaded { get; private set; }

    /// <summary>
    /// Parses the vehicle database and all sceneries, reporting progress.
    /// Runs synchronously; call it from a background thread. Never throws.
    /// </summary>
    public void Load(IProgress<LoadStatus>? progress = null,
                     string vehiclesDir = "databases/vehicles/",
                     string sceneryDir = "scenery/",
                     string miniDir = "textures/mini/")
    {
        if (Loaded)
            return;

        var vehicleFiles = VehicleDatabase.EnumerateFiles(vehiclesDir);
        var scnFiles = EnumerateScenery(sceneryDir);
        int total = Math.Max(1, vehicleFiles.Count + scnFiles.Count);
        int done = 0;

        // Phase 1 - vehicle database
        Vehicles.BeginLoad();
        foreach (string file in vehicleFiles)
        {
            progress?.Report(new LoadStatus((double)done / total, LoadPhase.Vehicles,
                Path.GetFileName(file)));
            try { Vehicles.LoadFile(file); }
            catch { /* skip malformed file */ }
            done++;
        }
        Vehicles.EndLoad();

        // Preload the miniature .bmp index now (behind the splash) so the depot's
        // first thumbnail render doesn't scan textures/mini/ on the UI thread.
        VehicleDatabase.PreloadMiniIndex(miniDir);

        // Likewise index every .fiz under dynamic/ once, so the consist's physics
        // (length / mass / couplings) resolve without a UI-thread directory scan.
        Physics.PreloadIndex();

        // Phase 2 - sceneries
        foreach (string file in scnFiles)
        {
            progress?.Report(new LoadStatus((double)done / total, LoadPhase.Sceneries,
                Path.GetFileName(file)));
            try { Sceneries.Add(new Scenery(file)); }
            catch { /* skip unreadable scenery */ }
            done++;
        }

        progress?.Report(new LoadStatus(1.0, LoadPhase.Done, null));
        Loaded = true;
    }

    /// <summary>Re-reads scenery/*.scn (Pascal actReloadScenarios).</summary>
    public void ReloadSceneries(string sceneryDir = "scenery/")
    {
        Sceneries.Clear();
        foreach (string file in EnumerateScenery(sceneryDir))
        {
            try { Sceneries.Add(new Scenery(file)); }
            catch { /* skip */ }
        }
    }

    private static List<string> EnumerateScenery(string sceneryDir)
    {
        if (!Directory.Exists(sceneryDir))
            return new List<string>();

        return Directory.GetFiles(sceneryDir, "*.scn")
            .Where(p => !Path.GetFileName(p).StartsWith("$"))
            .ToList();
    }
}
