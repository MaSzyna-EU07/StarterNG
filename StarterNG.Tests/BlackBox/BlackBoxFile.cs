using System.Numerics;
using System.Text;
using Eu07.BlackBox;

namespace StarterNG.Tests.BlackBox;

/// <summary>
/// Writes a black box the way the simulator does (blackbox_doc.md of its repository),
/// field by field, so the reader is checked against the layout and not against itself.
/// </summary>
internal sealed class BlackBoxFile
{
    private readonly MemoryStream _bytes = new();
    private readonly BinaryWriter _file;

    public BlackBoxFile(ushort version = 1, bool header = true)
    {
        _file = new BinaryWriter(_bytes, Encoding.UTF8, leaveOpen: true);
        if (header)
        {
            _file.Write("EU07BBX\0"u8);
            _file.Write(version);
            _file.Write((ushort)0);
        }
    }

    public byte[] Bytes => _bytes.ToArray();

    public MemoryStream Stream() => new(Bytes);

    /// <summary>A record of a type, its length and what the writer puts in it.</summary>
    public BlackBoxFile Record(byte type, Action<BinaryWriter> payload)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            payload(w);
        _file.Write(type);
        _file.Write((uint)body.Length);
        _file.Write(body.ToArray());
        return this;
    }

    public BlackBoxFile Info(double secondOfDay = 10 * 3600 + 34 * 60, string scenery = "td.scn") =>
        Record(0x01, w =>
        {
            w.Write(scenery);
            w.Write("2026-11-08T22:56:30");
            w.Write((ushort)2026);
            w.Write((ushort)312);
            w.Write(secondOfDay);
            w.Write("ep07-bb");
            w.Write("EU07 test");
            w.Write(0.1f);
            w.Write(1.0f);
            w.Write(60.0f);
        });

    public BlackBoxFile Track(uint index, string name, params Vector3[] points) =>
        Track(index, name, 0, 0, points);

    public BlackBoxFile Track(uint index, string name, byte kind, byte branch, params Vector3[] points) =>
        Record(0x10, w =>
        {
            w.Write(index);
            w.Write(name);
            w.Write(kind);
            w.Write(branch);
            w.Write((byte)1);
            w.Write((uint)points.Length);
            foreach (var p in points)
            {
                w.Write(p.X);
                w.Write(p.Y);
                w.Write(p.Z);
            }
        });

    public BlackBoxFile Signal(uint index, string name, double x, double y, double z) =>
        Record(0x11, w =>
        {
            w.Write(index);
            w.Write(name);
            w.Write(x);
            w.Write(y);
            w.Write(z);
        });

    public BlackBoxFile Vehicle(uint id, string name, string train = "", float length = 16.3f) =>
        Record(0x12, w =>
        {
            w.Write(id);
            w.Write(name);
            w.Write("303e-ep-tv");
            w.Write(train);
            w.Write(length);
            w.Write(2.9f);
            w.Write((byte)1);
        });

    public BlackBoxFile Removed(double time, uint id) =>
        Record(0x13, w =>
        {
            w.Write(time);
            w.Write(id);
        });

    public BlackBoxFile Cab(double time, uint vehicle, double z, float speed, byte driving = 1, byte flags = 0x03,
                            float target = 40, float nextLimit = -1, float nextDistance = -1, float brakeCylinder = 0,
                            uint devices = 0x0007, bool extended = true, sbyte cabEnd = 1, byte security = 0,
                            float lateral = 0.2f, ushort lightsHead = 0x15, ushort lightsTail = 0x22, byte? movement = null,
                            short hintHead = -1, short hintTail = -1) =>
        Record(0x20, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
            w.Write(speed);
            w.Write(0.1f);  // acceleration
            w.Write(1.5f);  // odometer
            w.Write(driving);
            w.Write(flags);
            w.Write((sbyte)3);  // main controller
            w.Write((sbyte)0);
            w.Write((sbyte)1);
            w.Write(0.0f);  // train brake
            w.Write(0.0f);
            w.Write(brakeCylinder);
            w.Write(5.0f);  // brake pipe
            w.Write(320.0f);  // current
            w.Write(target);
            w.Write(nextLimit);
            w.Write(nextDistance);
            w.Write(0.3f);
            w.Write(150.0f);
            w.Write(60.0f);
            w.Write(-1.0f);
            w.Write(devices);
            w.Write(8.5f);  // main reservoir
            w.Write(3000.0f);  // voltage
            w.Write(1100.0f);  // revolutions
            w.Write(lightsHead);  // lights at the head of the train
            w.Write(lightsTail);
            // The cab, the train protection and the lateral acceleration: a recording made before them ends above.
            if (extended)
            {
                w.Write(cabEnd);
                w.Write(security);
                w.Write(lateral);
                // what the driver goes by and the lights asked for: a recording made before them ends above
                if (movement is { } kind)
                {
                    w.Write(kind);
                    w.Write(hintHead);
                    w.Write(hintTail);
                }
            }
        });

    public BlackBoxFile Vehicles(double time, bool keyframe, params (uint Id, float X, float Z, float Speed)[] states) =>
        Record(0x21, w =>
        {
            w.Write(time);
            w.Write((byte)(keyframe ? 1 : 0));
            w.Write((uint)states.Length);
            foreach (var s in states)
            {
                w.Write(s.Id);
                w.Write(s.X);
                w.Write(0.4f);
                w.Write(s.Z);
                w.Write(0.0f);  // heading
                w.Write(s.Speed);
                w.Write((byte)2);
            }
        });

    public BlackBoxFile Switch(double time, uint track, byte state) =>
        Record(0x22, w =>
        {
            w.Write(time);
            w.Write(track);
            w.Write(state);
        });

    public BlackBoxFile Aspect(double time, uint signal, float speed1, float speed2, string command = "SetVelocity") =>
        Record(0x23, w =>
        {
            w.Write(time);
            w.Write(signal);
            w.Write(speed1);
            w.Write(speed2);
            w.Write(command);
        });

    public BlackBoxFile Command(double time, uint vehicle, bool pressed, string name) =>
        Record(0x30, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write((byte)(pressed ? 1 : 0));
            w.Write(1.0);
            w.Write(2.0);
            w.Write(name);
        });

    public BlackBoxFile Driver(double time, uint vehicle, byte driving) =>
        Record(0x31, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(driving);
        });

    public BlackBoxFile Passed(double time, uint vehicle, uint signal, string name, float speed1, float speed,
                               byte flags = 0, double z = 1300) =>
        Record(0x32, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(signal);
            w.Write(name);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
            w.Write(speed1);
            w.Write(-1.0f);
            w.Write(speed);
            w.Write(2.5f);
            w.Write(flags);
        });

    public BlackBoxFile User(double time, uint vehicle, bool inCab, sbyte? cabEnd = null) =>
        Record(0x33, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write((byte)(inCab ? 1 : 0));
            if (cabEnd is { } end)
                w.Write(end);
        });

    public BlackBoxFile StopPoint(uint index, string station, double z, float stopAt, float platform, float? heading = null) =>
        Record(0x14, w =>
        {
            w.Write(index);
            w.Write(station);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
            w.Write(stopAt);
            w.Write(platform);
            if (heading is { } way)
                w.Write(way);
        });

    public BlackBoxFile HornPoint(uint index, string name, double z, float duration = 2, float tone = 1) =>
        Record(0x15, w =>
        {
            w.Write(index);
            w.Write(name);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
            w.Write(duration);
            w.Write(tone);
        });

    public BlackBoxFile Timetable(double time, uint vehicle, string train, string from, string to,
                                  params (string Station, float Arrival, float Departure)[] rows) =>
        Record(0x24, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(train);
            w.Write("ROJ");
            w.Write(from);
            w.Write(to);
            w.Write(100.0f);
            w.Write((uint)rows.Length);
            float km = 0;
            foreach (var row in rows)
            {
                w.Write(row.Station);
                w.Write(km += 2.5f);
                w.Write(100.0f);
                w.Write(row.Arrival);
                w.Write(row.Departure);
                w.Write((byte)0);
            }
        });

    public BlackBoxFile Station(double time, uint vehicle, byte kind, ushort row, string station, double z,
                                float distance = 1, float stopAt = 0, float trainLength = 87, float platform = 150,
                                float arrival = -1, float departure = -1) =>
        Record(0x35, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(kind);
            w.Write(row);
            w.Write(station);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
            w.Write(distance);
            w.Write(stopAt);
            w.Write(trainLength);
            w.Write(platform);
            w.Write(arrival);
            w.Write(departure);
        });

    public BlackBoxFile HornRequest(double time, uint vehicle, double z, float duration = 2) =>
        Record(0x36, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(duration);
            w.Write(1.0f);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(z);
        });

    public BlackBoxFile Camera(double time, byte mode, uint vehicle, double z, byte view = 0, float heading = 0.5f) =>
        Record(0x25, w =>
        {
            w.Write(time);
            w.Write(mode);
            w.Write(view);
            w.Write(vehicle);
            w.Write(5.0);
            w.Write(2.4);
            w.Write(z);
            w.Write(heading);
            w.Write(-0.1f);
        });

    public BlackBoxFile Coupling(double time, uint vehicle, byte side, uint other, byte otherSide, byte flags,
                                 byte previousFlags, uint previousOther) =>
        Record(0x37, w =>
        {
            w.Write(time);
            w.Write(vehicle);
            w.Write(side);
            w.Write(other);
            w.Write(otherSide);
            w.Write(flags);
            w.Write(previousFlags);
            w.Write(previousOther);
            w.Write(5.0);
            w.Write(0.4);
            w.Write(1150.0);
        });

    public BlackBoxFile Clock(double time, double secondOfDay) =>
        Record(0x34, w =>
        {
            w.Write(time);
            w.Write((ushort)2026);
            w.Write((ushort)312);
            w.Write(secondOfDay);
        });

    public BlackBoxFile End(double time) => Record(0x3f, w => w.Write(time));
}
