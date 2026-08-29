using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Classes;

namespace StarterNG.Domain;

public static class TrainsetDisplay
{
    public static bool IsEmpty(Trainset trainset) =>
        trainset.Vehicles.Count == 0 ||
        trainset.Vehicles.All(v => string.IsNullOrWhiteSpace(v.Name) && string.IsNullOrWhiteSpace(v.SkinFile));

    public static string? CompositionText(Trainset trainset)
    {
        if (IsEmpty(trainset))
            return null;

        var db = GameData.Instance.Vehicles;
        var sb = new StringBuilder();
        string? prevKey = null;
        int run = 0;

        void Flush()
        {
            if (prevKey is null || run == 0) return;
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(run > 1 ? $"{prevKey}({run})" : prevKey);
            prevKey = null;
            run = 0;
        }

        for (int i = 0; i < trainset.Vehicles.Count; i++)
        {
            var v = trainset.Vehicles[i];
            var tex = db.TextureForSkin(v.SkinFile);
            string cat = tex?.ResolvedCategory ?? "";
            bool powered = cat is "e" or "s" or "p" or "z" or "a";

            if (i == 0 && powered)
            {
                string head = !string.IsNullOrWhiteSpace(v.Name) ? v.Name! :
                    (!string.IsNullOrEmpty(tex?.ResolvedClass) ? tex!.ResolvedClass : BaseSkin(v));
                sb.Append(head);
                prevKey = null;
                run = 0;
                continue;
            }

            string key = !string.IsNullOrEmpty(tex?.ResolvedClass) ? tex!.ResolvedClass
                : (!string.IsNullOrWhiteSpace(v.Name) ? v.Name! : BaseSkin(v));

            if (string.Equals(key, prevKey, StringComparison.OrdinalIgnoreCase))
            {
                run++;
            }
            else
            {
                Flush();
                prevKey = key;
                run = 1;
            }
        }
        Flush();
        return sb.Length > 0 ? sb.ToString() : string.Join(" + ", trainset.Vehicles.Select(v =>
            !string.IsNullOrWhiteSpace(v.Name) ? v.Name! : BaseSkin(v)));
    }

    private static string BaseSkin(Dynamic v) =>
        string.IsNullOrWhiteSpace(v.SkinFile) ? "?" :
        System.IO.Path.GetFileNameWithoutExtension(v.SkinFile);

    public static string? DefaultStartingVehicle(Trainset trainset)
    {
        if (trainset.Vehicles.Count == 0)
            return null;

        var last = trainset.Vehicles[^1];
        if (last.DriverType is eDriverType.Headdriver or eDriverType.Reardriver)
            return last.Name;

        var staffed = trainset.Vehicles.FirstOrDefault(v =>
            v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger);
        return staffed?.Name ?? trainset.Vehicles[0].Name;
    }

    public static string FormatStats(Trainset? trainset, Func<Dynamic, Physics?> physicsFor,
        string lengthLabel, string massLabel, string vehiclesLabel, string trackLabel,
        Func<Dynamic, string?>? categoryOf = null,
        Func<string?, int>? loadWeightKg = null)
    {
        if (trainset is null)
            return "";

        var (lengthM, massKg, loadKg) = RecalcParams(trainset, physicsFor, categoryOf, loadWeightKg, allVehicles: false);
        if (massKg < 200_000)
            (lengthM, massKg, loadKg) = RecalcParams(trainset, physicsFor, categoryOf, loadWeightKg, allVehicles: true);

        var inv = CultureInfo.InvariantCulture;
        string track = trainset.Track ?? "";
        if (trainset.Velocity > 0)
            track = $"{track} ({trainset.Velocity.ToString("0.#", inv)} km/h)";

        string massPart = $"{massLabel}: {(massKg / 1000.0).ToString("0.#", inv)} t";
        if (loadKg > 0)
            massPart += $" / {((massKg + loadKg) / 1000.0).ToString("0.#", inv)} t";

        return $"{lengthLabel}: {lengthM.ToString("0.#", inv)} m    ·    " +
               $"{massPart}    ·    " +
               $"{vehiclesLabel}: {trainset.Vehicles.Count}    ·    " +
               $"{trackLabel}: {track}";
    }

    private static (double lengthM, double massKg, double loadKg) RecalcParams(
        Trainset trainset,
        Func<Dynamic, Physics?> physicsFor,
        Func<Dynamic, string?>? categoryOf,
        Func<string?, int>? loadWeightKg,
        bool allVehicles)
    {
        double lengthM = 0, massKg = 0, loadKg = 0;
        foreach (var car in trainset.Vehicles)
        {
            var p = physicsFor(car);
            if (p != null)
                lengthM += p.Length;

            if (!IncludeVehicleToMass(car, p, categoryOf?.Invoke(car), allVehicles))
                continue;

            if (p != null)
                massKg += p.Mass;

            if (loadWeightKg != null &&
                car.LoadCount > 0 &&
                !string.IsNullOrEmpty(car.LoadType) &&
                LoadAccepted(p, car.LoadType))
                loadKg += (double)car.LoadCount * loadWeightKg(car.LoadType);
        }
        return (lengthM, massKg, loadKg);
    }

    private static bool IncludeVehicleToMass(Dynamic car, Physics? p, string? category, bool allVehicles)
    {
        if (p == null) return false;
        if (allVehicles) return true;
        if (category is "z" or "a") return true;
        return car.DriverType is eDriverType.Passenger or eDriverType.Nobody;
    }

    private static bool LoadAccepted(Physics? p, string loadType)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.LoadAccepted))
            return true;
        return p.LoadAccepted.Contains(loadType, StringComparison.OrdinalIgnoreCase);
    }

    public static string? UniquifyForLaunch(Trainset trainset, Scenery scenery, string? startName)
    {
        var used = CollectNames(scenery);
        var db = GameData.Instance.Vehicles;

        bool mu = trainset.Vehicles.Any(v =>
        {
            string? cat = db.TextureForSkin(v.SkinFile)?.ResolvedCategory;
            return cat is "z" or "a";
        });

        Dynamic? target = null;
        if (!string.IsNullOrEmpty(startName))
            target = trainset.Vehicles.FirstOrDefault(v =>
                string.Equals(v.Name, startName, StringComparison.OrdinalIgnoreCase));
        target ??= trainset.Vehicles.FirstOrDefault(v =>
            v.DriverType is eDriverType.Headdriver or eDriverType.Reardriver or eDriverType.Passenger);
        target ??= trainset.Vehicles.FirstOrDefault();

        if (mu)
        {
            foreach (var v in trainset.Vehicles)
            {
                if (!string.IsNullOrEmpty(v.Name))
                    used.Remove(v.Name!);
                v.Name = EnsureUnique(PreferredBaseName(v), used);
            }
            return target?.Name;
        }

        if (target != null)
        {
            if (!string.IsNullOrEmpty(target.Name))
                used.Remove(target.Name!);
            target.Name = EnsureUnique(PreferredBaseName(target), used);
        }
        return target?.Name;
    }

    public static string PreferredBaseName(Dynamic v)
    {
        var tex = GameData.Instance.Vehicles.TextureForSkin(v.SkinFile);
        if (tex != null)
        {

            if (!string.IsNullOrEmpty(tex.TextureMini) &&
                !string.Equals(tex.TextureMini, tex.ResolvedClass, StringComparison.OrdinalIgnoreCase))
                return SanitizeName(tex.TextureMini!);
            if (!string.IsNullOrEmpty(tex.Skinfile))
                return SanitizeName(Path.GetFileNameWithoutExtension(tex.Skinfile));
        }
        if (!string.IsNullOrWhiteSpace(v.SkinFile))
            return SanitizeName(Path.GetFileNameWithoutExtension(v.SkinFile));
        return SanitizeName(string.IsNullOrWhiteSpace(v.Name) ? "vehicle" : v.Name!);
    }

    public static string EnsureUnique(string baseName, HashSet<string> used)
    {
        baseName = SanitizeName(baseName);
        if (!used.Contains(baseName))
        {
            used.Add(baseName);
            return baseName;
        }
        int n = 1;
        string candidate;
        do
            candidate = $"{baseName}_{n++}";
        while (used.Contains(candidate));
        used.Add(candidate);
        return candidate;
    }

    public static HashSet<string> CollectNames(Scenery scenery, Trainset? except = null)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string n in scenery.LooseVehicleNames)
            used.Add(n);
        foreach (var d in scenery.LooseVehicles)
            if (!string.IsNullOrEmpty(d.Name))
                used.Add(d.Name!);
        foreach (var ts in scenery.Trainsets)
        {
            if (ReferenceEquals(ts, except)) continue;
            foreach (var v in ts.Vehicles)
                if (!string.IsNullOrEmpty(v.Name))
                    used.Add(v.Name!);
        }
        return used;
    }

    private static string SanitizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "vehicle" : name.Replace(' ', '_');
}
