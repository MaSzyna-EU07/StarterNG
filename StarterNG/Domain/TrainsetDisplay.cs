using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;

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

            if (i == 0 && IsPowered(v))
            {
                sb.Append(VehicleLabel(v, head: true));
                prevKey = null;
                run = 0;
                continue;
            }

            string key = VehicleLabel(v);

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

    public static string VehicleLabel(Dynamic v, bool head = false)
    {
        var tex = GameData.Instance.Vehicles.TextureForSkin(v.SkinFile);
        string? cls = string.IsNullOrEmpty(tex?.ResolvedClass) ? null : tex!.ResolvedClass;
        string? name = string.IsNullOrWhiteSpace(v.Name) ? null : v.Name;
        return (head ? name ?? cls : cls ?? name) ?? BaseSkin(v);
    }

    public static bool IsPowered(Dynamic v) =>
        GameData.Instance.Vehicles.TextureForSkin(v.SkinFile)?.ResolvedCategory
            is "e" or "s" or "p" or "z" or "a";

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

    public static List<(string Label, string Value)> StatsFields(
        Trainset? trainset, Func<Dynamic, Physics?> physicsFor,
        Func<Dynamic, string?>? categoryOf = null,
        Func<string?, int>? loadWeightKg = null)
    {
        var fields = new List<(string, string)>();
        if (trainset is null)
            return fields;

        var (lengthM, massKg, loadKg) = RecalcParams(trainset, physicsFor, categoryOf, loadWeightKg, allVehicles: false);
        if (massKg < 200_000)
            (lengthM, massKg, loadKg) = RecalcParams(trainset, physicsFor, categoryOf, loadWeightKg, allVehicles: true);

        var inv = CultureInfo.InvariantCulture;

        string track = trainset.Track ?? "";
        if (trainset.Velocity > 0)
            track = $"{track} ({trainset.Velocity.ToString("0.#", inv)} km/h)";

        fields.Add(($"{App.Loc["Mass"]} [t]:", (massKg / 1000.0).ToString("0.#", inv)));
        fields.Add(($"{App.Loc["MassBrutto"]} [t]:", ((massKg + loadKg) / 1000.0).ToString("0.#", inv)));
        fields.Add(($"{App.Loc["Length"]} [m]:", lengthM.ToString("0.#", inv)));
        fields.Add(($"{App.Loc["VehicleCount"]}:", trainset.Vehicles.Count.ToString(inv)));
        fields.Add(($"{App.Loc["Track"]}:", track));
        return fields;
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
            return false;
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
                Rename(v, used);
            return target?.Name;
        }

        if (target != null)
            Rename(target, used);
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

    public static (string Base, string Suffix) SplitWagonNumber(string? name)
    {
        string n = name ?? "";
        int hash = n.LastIndexOf('#');
        if (hash >= 0 && hash + 1 < n.Length && n[(hash + 1)..].All(char.IsDigit))
            return (n[..hash], n[hash..]);
        return (n, "");
    }

    private static void Rename(Dynamic v, HashSet<string> used)
    {
        var (_, number) = SplitWagonNumber(v.Name);
        if (!string.IsNullOrEmpty(v.Name))
            used.Remove(v.Name!);
        v.Name = EnsureUnique(PreferredBaseName(v), used) + number;
    }
}
