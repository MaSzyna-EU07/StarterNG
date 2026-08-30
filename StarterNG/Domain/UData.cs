using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Classes;

namespace StarterNG.Domain;

public static class UData
{
    public readonly record struct TrainParams(double Length, double Mass, double LoadMass, int CountVehicles);

    public static int GetMaxCoupler(Dynamic v, Func<Dynamic, Physics?> physicsFor, bool leftCoupler = true)
    {
        var fiz = physicsFor(v);
        if (fiz == null) return 3;
        return leftCoupler
            ? (v.Offset >= 0 ? fiz.AllowedFlagA : fiz.AllowedFlagB)
            : (v.Offset >= 0 ? fiz.AllowedFlagB : fiz.AllowedFlagA);
    }

    public static string GetControlType(Dynamic v, Func<Dynamic, Physics?> physicsFor, bool leftCoupler = true)
    {
        var fiz = physicsFor(v);
        if (fiz == null) return "";
        return leftCoupler
            ? (v.Offset >= 0 ? fiz.ControlTypeA : fiz.ControlTypeB)
            : (v.Offset >= 0 ? fiz.ControlTypeB : fiz.ControlTypeA);
    }

    public static List<int> CheckFlag(int flag)
    {
        var result = new List<int>();
        for (int f = 128; f >= 1; f >>= 1)
            if (flag >= f) { result.Add(f); flag -= f; }
        return result;
    }

    public static int CommonCoupler(int c1, int c2)
    {
        var f1 = CheckFlag(c1);
        var f2 = CheckFlag(c2);
        return f1.Where(f2.Contains).Sum();
    }

    public static void AutoConnect(IList<Dynamic> vehicles, int leftVehicle,
        Func<Dynamic, Physics?> physicsFor)
    {
        if (leftVehicle < 0 || vehicles.Count - 1 <= leftVehicle) return;

        int leftMax = GetMaxCoupler(vehicles[leftVehicle], physicsFor, leftCoupler: false);
        string controlType1 = GetControlType(vehicles[leftVehicle], physicsFor, leftCoupler: false);

        int rightMax = GetMaxCoupler(vehicles[leftVehicle + 1], physicsFor);
        string controlType2 = GetControlType(vehicles[leftVehicle + 1], physicsFor);

        int coupler = CommonCoupler(leftMax, rightMax);

        const int F4 = 4;
        if (CheckFlag(coupler).Contains(F4) &&
            !string.Equals(controlType1, controlType2, StringComparison.OrdinalIgnoreCase))
            coupler -= F4;

        vehicles[leftVehicle].Coupling.Flags = coupler;
    }

    public static List<int> GetMultiple(IList<Dynamic> vehicles, int index,
        Func<Dynamic, string?> setIdOf)
    {
        var result = new List<int> { index };

        bool Same(int a, int b)
        {
            string? x = setIdOf(vehicles[a]);
            return x != null && x == setIdOf(vehicles[b]);
        }

        int i = 0;
        while (index - i > 0 && Same(index - i, index - i - 1))
        {
            result.Add(index - i - 1);
            i++;
        }

        i = 0;
        while (vehicles.Count - 1 > index + i && Same(index + i, index + i + 1))
        {
            result.Add(index + i + 1);
            i++;
        }
        return result;
    }

    public static bool StaffedTrain(Trainset train) =>
        train.Vehicles.Count > 0 &&
        (IsDriver(train.Vehicles[0].DriverType) || IsDriver(train.Vehicles[^1].DriverType));

    private static bool IsDriver(eDriverType d) =>
        d is eDriverType.Headdriver or eDriverType.Reardriver;

    public static bool IncludeVehicleToMass(Dynamic v, bool allVehicles,
        Func<Dynamic, Physics?> physicsFor, Func<Dynamic, string?> categoryOf)
    {
        if (physicsFor(v) == null) return false;
        if (allVehicles) return true;

        bool notDriver = v.DriverType is eDriverType.Passenger or eDriverType.Nobody;
        string? cat = categoryOf(v);
        return notDriver || cat is "z" or "a";
    }

    public static TrainParams RecalcTrainParams(Trainset? train, bool allVehicles,
        Func<Dynamic, Physics?> physicsFor, Func<Dynamic, string?> categoryOf,
        Func<string?, int> loadWeight)
    {
        double length = 0, mass = 0, loadMass = 0;
        if (train == null) return new TrainParams(0, 0, 0, 0);

        for (int i = train.Vehicles.Count - 1; i >= 0; i--)
        {
            var v = train.Vehicles[i];
            var fiz = physicsFor(v);

            if (IncludeVehicleToMass(v, allVehicles, physicsFor, categoryOf) && fiz != null)
            {
                mass += fiz.Mass;

                if (!string.IsNullOrEmpty(v.LoadType) &&
                    fiz.LoadAccepted.Contains(v.LoadType!, StringComparison.OrdinalIgnoreCase))
                    loadMass += v.LoadCount * loadWeight(v.LoadType);
                else if (!string.IsNullOrEmpty(v.LoadType))
                    Infrastructure.Diagnostics.Log($"{v.Name} - nieobsługiwany ładunek pojazdu");
            }

            if (fiz != null)
                length += fiz.Length;
        }
        return new TrainParams(length, mass, loadMass, train.Vehicles.Count);
    }

    public static void RandomLoad(IEnumerable<Dynamic> vehicles,
        Func<Dynamic, Physics?> physicsFor, Random rng)
    {
        foreach (var v in vehicles)
        {
            var fiz = physicsFor(v);
            if (fiz == null) continue;

            var cargo = fiz.LoadAccepted
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            if (cargo.Count > 0)
                v.LoadType = cargo[rng.Next(cargo.Count)];
        }
    }

    public static void ReverseMultiple(IList<Dynamic> vehicles, int position,
        Func<Dynamic, string?> setIdOf, Func<Dynamic, Physics?> physicsFor)
    {
        var indexes = GetMultiple(vehicles, position, setIdOf);

        foreach (int i in indexes)
            vehicles[i].Offset = vehicles[i].Offset >= 0 ? -1f : 0f;

        indexes.Sort();

        int first = indexes[0], last = indexes[^1];
        for (int i = 0; i < indexes.Count / 2; i++)
            (vehicles[first + i], vehicles[last - i]) = (vehicles[last - i], vehicles[first + i]);

        foreach (int i in indexes)
            AutoConnect(vehicles, i, physicsFor);
    }

    public static bool CheckMoveVehicle(IList<Dynamic> vehicles, int fromPos, int toPos,
        Func<Dynamic, string?> setIdOf)
    {
        bool Same(int a, int b)
        {
            if (a < 0 || b < 0 || a >= vehicles.Count || b >= vehicles.Count) return false;
            string? x = setIdOf(vehicles[a]);
            return x != null && x == setIdOf(vehicles[b]);
        }

        if (fromPos < toPos)
        {
            if (toPos > 0 && toPos < vehicles.Count - 1 && Same(toPos, toPos + 1))
                return false;
        }
        else if (toPos > 0 && Same(toPos, toPos - 1))
        {
            return false;
        }
        return true;
    }
}
