using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Eu07.BlackBox;

/// <summary>A span of time, start included, end not.</summary>
public readonly record struct TimeSpanRange(double Start, double End)
{
    public double Length => End - Start;
    public bool Contains(double t) => t >= Start && t < End;
}

/// <summary>
/// What a black box holds, as far as it has been read: the map, the vehicles and what
/// happened to them over the time of the recording, which runs from 0 in seconds of
/// simulation. The lists grow as more of a file still written is read; the queries
/// answer for any moment of the time read so far.
/// </summary>
public sealed class Recording
{
    private readonly List<MapTrack> _tracks = new();
    private readonly Dictionary<int, List<MapTrack>> _trackIndex = new();
    private readonly List<MapSignal> _signals = new();
    private readonly Dictionary<int, MapSignal> _signalIndex = new();
    private readonly Dictionary<int, VehicleInfo> _vehicles = new();
    private readonly List<CabSample> _cab = new();
    private readonly List<VehicleLook> _looks = new();
    private readonly Dictionary<int, List<(double Time, VehicleState State)>> _history = new();
    private readonly List<SwitchChange> _switches = new();
    private readonly Dictionary<int, List<SwitchChange>> _switchIndex = new();
    private readonly List<AspectChange> _aspects = new();
    private readonly Dictionary<int, List<AspectChange>> _aspectIndex = new();
    private readonly List<CommandEntry> _commands = new();
    private readonly List<DriverChange> _driverChanges = new();
    private readonly List<SignalPassing> _passings = new();
    private readonly List<UserVehicleChange> _userVehicles = new();
    private readonly List<ClockEntry> _clocks = new();
    private readonly List<MapStopPoint> _stopPoints = new();
    private readonly List<MapHornPoint> _hornPoints = new();
    private readonly List<Timetable> _timetables = new();
    private readonly List<StationEvent> _stations = new();
    private readonly List<HornRequest> _hornRequests = new();
    private readonly List<CameraState> _cameras = new();
    private readonly List<CouplingChange> _couplings = new();
    private double _lastTime;

    public int Version { get; internal set; }
    public RecordingInfo? Info { get; internal set; }

    /// <summary>The time of the end record: null while the simulator still writes, or if it didn't end the recording.</summary>
    public double? EndTime { get; internal set; }

    public bool Complete => EndTime is not null;

    /// <summary>Records of types this reader doesn't know, passed over.</summary>
    public int Unknown { get; internal set; }

    /// <summary>Records shorter than their type says, passed over.</summary>
    public int Malformed { get; internal set; }

    /// <summary>Goes up with every read that added something, for views to tell they have to look again.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<MapTrack> Tracks => _tracks;
    public IReadOnlyList<MapSignal> Signals => _signals;
    public IReadOnlyDictionary<int, VehicleInfo> Vehicles => _vehicles;
    public IReadOnlyList<CabSample> Cab => _cab;
    public IReadOnlyList<VehicleLook> Looks => _looks;
    public IReadOnlyList<SwitchChange> Switches => _switches;
    public IReadOnlyList<AspectChange> Aspects => _aspects;
    public IReadOnlyList<CommandEntry> Commands => _commands;
    public IReadOnlyList<DriverChange> DriverChanges => _driverChanges;
    public IReadOnlyList<SignalPassing> Passings => _passings;
    public IReadOnlyList<UserVehicleChange> UserVehicles => _userVehicles;
    public IReadOnlyList<ClockEntry> Clocks => _clocks;
    public IReadOnlyList<MapStopPoint> StopPoints => _stopPoints;
    public IReadOnlyList<MapHornPoint> HornPoints => _hornPoints;
    public IReadOnlyList<Timetable> Timetables => _timetables;
    public IReadOnlyList<StationEvent> Stations => _stations;
    public IReadOnlyList<HornRequest> HornRequests => _hornRequests;
    public IReadOnlyList<CameraState> Cameras => _cameras;
    public IReadOnlyList<CouplingChange> Couplings => _couplings;

    /// <summary>The time the recording covers so far.</summary>
    public double Duration => Math.Max(EndTime ?? 0, _lastTime);

    /// <summary>The seconds between two looks at the vehicles.</summary>
    public double LookInterval => Info is { VehiclesInterval: > 0 } info ? info.VehiclesInterval : 1.0;

    // ----- filled by the reader -----

    internal void Changed()
    {
        ++Revision;
        _memo.Clear();
        _consists.Clear();
    }

    // What the questions about the user's train worked out, kept until a read adds something: the views ask them every frame.
    private readonly Dictionary<object, object> _memo = new();
    private readonly Dictionary<(int Vehicle, int Coupling), IReadOnlySet<int>> _consists = new();
    private static readonly object UserStationsKey = new(), UserPassingsKey = new(), UserHornChecksKey = new(), UserCouplingsKey = new();

    /// <summary>What make gives, worked out once until the recording changes.</summary>
    internal T Memo<T>(object key, Func<T> make) where T : class
    {
        if (_memo.TryGetValue(key, out var kept))
            return (T)kept;
        var made = make();
        _memo[key] = made;
        return made;
    }

    private void Saw(double time) => _lastTime = Math.Max(_lastTime, time);

    internal void AddTrack(MapTrack track)
    {
        _tracks.Add(track);
        if (!_trackIndex.TryGetValue(track.Index, out var ways))
            _trackIndex[track.Index] = ways = new List<MapTrack>(1);
        ways.Add(track);
    }

    internal void AddSignal(MapSignal signal)
    {
        _signals.Add(signal);
        _signalIndex[signal.Index] = signal;
    }

    internal void AddVehicle(VehicleInfo vehicle) => _vehicles[vehicle.Id] = vehicle;

    internal void RemoveVehicle(double time, int id)
    {
        Saw(time);
        if (_vehicles.TryGetValue(id, out var vehicle))
            vehicle.RemovedAt = time;
    }

    internal void AddCab(CabSample sample)
    {
        Saw(sample.Time);
        _cab.Add(sample);
    }

    internal void AddLook(VehicleLook look)
    {
        Saw(look.Time);
        _looks.Add(look);
        foreach (var state in look.States)
        {
            if (!_history.TryGetValue(state.Id, out var history))
                _history[state.Id] = history = new List<(double, VehicleState)>();
            history.Add((look.Time, state));
        }
    }

    internal void AddSwitch(SwitchChange change)
    {
        Saw(change.Time);
        _switches.Add(change);
        if (!_switchIndex.TryGetValue(change.Track, out var list))
            _switchIndex[change.Track] = list = new List<SwitchChange>();
        list.Add(change);
    }

    internal void AddAspect(AspectChange change)
    {
        Saw(change.Time);
        _aspects.Add(change);
        if (!_aspectIndex.TryGetValue(change.Signal, out var list))
            _aspectIndex[change.Signal] = list = new List<AspectChange>();
        list.Add(change);
    }

    internal void AddCommand(CommandEntry command)
    {
        Saw(command.Time);
        _commands.Add(command);
    }

    internal void AddDriverChange(DriverChange change)
    {
        Saw(change.Time);
        _driverChanges.Add(change);
    }

    internal void AddPassing(SignalPassing passing)
    {
        Saw(passing.Time);
        _passings.Add(passing);
    }

    internal void AddUserVehicle(UserVehicleChange change)
    {
        Saw(change.Time);
        _userVehicles.Add(change);
    }

    internal void AddClock(ClockEntry clock)
    {
        Saw(clock.Time);
        _clocks.Add(clock);
    }

    internal void AddStopPoint(MapStopPoint point) => _stopPoints.Add(point);

    internal void AddHornPoint(MapHornPoint point) => _hornPoints.Add(point);

    internal void AddTimetable(Timetable timetable)
    {
        Saw(timetable.Time);
        _timetables.Add(timetable);
    }

    internal void AddStation(StationEvent station)
    {
        Saw(station.Time);
        _stations.Add(station);
    }

    internal void AddCamera(CameraState camera)
    {
        Saw(camera.Time);
        _cameras.Add(camera);
    }

    internal void AddCoupling(CouplingChange change)
    {
        Saw(change.Time);
        _couplings.Add(change);
    }

    internal void AddHornRequest(HornRequest request)
    {
        Saw(request.Time);
        _hornRequests.Add(request);
    }

    // ----- queries -----

    public IReadOnlyList<MapTrack> TrackWays(int index) =>
        _trackIndex.TryGetValue(index, out var ways) ? ways : Array.Empty<MapTrack>();

    public MapSignal? Signal(int index) => _signalIndex.GetValueOrDefault(index);

    public VehicleInfo? Vehicle(int id) => _vehicles.GetValueOrDefault(id);

    /// <summary>The last of a list sorted by time at or before t: -1 if none is.</summary>
    private static int LastAtOrBefore<T>(IReadOnlyList<T> list, double t, Func<T, double> time)
    {
        int low = 0, high = list.Count - 1, found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (time(list[middle]) <= t)
            {
                found = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        return found;
    }

    /// <summary>The index of the sample of the user's train at or before t, -1 before the first.</summary>
    public int CabIndexAt(double t) => LastAtOrBefore(_cab, t, s => s.Time);

    /// <summary>The sample of the user's train at or before t.</summary>
    public CabSample? CabAt(double t) => CabIndexAt(t) is var i and >= 0 ? _cab[i] : null;

    /// <summary>
    /// Where the user's train was at t, between the two samples around it. Null before
    /// the first sample, and in a gap: the user out of any vehicle.
    /// </summary>
    public Position? CabPositionAt(double t)
    {
        int i = CabIndexAt(t);
        if (i < 0)
            return null;
        var before = _cab[i];
        if (i + 1 >= _cab.Count)
            return t - before.Time <= 1.0 ? before.Position : null;
        var after = _cab[i + 1];
        if (after.Vehicle != before.Vehicle || after.Time - before.Time > 1.0)
            return t - before.Time <= 1.0 ? before.Position : null;
        double f = (t - before.Time) / Math.Max(1e-9, after.Time - before.Time);
        return Position.Lerp(before.Position, after.Position, Math.Clamp(f, 0, 1));
    }

    /// <summary>
    /// Every vehicle on the scenery at t, where it was. A vehicle is listed only when it
    /// moved, so between two listings far apart it stood at the first place until the
    /// look before the second.
    /// </summary>
    public IReadOnlyList<VehicleState> VehiclesAt(double t)
    {
        var states = new List<VehicleState>(_history.Count);
        double interval = LookInterval;
        foreach (var (id, history) in _history)
        {
            if (_vehicles.TryGetValue(id, out var info) && info.RemovedAt is { } removed && removed <= t)
                continue;
            int i = LastAtOrBefore(history, t, entry => entry.Time);
            if (i < 0)
                continue;
            var (time, state) = history[i];
            if (i + 1 < history.Count)
            {
                var (nextTime, next) = history[i + 1];
                double from = Math.Max(time, nextTime - interval * 1.5);
                if (t > from)
                    state = Lerp(state, next, (t - from) / Math.Max(1e-9, nextTime - from));
            }
            states.Add(state);
        }
        return states;
    }

    private static VehicleState Lerp(VehicleState a, VehicleState b, double f)
    {
        float k = (float)Math.Clamp(f, 0, 1);
        float turn = b.Heading - a.Heading;
        while (turn > MathF.PI) turn -= 2 * MathF.PI;
        while (turn < -MathF.PI) turn += 2 * MathF.PI;
        return a with
        {
            Position = Vector3.Lerp(a.Position, b.Position, k),
            Heading = a.Heading + turn * k,
            SpeedKmh = a.SpeedKmh + (b.SpeedKmh - a.SpeedKmh) * k,
            Driving = k < 0.5f ? a.Driving : b.Driving
        };
    }

    /// <summary>The aspect a signal showed at t, null before it was first seen.</summary>
    public AspectChange? AspectAt(int signal, double t) =>
        _aspectIndex.TryGetValue(signal, out var list) && LastAtOrBefore(list, t, c => c.Time) is var i and >= 0
            ? list[i]
            : null;

    /// <summary>The position of a switch at t, -1 if unknown.</summary>
    public int SwitchAt(int track, double t) =>
        _switchIndex.TryGetValue(track, out var list) && LastAtOrBefore(list, t, c => c.Time) is var i and >= 0
            ? list[i].State
            : -1;

    /// <summary>The time of day of the simulation at t.</summary>
    public TimeSpan ClockAt(double t)
    {
        int i = LastAtOrBefore(_clocks, t, c => c.Time);
        double second = i >= 0
            ? _clocks[i].SecondOfDay + (t - _clocks[i].Time)
            : (Info?.SecondOfDay ?? 0) + t;
        second %= 86400;
        if (second < 0)
            second += 86400;
        return TimeSpan.FromSeconds(second);
    }

    /// <summary>The vehicle the user was in at t, and whether in its cab; null before it was known.</summary>
    public UserVehicleChange? UserVehicleAt(double t) =>
        LastAtOrBefore(_userVehicles, t, c => c.Time) is var i and >= 0 ? _userVehicles[i] : null;

    /// <summary>The stretches of the samples of the user's train the condition holds over.</summary>
    public IReadOnlyList<TimeSpanRange> CabSpans(Func<CabSample, bool> condition)
    {
        var spans = new List<TimeSpanRange>();
        double? start = null;
        double gap = (Info is { CabInterval: > 0 } info ? info.CabInterval : 0.1) * 5;
        for (int i = 0; i < _cab.Count; i++)
        {
            var sample = _cab[i];
            bool holds = condition(sample);
            bool broken = i > 0 && sample.Time - _cab[i - 1].Time > gap;
            if (start is { } open && (!holds || broken))
            {
                spans.Add(new TimeSpanRange(open, broken ? _cab[i - 1].Time : sample.Time));
                start = null;
            }
            if (holds && start is null)
                start = sample.Time;
        }
        if (start is { } last && _cab.Count > 0)
            spans.Add(new TimeSpanRange(last, _cab[^1].Time));
        return spans;
    }

    /// <summary>When the AI drove the user's train: the autopilot.</summary>
    public IReadOnlyList<TimeSpanRange> AutopilotSpans() => CabSpans(s => s.Driving == Driver.Ai);

    /// <summary>The signals the user's train passed: those passed by the vehicle the samples are of at the time.</summary>
    public IReadOnlyList<SignalPassing> UserPassings() =>
        Memo(UserPassingsKey, () => _passings.Where(p => IsUserTrain(p.Time + 0.5, p.Vehicle, p.Position, Math.Max(0, p.Overrun) + 250)).ToList());

    /// <summary>
    /// Whether a vehicle was of the user's train at t. The records of a train give the
    /// vehicle at its head, which is not the one the user is in when the train is driven
    /// from its other end or from a car in the middle: those count when the place the
    /// record is about lies within reach of the user's vehicle.
    /// </summary>
    public bool IsUserTrain(double t, int vehicle, Position place, double reach)
    {
        if (CabAt(t) is not { } s || t - s.Time > 1.5)
            return false;
        if (s.Vehicle == vehicle)
            return true;
        // The couplings tell the train exactly; a recording made before them has the distance only.
        return CouplingsWritten ? ConsistAt(s.Vehicle, t).Contains(vehicle) : s.Position.DistanceTo(place) <= reach;
    }

    /// <summary>
    /// Whether the couplings were written: a recording made since has the movement in its cab samples, and one with
    /// no train of more than one vehicle has no coupling at all.
    /// </summary>
    private bool CouplingsWritten => _couplings.Count > 0 || (_cab.Count > 0 && _cab[0].Movement != Movement.Unknown);

    /// <summary>The vehicles coupled into one train with the one given at t, itself included.</summary>
    public IReadOnlySet<int> ConsistAt(int vehicle, double t)
    {
        // The trains change only at a coupling: the consist is the same until the next one.
        int last = LastAtOrBefore(_couplings, t, c => c.Time);
        if (_consists.TryGetValue((vehicle, last), out var kept))
            return kept;
        var consist = Consist(vehicle, last);
        _consists[(vehicle, last)] = consist;
        return consist;
    }

    private HashSet<int> Consist(int vehicle, int last)
    {
        // What each coupler held after the coupling given: the couplings in the order written, each telling both ends.
        var held = new Dictionary<(int Vehicle, int Side), (int Other, int OtherSide)>();
        for (int i = 0; i <= last; i++)
        {
            var change = _couplings[i];
            if (held.Remove((change.Vehicle, change.Side), out var before))
                held.Remove((before.Other, before.OtherSide));
            if (change.Other != 0 && (change.Flags & CouplingFlags.Coupler) != 0)
            {
                held[(change.Vehicle, change.Side)] = (change.Other, change.OtherSide);
                held[(change.Other, change.OtherSide)] = (change.Vehicle, change.Side);
            }
        }
        var consist = new HashSet<int> { vehicle };
        var open = new Stack<int>();
        open.Push(vehicle);
        while (open.Count > 0)
        {
            int at = open.Pop();
            for (int side = 0; side < 2; side++)
                if (held.TryGetValue((at, side), out var next) && consist.Add(next.Other))
                    open.Push(next.Other);
        }
        return consist;
    }

    /// <summary>Where the user looked from at t, null before it was known.</summary>
    public CameraState? CameraAt(double t) =>
        LastAtOrBefore(_cameras, t, c => c.Time) is var i and >= 0 ? _cameras[i] : null;

    /// <summary>
    /// The couplings made and undone after the start: those at t = 0 are what the trains
    /// were made of, not something done.
    /// </summary>
    public IEnumerable<CouplingChange> CouplingEvents() =>
        _couplings.Where(c => c.Time > 0.01 && (c.Coupled || c.Uncoupled || c.Flags != c.PreviousFlags));

    /// <summary>The couplings the user's train took part in, before or after them.</summary>
    public IReadOnlyList<CouplingChange> UserCouplings() =>
        Memo(UserCouplingsKey, () => CouplingEvents().Where(c => CabAt(c.Time) is { } s && c.Time - s.Time <= 1.5 &&
                                                                 (ConsistAt(s.Vehicle, c.Time - 0.01).Contains(c.Vehicle) ||
                                                                  ConsistAt(s.Vehicle, c.Time + 0.01).Contains(c.Vehicle)))
                                                     .ToList());

    /// <summary>
    /// The timetable of the train the user was in at t: the last one written by then, else the first; none when the
    /// user had gone over to a vehicle of another consist, a train without a timetable or one standing on its own.
    /// </summary>
    public Timetable? TimetableAt(double t)
    {
        var timetable = LastAtOrBefore(_timetables, t, x => x.Time) is var i and >= 0 ? _timetables[i] : _timetables.FirstOrDefault();
        // A recording made before the couplings were written can't tell the trains apart: the timetable stays.
        if (timetable is null || CabAt(t) is not { } sample || sample.Vehicle == timetable.Vehicle || !CouplingsWritten)
            return timetable;
        // another vehicle of the same train (the other cab, a car of it) keeps the timetable
        return ConsistAt(sample.Vehicle, t).Contains(timetable.Vehicle) ? timetable : null;
    }

    /// <summary>The stations of the timetable of the user's train, as it came to them.</summary>
    public IReadOnlyList<StationEvent> UserStations() =>
        Memo(UserStationsKey, () => _stations.Where(s => IsUserTrain(s.Time, s.Vehicle, s.Position, Math.Max(s.TrainLength, 50) + 150)).ToList());

    /// <summary>The horn points the user's train passed, and whether the horn sounded there.</summary>
    public IReadOnlyList<HornCheck> UserHornChecks() => Memo(UserHornChecksKey, () => HornChecks().ToList());

    private IEnumerable<HornCheck> HornChecks()
    {
        foreach (var request in _hornRequests)
        {
            if (!IsUserTrain(request.Time, request.Vehicle, request.Position, 300))
                continue;
            bool autopilot = CabAt(request.Time) is { Driving: Driver.Ai };
            yield return new HornCheck(request, HornSounded(request), autopilot);
        }
    }

    /// <summary>The time given to sound the horn after a horn point, beyond how long it is to sound.</summary>
    public const double HornGrace = 8;

    /// <summary>Whether the horn of the user's train sounded from just before the horn point until a while after.</summary>
    private bool HornSounded(HornRequest request)
    {
        double until = request.Time + Math.Max(request.Duration, 1) + HornGrace;
        for (int i = Math.Max(0, CabIndexAt(request.Time - 2)); i < _cab.Count && _cab[i].Time <= until; i++)
            if (_cab[i].Has(CabDevices.Horn))
                return true;
        return false;
    }

    /// <summary>When the train protection braked the user's train: the alerter or the cab signalling not acknowledged.</summary>
    public IReadOnlyList<TimeSpanRange> SecurityBrakings() => CabSpans(s => s.Has(SecurityFlags.Braking));

    /// <summary>The commands the user gave in a vehicle, presses only.</summary>
    public IEnumerable<CommandEntry> UserCommands() => _commands.Where(c => c.Pressed && c.Vehicle != 0);

    public DrivingSummary Summarize(float tolerance = DrivingSummary.DefaultTolerance) => DrivingSummary.Of(this, tolerance);
}

/// <summary>A horn point the user's train passed: whether the horn sounded, and whether the autopilot drove then.</summary>
public sealed record HornCheck(HornRequest Request, bool Sounded, bool Autopilot);
