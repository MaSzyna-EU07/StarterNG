using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StarterNG.Classes;

public enum LoadPhase
{
    Vehicles,
    Sceneries,
    Done
}

public readonly record struct LoadStatus(double Fraction, LoadPhase Phase, string? Detail);

public sealed class GameData
{
    public static GameData Instance { get; } = new();

    public VehicleDatabase Vehicles { get; } = new();
    public List<Scenery> Sceneries { get; } = new();

    public bool Loaded { get; private set; }

    public void Load(IProgress<LoadStatus>? progress = null,
                     string sceneryDir = "scenery/",
                     string miniDir = "textures/mini/",
                     string dynamicRoot = "dynamic")
    {
        if (Loaded)
            return;

        var scnFiles = EnumerateScenery(sceneryDir);
        int total = Math.Max(1, scnFiles.Count);
        int done = 0;

        progress?.Report(new LoadStatus(0, LoadPhase.Vehicles, "textures.txt"));
        int vehicles = Vehicles.LoadFromTexturesTxt(dynamicRoot);
        if (vehicles == 0)
            Infrastructure.Diagnostics.Log($"{dynamicRoot}: brak plikow textures.txt");

        Parallel.Invoke(
            () => VehicleDatabase.PreloadMiniIndex(miniDir),
            () => Physics.PreloadIndex());

        var sceneryLock = new object();
        var doneCount = done;
        Parallel.ForEach(scnFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                try
                {
                    var scenery = new Scenery(file);
                    lock (sceneryLock)
                    {
                        Sceneries.Add(scenery);
                        doneCount++;
                        progress?.Report(new LoadStatus((double)doneCount / total, LoadPhase.Sceneries,
                            Path.GetFileName(file)));
                    }
                }
                catch (Exception ex)
                {
                    StarterNG.Infrastructure.Diagnostics.Log($"scenery/{Path.GetFileName(file)}", ex);
                }
            });
        done = doneCount;

        progress?.Report(new LoadStatus(1.0, LoadPhase.Done, null));
        Loaded = true;
    }

    public void ReloadSceneries(string sceneryDir = "scenery/")
    {
        Sceneries.Clear();
        var scnFiles = EnumerateScenery(sceneryDir);
        var sceneryLock = new object();
        Parallel.ForEach(scnFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                try
                {
                    var scenery = new Scenery(file);
                    lock (sceneryLock)
                        Sceneries.Add(scenery);
                }
                catch (Exception ex)
                {
                    StarterNG.Infrastructure.Diagnostics.Log($"scenery/{Path.GetFileName(file)}", ex);
                }
            });
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
