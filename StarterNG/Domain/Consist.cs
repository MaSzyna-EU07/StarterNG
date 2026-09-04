using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using StarterNG.Classes;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Domain;

public sealed class Consist : IReadOnlyList<ConsistItem>
{
    private static readonly string[] CarSuffixes = { "sa", "sb", "ra", "rb", "s" };

    private readonly VehicleDatabase _db;
    private readonly VehicleInfo _info;
    private readonly List<ConsistItem> _items = new();
    private int _nameCounter;

    public Consist(VehicleDatabase db, VehicleInfo info)
    {
        _db = db;
        _info = info;
    }

    public event Action? Changed;

    public Trainset? EditingTrainset { get; set; }

    public ConsistItem? Selected { get; set; }

    public Dynamic? SelectedCar { get; set; }

    public int Count => _items.Count;

    public ConsistItem this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public IEnumerator<ConsistItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(ConsistItem item) => _items.IndexOf(item);

    public void Raise() => Changed?.Invoke();

    public void LoadFrom(Trainset trainset)
    {
        EditingTrainset = trainset;
        _items.Clear();

        var cars = trainset.Vehicles.Select(v =>
        {
            var c = v.Clone();
            c.MiniName = _db.MiniForSkin(c.SkinFile) ?? c.MiniName;
            return c;
        }).ToList();

        int i = 0;
        while (i < cars.Count)
        {
            if (HoldsNext(cars[i]))
            {
                var locked = new List<Dynamic> { cars[i] };
                int j = i;
                while (j < cars.Count - 1 && HoldsNext(cars[j]))
                    locked.Add(cars[++j]);

                _items.Add(new ConsistItem
                {
                    Cars = locked,
                    Grouped = true,
                    Flipped = locked[0].Offset < 0,
                    Driver = UnitDriver(locked)
                });
                i = j + 1;
                continue;
            }

            VehicleSet? set = null;
            if (_db.TextureForSkin(cars[i].SkinFile)?.Uuid is { } uuid)
                _db.SetByTextureUuid.TryGetValue(uuid, out set);

            if (set?.TextureRefs is { Count: > 1 })
            {
                var members = new HashSet<string>(set.TextureRefs.Where(r => !string.IsNullOrEmpty(r)));
                var group = new List<Dynamic>();

                int j = i;
                while (j < cars.Count && group.Count < set.TextureRefs.Count)
                {
                    if (_db.TextureForSkin(cars[j].SkinFile)?.Uuid is { } u
                        && members.Contains(u))
                        group.Add(cars[j++]);
                    else
                        break;
                }

                if (group.Count >= 2)
                {
                    _items.Add(new ConsistItem
                    {
                        Cars = group,
                        Grouped = true,
                        Flipped = group[0].Offset < 0,
                        Driver = UnitDriver(group)
                    });
                    i = j;
                    continue;
                }
            }

            if (IsUnitCar(cars[i]))
            {
                string key = UnitKey(cars[i]);
                var group = new List<Dynamic> { cars[i] };
                int j = i + 1;
                while (j < cars.Count && IsUnitCar(cars[j]) && UnitKey(cars[j]) == key)
                    group.Add(cars[j++]);

                if (group.Count >= 2)
                {
                    _items.Add(new ConsistItem
                    {
                        Cars = group,
                        Grouped = true,
                        Flipped = group[0].Offset < 0,
                        Driver = UnitDriver(group)
                    });
                    i = j;
                    continue;
                }
            }

            _items.Add(new ConsistItem
            {
                Cars = new List<Dynamic> { cars[i] },
                Grouped = false,
                Flipped = cars[i].Offset < 0,
                Driver = cars[i].DriverType
            });
            i++;
        }

        Selected = _items.FirstOrDefault();
        SyncStartingVehicle();
        Raise();
    }

    public List<Dynamic> Flatten()
    {
        var flat = new List<Dynamic>();
        foreach (var item in _items)
        {
            var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse().ToList() : item.Cars;
            foreach (var car in cars)
                flat.Add(car);
        }
        return flat;
    }

    public void SyncStartingVehicle()
    {
        if (Selected is not { Cars.Count: > 0 } item)
            return;

        var driven = item.Cars.FirstOrDefault(c =>
            c.DriverType is eDriverType.Headdriver or eDriverType.Reardriver);
        AppState.Instance.StartingVehicleName = (driven ?? item.Cars[0]).Name;
    }

    public Dynamic MakeDynamic(VehicleTexture texture, Scenery? scenery)
    {
        string skin = Base(texture.Skinfile);
        return new Dynamic
        {
            RangeMax = -1,
            RangeMin = 0,
            Name = MakeUniqueName(skin, scenery),
            DataFolder = StripDynamicPrefix(texture.Directory),
            SkinFile = skin,
            MmdFile = string.IsNullOrEmpty(texture.Model) ? skin : texture.Model!,
            Offset = 0f,
            DriverType = eDriverType.Nobody,
            Coupling = new Coupling { Flags = Coupling.Mechanical | Coupling.BrakePipe },
            HasVelocity = true,
            MiniName = _db.ResolveMiniName(texture)
        };
    }

    private string MakeUniqueName(string baseName, Scenery? scenery)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "vehicle";
        baseName = baseName.Replace(' ', '_');

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
            foreach (var c in item.Cars)
                if (!string.IsNullOrEmpty(c.Name))
                    used.Add(c.Name!);

        if (scenery != null)
        {
            foreach (string n in scenery.LooseVehicleNames)
                used.Add(n);
            foreach (var ts in scenery.Trainsets)
            {
                if (ReferenceEquals(ts, EditingTrainset)) continue;
                foreach (var v in ts.Vehicles)
                    if (!string.IsNullOrEmpty(v.Name))
                        used.Add(v.Name!);
            }
        }

        string candidate;
        do
            candidate = $"{baseName}_{++_nameCounter}";
        while (used.Contains(candidate));
        return candidate;
    }

    private static string StripDynamicPrefix(string directory)
    {
        string d = directory.Replace('\\', '/').TrimEnd('/');
        const string prefix = "dynamic/";
        if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            d = d[prefix.Length..];
        return d;
    }

    public void Insert(int at, ConsistItem item) =>
        _items.Insert(Math.Clamp(at, 0, _items.Count), item);

    public void MoveLeft(ConsistItem item)
    {
        int i = _items.IndexOf(item);
        if (i > 0)
        {
            (_items[i - 1], _items[i]) = (_items[i], _items[i - 1]);
            Raise();
        }
    }

    public void MoveRight(ConsistItem item)
    {
        int i = _items.IndexOf(item);
        if (i >= 0 && i < _items.Count - 1)
        {
            (_items[i + 1], _items[i]) = (_items[i], _items[i + 1]);
            Raise();
        }
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count) return;

        var item = _items[from];
        _items.RemoveAt(from);
        to = Math.Clamp(to, 0, _items.Count);
        _items.Insert(to, item);

        Selected = item;
        AutoConnectAll();
        Raise();
    }

    public void Remove(ConsistItem item)
    {
        int gap = _items.IndexOf(item);
        _items.Remove(item);

        if (ReferenceEquals(Selected, item))
            Selected = _items.Count == 0
                ? null
                : _items[Math.Clamp(gap, 0, _items.Count - 1)];

        Raise();
    }

    public void Flip(ConsistItem item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return;

        int first = index;
        while (first > 0 && HoldsTail(_items[first - 1]))
            first--;

        int last = index;
        while (last + 1 < _items.Count && HoldsTail(_items[last]))
            last++;

        for (int k = first; k <= last; k++)
        {
            var card = _items[k];
            card.Flipped = !card.Flipped;
            foreach (var car in card.Cars)
                car.Offset = car.Offset >= 0 ? -1f : 0f;
        }

        if (last > first)
            _items.Reverse(first, last - first + 1);

        AutoConnectAll();
        Raise();
    }

    public void CycleDriver(ConsistItem item)
    {
        var car = ActiveCar(item);
        car.DriverType = car.DriverType switch
        {
            eDriverType.Nobody => eDriverType.Headdriver,
            eDriverType.Headdriver => eDriverType.Reardriver,
            eDriverType.Reardriver => eDriverType.Passenger,
            _ => eDriverType.Nobody
        };
        item.Driver = UnitDriver(item.Cars);
        if (ReferenceEquals(Selected, item))
            SyncStartingVehicle();
        Raise();
        AppState.Instance.NotifyChanged();
    }

    public void Split(ConsistItem item)
    {
        int i = _items.IndexOf(item);
        if (i < 0) return;

        var order = item.Flipped
            ? Enumerable.Reverse(item.Cars).ToList()
            : item.Cars;

        _items.RemoveAt(i);
        for (int c = 0; c < order.Count; c++)
        {
            _items.Insert(i + c, new ConsistItem
            {
                Cars = new List<Dynamic> { order[c] },
                Grouped = false,
                Flipped = item.Flipped,
                Driver = order[c].DriverType
            });
        }
        Selected = _items[i];
        Raise();
    }

    public void Join(ConsistItem item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return;

        int first = index;
        while (first > 0 && HoldsTail(_items[first - 1]))
            first--;

        int last = index;

        bool inUnit = first < index || HoldsTail(_items[index]);
        if (!inUnit && last + 1 < _items.Count)
        {
            var card = _items[last];
            if (card.Cars.Count > 0)
                TailCar(card).Coupling.Set(Coupling.WorkshopLock, true);
            last++;
        }

        while (last + 1 < _items.Count && HoldsTail(_items[last]))
            last++;

        if (last == first) return;

        var cars = new List<Dynamic>();
        var driver = eDriverType.Nobody;
        for (int k = first; k <= last; k++)
        {
            cars.AddRange(_items[k].Cars);
            if (driver == eDriverType.Nobody)
                driver = _items[k].Driver;
        }

        bool flipped = _items[first].Flipped;
        if (flipped)
            cars.Reverse();

        var joined = new ConsistItem
        {
            Cars = cars,
            Grouped = true,
            Flipped = flipped,
            Driver = driver
        };

        _items.RemoveRange(first, last - first + 1);
        _items.Insert(first, joined);
        Selected = joined;

        AutoConnectAll();
        Raise();
    }

    public void RandomizeOrder(Random rng)
    {
        int start = Selected != null ? _items.IndexOf(Selected) : 0;
        if (start < 0) start = 0;

        var indices = new List<int>();
        for (int i = start; i < _items.Count; i++)
        {
            if (_items[i].Grouped) continue;
            string? cat = _info.CategoryOf(_items[i].Cars[0]);
            if (cat is { Length: 1 } && char.IsUpper(cat[0]))
                indices.Add(i);
        }

        if (indices.Count < 2) return;

        var items = indices.Select(i => _items[i]).ToList();
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        for (int k = 0; k < indices.Count; k++)
            _items[indices[k]] = items[k];

        AutoConnectAll();
        Raise();
    }

    public void RandomizeOrientation(Random rng)
    {
        int start = Selected != null ? _items.IndexOf(Selected) : 0;
        if (start < 0) start = 0;
        if (start >= _items.Count) return;

        for (int i = start; i < _items.Count; i++)
            if (rng.Next(2) == 0)
                _items[i].Flipped = !_items[i].Flipped;

        AutoConnectAll();
        Raise();
    }

    public void AutoConnectAll()
    {
        var flat = new List<(Dynamic car, bool flipped)>();
        foreach (var item in _items)
        {
            var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse() : item.Cars;
            foreach (var c in cars)
                flat.Add((c, item.Flipped));
        }

        for (int i = 0; i < flat.Count - 1; i++)
        {
            var (left, lf) = flat[i];
            var (right, rf) = flat[i + 1];

            var lp = _info.PhysicsFor(left);
            var rp = _info.PhysicsFor(right);

            int leftMax = lp == null ? 3 : (lf ? lp.AllowedFlagA : lp.AllowedFlagB);
            int rightMax = rp == null ? 3 : (rf ? rp.AllowedFlagB : rp.AllowedFlagA);
            int common = leftMax & rightMax;

            string lct = lp == null ? "" : (lf ? lp.ControlTypeA : lp.ControlTypeB);
            string rct = rp == null ? "" : (rf ? rp.ControlTypeB : rp.ControlTypeA);
            if ((common & Coupling.ControlMU) != 0 &&
                !string.Equals(lct, rct, StringComparison.OrdinalIgnoreCase))
                common &= ~Coupling.ControlMU;

            left.Coupling.Flags = left.Coupling.Locked ? -common : common;
        }
    }

    public bool CanFormUnit(ConsistItem left, ConsistItem right)
    {
        if (left.Cars.Count == 0 || right.Cars.Count == 0)
            return false;

        var tail = TailCar(left);
        var head = HeadCar(right);

        var lp = _info.PhysicsFor(tail);
        var rp = _info.PhysicsFor(head);
        int leftMax = lp == null ? 3 : (left.Flipped ? lp.AllowedFlagA : lp.AllowedFlagB);
        int rightMax = rp == null ? 3 : (right.Flipped ? rp.AllowedFlagB : rp.AllowedFlagA);

        return (leftMax & rightMax & Coupling.WorkshopLock) != 0 || SameSet(tail, head);
    }

    public bool SameSet(Dynamic a, Dynamic b)
    {
        if (IsUnitCar(a) && IsUnitCar(b) && UnitKey(a) == UnitKey(b))
            return true;

        string? ua = _db.TextureForSkin(a.SkinFile)?.Uuid;
        string? ub = _db.TextureForSkin(b.SkinFile)?.Uuid;
        if (string.IsNullOrEmpty(ua) || string.IsNullOrEmpty(ub))
            return false;

        return _db.SetByTextureUuid.TryGetValue(ua!, out var sa) &&
               _db.SetByTextureUuid.TryGetValue(ub!, out var sb) &&
               ReferenceEquals(sa, sb) && sa.TextureRefs.Count > 1;
    }

    public Dynamic ActiveCar(ConsistItem item) =>
        SelectedCar != null && item.Cars.Contains(SelectedCar) ? SelectedCar : item.Cars[0];

    public static IEnumerable<Dynamic> UnitTargets(ConsistItem item) => item.Cars;

    public IEnumerable<Dynamic> MemberTargets(ConsistItem item) =>
        item.Cars.Count == 1 ? item.Cars : new[] { ActiveCar(item) };

    public static Dynamic TailCar(ConsistItem item) =>
        item.Flipped ? item.Cars[0] : item.Cars[^1];

    public static Dynamic HeadCar(ConsistItem item) =>
        item.Flipped ? item.Cars[^1] : item.Cars[0];

    public static bool HoldsTail(ConsistItem item) =>
        item.Cars.Count > 0 && HoldsNext(TailCar(item));

    public static bool HoldsNext(Dynamic car) =>
        car.Coupling.Has(Coupling.WorkshopLock) || car.Coupling.Locked;

    public static eDriverType UnitDriver(IReadOnlyList<Dynamic> group) =>
        group.Select(c => c.DriverType)
            .Where(d => d != eDriverType.Nobody)
            .DefaultIfEmpty(eDriverType.Nobody)
            .First();

    public static string Base(string skinOrName) => System.IO.Path.GetFileNameWithoutExtension(skinOrName);

    public static string UnitKey(Dynamic d) => StripCarSuffix(Base(d.SkinFile));

    public static bool IsUnitCar(Dynamic d) => UnitKey(d) != Base(d.SkinFile);

    private static string StripCarSuffix(string name)
    {
        foreach (string suf in CarSuffixes)
        {
            if (name.Length > suf.Length &&
                name.EndsWith(suf, StringComparison.OrdinalIgnoreCase) &&
                char.IsDigit(name[name.Length - suf.Length - 1]))
                return name[..^suf.Length];
        }
        return name;
    }

    public static string UnitLabel(ConsistItem item)
    {
        string key = UnitKey(item.Cars[0]);
        return item.Cars.Count > 1 ? $"{key}  [{item.Cars.Count}]" : key;
    }

    public static int GetWagonNumber(Dynamic car)
    {
        string name = car.Name ?? "";
        int hash = name.LastIndexOf('#');
        if (hash >= 0 && hash + 1 < name.Length && name[(hash + 1)..].All(char.IsDigit)
            && int.TryParse(name[(hash + 1)..], out int n))
            return n;
        return 0;
    }

    public void SetWagonNumber(Dynamic car, int number)
    {
        string name = car.Name ?? "";
        int hash = name.LastIndexOf('#');
        string baseName = (hash >= 0 && hash + 1 < name.Length && name[(hash + 1)..].All(char.IsDigit))
            ? name[..hash]
            : name;
        car.Name = number > 0 ? $"{baseName}#{number}" : baseName;
        Raise();
    }
}
