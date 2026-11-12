using System;
using System.Collections.Generic;
using System.Numerics;

namespace Eu07.BlackBox;

/// <summary>The types of the records, as the simulator writes them (blackbox_doc.md in its repository).</summary>
public enum RecordType : byte
{
    Info = 0x01,
    Track = 0x10,
    Signal = 0x11,
    Vehicle = 0x12,
    VehicleRemoved = 0x13,
    StopPoint = 0x14,
    HornPoint = 0x15,
    Cab = 0x20,
    Vehicles = 0x21,
    SwitchState = 0x22,
    SignalAspect = 0x23,
    Timetable = 0x24,
    Camera = 0x25,
    Command = 0x30,
    DriverChange = 0x31,
    SignalPassed = 0x32,
    UserVehicle = 0x33,
    Clock = 0x34,
    Station = 0x35,
    HornRequest = 0x36,
    Coupling = 0x37,
    End = 0x3f
}

/// <summary>Who drives a vehicle.</summary>
public enum Driver : byte
{
    None = 0,
    Human = 1,
    Ai = 2
}

[Flags]
public enum CabFlags : byte
{
    None = 0,
    /// <summary>The user is in the cab, not looking around outside.</summary>
    InCab = 0x01,
    /// <summary>The consist is started, so what the AI would do is worked out.</summary>
    AiValid = 0x02,
    /// <summary>The alerter or the cab signalling blinks.</summary>
    AlerterBlinking = 0x04,
    /// <summary>The alerter or the cab signalling beeps.</summary>
    AlerterBeeping = 0x08,
    WheelSlip = 0x10,
    RadioStop = 0x20,
    AlarmChain = 0x40
}

/// <summary>What is switched on in the user's train.</summary>
[Flags]
public enum CabDevices : uint
{
    None = 0,
    Battery = 0x0001,
    MainBreaker = 0x0002,
    PantographFront = 0x0004,
    PantographRear = 0x0008,
    Converter = 0x0010,
    Compressor = 0x0020,
    /// <summary>The motors are connected.</summary>
    LineContactors = 0x0040,
    /// <summary>Tripped.</summary>
    OverloadRelay = 0x0080,
    Sanding = 0x0100,
    Horn = 0x0200,
    DoorsLeft = 0x0400,
    DoorsRight = 0x0800,
    Radio = 0x1000,
    Heating = 0x2000,
    /// <summary>Applied.</summary>
    SpringBrake = 0x4000,
    ShuntMode = 0x8000,
    /// <summary>Sounding.</summary>
    DepartureSignal = 0x0001_0000,
    /// <summary>The doors may be opened.</summary>
    DoorPermitLeft = 0x0002_0000,
    DoorPermitRight = 0x0004_0000,
    /// <summary>The steps come out with the doors.</summary>
    DoorStep = 0x0008_0000,
    /// <summary>The doors lock above a speed.</summary>
    DoorLock = 0x0010_0000,
    /// <summary>Locked now.</summary>
    DoorsLocked = 0x0020_0000,
    /// <summary>Releasing the brakes of the vehicle.</summary>
    BrakeReleaser = 0x0040_0000,
    FuelPump = 0x0080_0000,
    OilPump = 0x0100_0000,
    /// <summary>The fans of the traction motors.</summary>
    MotorBlowers = 0x0200_0000,
    /// <summary>The diesel engine is over its temperature.</summary>
    EngineHot = 0x0400_0000,
    /// <summary>The cruise control holds a speed.</summary>
    SpeedControl = 0x0800_0000
}

/// <summary>What drives the vehicle whose motors the cab drives.</summary>
public enum EngineKind : byte
{
    /// <summary>Nothing of its own: a draisine, a car.</summary>
    None = 0,
    Electric = 1,
    ElectricInduction = 2,
    /// <summary>With a mechanical or a hydraulic transmission.</summary>
    Diesel = 3,
    DieselElectric = 4,
    Steam = 5
}

/// <summary>
/// The rest of a look at the user's train, written from the same version as the doors' permits: what drives it,
/// the brakes' setting and mode, the hand brake, the radio, the horn, the pressure the driver's brake valve shows
/// (its equalizing reservoir, the FV4a's time reservoir) and that of the main reservoir pipe, a diesel's oil, temperatures, clutch and torque converter, the cruise control,
/// the power and the integrated controller of a unit, the highest brake cylinder pressure of the train. The
/// temperatures and the oil mean something for a diesel only.
/// </summary>
public readonly record struct CabExtra(EngineKind Engine, byte BrakeDelay, byte RadioChannel, float DriverValve,
                                       float FeedPipe, float OilPressure, float WaterTemperature, float OilTemperature,
                                       float EngineTemperature, float SpeedControlKmh, float EnginePower, sbyte HandBrake,
                                       byte BrakeMode, byte Horn, float IntegratedController, float Clutch, float ConverterFill,
                                       float ConsistBrakeCylinder);

/// <summary>The train protection: the alerter (CA) and the cab signalling (SHP) apart.</summary>
[Flags]
public enum SecurityFlags : byte
{
    None = 0,
    /// <summary>The alerter lamp blinks: the driver is to acknowledge.</summary>
    Vigilance = 0x01,
    /// <summary>The cab signalling lamp blinks: an SHP magnet was passed.</summary>
    CabSignal = 0x02,
    VigilanceBeeping = 0x04,
    CabSignalBeeping = 0x08,
    /// <summary>Not acknowledged in time: the train protection brakes the train.</summary>
    Braking = 0x10
}

/// <summary>What a train did at a station of its timetable.</summary>
public enum StationKind : byte
{
    /// <summary>Stopped at the stop point of a stop of its timetable.</summary>
    Stopped = 1,
    /// <summary>Passed a station where the timetable has no stop.</summary>
    Passed = 2,
    /// <summary>Left the stop point behind before it was time to go: without stopping, or leaving early.</summary>
    Missed = 3,
    /// <summary>Still at the stop point when it was time to go.</summary>
    Ready = 4
}

/// <summary>The lights lit at an end of a vehicle.</summary>
[Flags]
public enum Lights : ushort
{
    None = 0,
    HeadlightLeft = 0x001,
    RedMarkerLeft = 0x002,
    HeadlightUpper = 0x004,
    HeadlightRight = 0x010,
    RedMarkerRight = 0x020,
    RearEndSignals = 0x040,
    AuxiliaryLeft = 0x080,
    AuxiliaryRight = 0x100,
    HighBeamLeft = 0x200,
    HighBeamRight = 0x400
}

[Flags]
public enum PassingFlags : byte
{
    None = 0,
    /// <summary>A shunting signal.</summary>
    Shunt = 0x01,
    /// <summary>Passed showing stop: a signal passed at danger.</summary>
    AtStop = 0x02,
    /// <summary>Stop of an automatic block signal, which may be passed slowly after a stop.</summary>
    AutomaticBlock = 0x04
}

public enum TrackKind : byte
{
    Normal = 0,
    SwitchBranch = 1
}

/// <summary>A position in the frame of the scenery: metres, y up.</summary>
public readonly record struct Position(double X, double Y, double Z)
{
    public static Position Lerp(Position a, Position b, double f) =>
        new(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);

    public double DistanceTo(Position other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public sealed record RecordingInfo(string Scenery, string Started, int Year, int YearDay, double SecondOfDay,
                                   string StartVehicle, string SimulatorVersion, float CabInterval,
                                   float VehiclesInterval, float KeyframeInterval)
{
    /// <summary>The time of day the simulation started at.</summary>
    public TimeSpan StartClock => TimeSpan.FromSeconds(SecondOfDay);
}

/// <summary>One rail track of the map, or one of the two ways of a switch, which share the number.</summary>
public sealed record MapTrack(int Index, string Name, TrackKind Kind, int Branch, int Category,
                              IReadOnlyList<Vector3> Points);

public sealed record MapSignal(int Index, string Name, Position Position);

/// <summary>
/// A stop point of a passenger train (W4). StopAt: below 0 the head stops that many metres
/// before it, above 0 the middle of the train. Platform: above 0 its length, below 0 the
/// stop is for trains shorter than that (W32), 0 not given; its last digit also tells the side of
/// the platform (see <see cref="Platforms"/>).
/// </summary>
public sealed record MapStopPoint(int Index, string Station, Position Position, float StopAt, float Platform)
{
    /// <summary>
    /// The heading of the trains it is for [rad, from +z towards +x], NaN when not known: on no track, on tracks of
    /// both ways, or in a recording made before it was written.
    /// </summary>
    public float Heading { get; init; } = float.NaN;

    public bool HasHeading => !float.IsNaN(Heading);

    public bool HasPlatform => Platform > 0;

    /// <summary>A W32: the stop of the trains shorter than its length; the longer ones go on to another stop point.</summary>
    public bool ForShortTrains => Platform < 0;

    /// <summary>The length of the platform, or of the trains a W32 is for [m].</summary>
    public double PlatformLength => Platforms.Length(Platform);
}

/// <summary>
/// The second value of a stop point, as the simulator's driver reads it: the whole of it the metres of the
/// platform, and its last digit, besides, the side the doors open on (1, 2, both 3); 201 is 201 m.
/// </summary>
public static class Platforms
{
    public static double Length(float value) => Math.Abs(value);

    public static int Side(float value) => (int)Math.Floor(Math.Abs(value)) % 10;
}

/// <summary>A horn point (W6): the trains passing it are to sound the horn this long, in this tone (1 low, 2 high).</summary>
public sealed record MapHornPoint(int Index, string Name, Position Position, float Duration, float Tone);

/// <summary>A station of a timetable; the times in minutes of the day, below 0 none.</summary>
public sealed record TimetableRow(string Station, float Km, float Vmax, float Arrival, float Departure, bool Maintenance)
{
    /// <summary>The train stops there: a station passed through has a departure only.</summary>
    public bool IsStop => Arrival >= 0;
}

/// <summary>The timetable of the train the user was in. The rows are numbered from 1, the station records give their number.</summary>
public sealed record Timetable(double Time, int Vehicle, string Train, string Category, string From, string To, float Vmax,
                               IReadOnlyList<TimetableRow> Rows)
{
    public TimetableRow? Row(int number) => number >= 1 && number <= Rows.Count ? Rows[number - 1] : null;
}

/// <summary>
/// A train at a station of its timetable, at its stop point. Distance: of its head before
/// the stop point, below 0 past it; StopAt: where the head was to stop, that many metres before it.
/// </summary>
public sealed record StationEvent(double Time, int Vehicle, StationKind Kind, int Row, string Station, Position Position,
                                  float Distance, float StopAt, float TrainLength, float Platform, float Arrival,
                                  float Departure)
{
    /// <summary>How far from the place it was to stop the head stood: above 0 short of it, below 0 past it.</summary>
    public float StopError => Distance - StopAt;

    /// <summary>
    /// Whether the whole train stood along the platform, when its length is known: as the driver takes it, the length of
    /// the trains a W32 is for is the room it has there too.
    /// </summary>
    public bool? OnPlatform => Platform != 0 ? -Distance <= PlatformTolerance && Distance + TrainLength - PlatformLength <= PlatformTolerance : null;

    /// <summary>How far off the platform a train may stand at either end and still count as along it [m].</summary>
    public const double PlatformTolerance = 2;

    /// <summary>The length of the platform [m], without the digit of its side.</summary>
    public double PlatformLength => Platforms.Length(Platform);

    /// <summary>How much of the train stood off the platform [m]: the head past the stop point, the tail before the platform's end; 0 along it.</summary>
    public double OffPlatform => Platform != 0 ? Math.Max(0, -Distance) + Math.Max(0, Distance + TrainLength - PlatformLength) : 0;
}

public enum CameraMode : byte
{
    /// <summary>From the cab of the vehicle the user is in.</summary>
    Cab = 0,
    /// <summary>One of the outside views of the user's train.</summary>
    External = 1,
    /// <summary>The free camera.</summary>
    Free = 2,
    Debug = 3
}

/// <summary>Where the user looked from. Heading as that of a vehicle: towards (sin, cos) in (x, z).</summary>
public readonly record struct CameraState(double Time, CameraMode Mode, int View, int Vehicle, Position Position,
                                          float Heading, float Pitch);

/// <summary>What a coupling joins: the bits of the simulator's coupling.</summary>
[Flags]
public enum CouplingFlags : byte
{
    None = 0,
    Coupler = 0x01,
    BrakePipe = 0x02,
    Control = 0x04,
    Power = 0x08,
    Gangway = 0x10,
    MainPipe = 0x20,
    Heating = 0x40,
    /// <summary>Not undone in shunting: between the cars of a unit.</summary>
    Permanent = 0x80
}

/// <summary>A coupler coupled, uncoupled, or its hoses and cables changed. Side 0 the front coupler, 1 the rear one.</summary>
public sealed record CouplingChange(double Time, int Vehicle, int Side, int Other, int OtherSide, CouplingFlags Flags,
                                    CouplingFlags PreviousFlags, int PreviousOther, Position Position)
{
    public bool Coupled => Other != 0 && (Flags & CouplingFlags.Coupler) != 0 && PreviousOther != Other;
    public bool Uncoupled => Other == 0 && PreviousOther != 0;
}

/// <summary>A train told to sound the horn at a horn point.</summary>
public readonly record struct HornRequest(double Time, int Vehicle, float Duration, float Tone, Position Position);

/// <summary>A vehicle as it is first seen. Id 0 is no vehicle.</summary>
public sealed record VehicleInfo(int Id, string Name, string Type, string Train, float Length, float Width,
                                 int Category)
{
    public double? RemovedAt { get; internal set; }
}

/// <summary>What the AI driver would do at that moment, from that place and at that speed.</summary>
public readonly record struct AiReference(float TargetKmh, float NextLimitKmh, float NextLimitDistance,
                                          float Acceleration, float BrakingDistance, float TimetableVmaxKmh,
                                          float TrackVmaxKmh)
{
    public bool HasNextLimit => NextLimitKmh >= 0 && NextLimitDistance >= 0;
}

/// <summary>One look at the user's train, ten a second.</summary>
public readonly record struct CabSample(double Time, int Vehicle, Position Position, float SpeedKmh,
                                        float Acceleration, float OdometerKm, Driver Driving, CabFlags Flags,
                                        sbyte MainController, sbyte ScndController, sbyte Reverser,
                                        float TrainBrake, float LocalBrake, float BrakeCylinder, float BrakePipe,
                                        float MotorCurrent, AiReference Ai, CabDevices Devices,
                                        float MainReservoir, float Voltage, float EngineRpm,
                                        Lights LightsHead, Lights LightsTail, sbyte CabEnd = 0,
                                        SecurityFlags Security = SecurityFlags.None, float LateralAcceleration = 0,
                                        Movement Movement = Movement.Unknown, short LightHintHead = -1, short LightHintTail = -1,
                                        Lights? EquippedHead = null, Lights? EquippedTail = null, CabExtra? Extra = null)
{
    public bool Has(CabDevices device) => (Devices & device) == device;
    public bool Has(SecurityFlags flag) => (Security & flag) == flag;

    public bool AiValid => (Flags & CabFlags.AiValid) != 0;
    public bool InCab => (Flags & CabFlags.InCab) != 0;
    public float AbsSpeedKmh => Math.Abs(SpeedKmh);
}

/// <summary>One vehicle in a look at the vehicles.</summary>
public readonly record struct VehicleState(int Id, Vector3 Position, float Heading, float SpeedKmh, Driver Driving);

/// <summary>A look at the vehicles: those which moved, or all of them in a keyframe.</summary>
public sealed record VehicleLook(double Time, bool Keyframe, IReadOnlyList<VehicleState> States);

public readonly record struct SwitchChange(double Time, int Track, int State);

/// <summary>What a signal shows: the speeds its memory cell holds, 0 stop, -1 no limit, and the command.</summary>
public readonly record struct AspectChange(double Time, int Signal, float Speed1, float Speed2, string Command);

public sealed record CommandEntry(double Time, int Vehicle, bool Pressed, double Param1, double Param2, string Name);

public readonly record struct DriverChange(double Time, int Vehicle, Driver Driving);

public sealed record SignalPassing(double Time, int Vehicle, int Signal, string Event, Position Position,
                                   float Speed1, float Speed2, float SpeedKmh, float Overrun, PassingFlags Flags)
{
    /// <summary>No signal of the map: the passing came from an event the map doesn't hold.</summary>
    public const int NoSignal = -1;

    public bool AtDanger => (Flags & PassingFlags.AtStop) != 0;
}

/// <summary>The vehicle the user was in, whether in its cab, and which cab: 1 the front one, -1 the rear one, 0 none.</summary>
public readonly record struct UserVehicleChange(double Time, int Vehicle, bool InCab, sbyte CabEnd = 0);

public readonly record struct ClockEntry(double Time, int Year, int YearDay, double SecondOfDay);
