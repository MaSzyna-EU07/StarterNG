using System;
using System.Collections.Generic;
using System.Linq;

using StarterNG.Classes;

namespace StarterNG.Domain;

public sealed class Cargo
{
    private readonly VehicleInfo _info;
    private readonly Random _rng;

    public Cargo(VehicleInfo info, Random rng)
    {
        _info = info;
        _rng = rng;
    }

    public bool IsLoadable(ConsistItem item) => IsLoadable(item.Cars[0]);

    public bool IsLoadable(Dynamic car)
    {
        if (_info.CategoryOf(car) is "e" or "s" or "p")
            return false;
        return !string.IsNullOrWhiteSpace(_info.PhysicsFor(car)?.LoadAccepted);
    }

    public int MaxFor(Dynamic car, string? type = null)
    {
        if (!IsLoadable(car)) return 0;

        if (Dynamic.IsPantStateType(type ?? car.LoadType)) return Dynamic.PantStateMax;

        if (car.MaxLoad >= 0) return car.MaxLoad;
        return _info.PhysicsFor(car)?.MaxLoad ?? 0;
    }

    public int LimitFor(Dynamic car, string? type = null)
    {
        int max = MaxFor(car, type);
        return max > 0 ? max : (IsLoadable(car) ? int.MaxValue : 0);
    }

    public List<string> AcceptedTypes(Dynamic car)
    {
        var p = _info.PhysicsFor(car);
        if (!IsLoadable(car) || p == null)
            return new List<string>();

        return p.LoadAccepted.Split(',', ';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Accepts(Dynamic car, string? type) =>
        !string.IsNullOrEmpty(type) &&
        AcceptedTypes(car).Contains(type!, StringComparer.OrdinalIgnoreCase);

    public void FillToMax(Dynamic car)
    {
        int max = MaxFor(car);
        if (max <= 0) return;
        if (string.IsNullOrEmpty(car.LoadType))
            car.LoadType = AcceptedTypes(car).FirstOrDefault();
        if (string.IsNullOrEmpty(car.LoadType)) return;
        car.LoadCount = max;
    }

    public void CopyFromPrevious(IReadOnlyList<ConsistItem> consist, ConsistItem item)
    {
        int idx = IndexOf(consist, item);
        if (idx <= 0) return;

        var source = consist[idx - 1].Cars[0];
        foreach (var c in item.Cars)
        {
            if (!Accepts(c, source.LoadType)) continue;
            c.LoadType = source.LoadType;
            c.LoadCount = Math.Min(source.LoadCount, LimitFor(c));
        }
    }

    public void FillUnit(ConsistItem item)
    {
        foreach (var c in item.Cars)
            FillToMax(c);
    }

    public void CopyToFollowing(IReadOnlyList<ConsistItem> consist, ConsistItem item)
    {
        int idx = IndexOf(consist, item);
        if (idx < 0) return;

        var lead = item.Cars[0];
        string? type = lead.LoadType;
        if (string.IsNullOrEmpty(type)) return;

        foreach (var c in consist.Skip(idx + 1).SelectMany(u => u.Cars))
        {
            if (!AcceptedTypes(c).Contains(type!, StringComparer.OrdinalIgnoreCase))
                continue;
            c.LoadType = type;
            c.LoadCount = Math.Min(lead.LoadCount, MaxFor(c));
        }
    }

    public void RandomTypes(IReadOnlyList<ConsistItem> consist)
    {
        foreach (var c in consist.SelectMany(u => u.Cars))
        {
            var types = AcceptedTypes(c);
            if (types.Count == 0) continue;
            c.LoadType = types[_rng.Next(types.Count)];
        }
    }

    public void RandomAmounts(IReadOnlyList<ConsistItem> consist)
    {
        foreach (var c in consist.SelectMany(u => u.Cars))
        {
            int max = MaxFor(c);
            if (max <= 0) continue;
            if (string.IsNullOrEmpty(c.LoadType))
                c.LoadType = AcceptedTypes(c).FirstOrDefault();
            if (string.IsNullOrEmpty(c.LoadType)) continue;
            int min = Math.Max(1, max / 10);
            c.LoadCount = _rng.Next(min, max + 1);
        }
    }

    private static int IndexOf(IReadOnlyList<ConsistItem> consist, ConsistItem item)
    {
        for (int i = 0; i < consist.Count; i++)
            if (ReferenceEquals(consist[i], item))
                return i;
        return -1;
    }
}
