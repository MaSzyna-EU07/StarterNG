using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Eu07.BlackBox;

/// <summary>
/// Reads a black box into a <see cref="Recording"/>, as much of it as is there. A file
/// the simulator still writes is read again from where the last read stopped: a record
/// cut short waits for the next read, so a running session can be followed every few
/// seconds without reading it from the start.
/// </summary>
public sealed class BlackBoxReader
{
    public static ReadOnlySpan<byte> Magic => "EU07BBX\0"u8;
    public const int FormatVersion = 1;
    public const int HeaderSize = 12;
    public const int RecordHeaderSize = 5;

    private long _offset;

    public Recording Recording { get; } = new();

    /// <summary>Bytes taken so far: the header and the whole records.</summary>
    public long Offset => _offset;

    /// <summary>Opens the file shared, as the simulator may be writing it, and reads what is new.</summary>
    public int ReadMore(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadMore(stream);
    }

    /// <summary>Reads the whole records added since the last read. Returns how many were read.</summary>
    /// <exception cref="InvalidDataException">Not a black box, or of a version this reader doesn't know.</exception>
    public int ReadMore(Stream stream)
    {
        if (stream.CanSeek)
        {
            if (stream.Length < _offset)
                throw new InvalidDataException("The black box got shorter than what was read of it: it was written anew.");
            stream.Seek(_offset, SeekOrigin.Begin);
        }

        using var added = new MemoryStream();
        stream.CopyTo(added);
        var bytes = added.GetBuffer().AsSpan(0, (int)added.Length);

        int at = 0;
        if (_offset == 0)
        {
            if (bytes.Length < HeaderSize)
                return 0;
            ReadHeader(bytes[..HeaderSize]);
            at = HeaderSize;
        }

        int read = 0;
        while (bytes.Length - at >= RecordHeaderSize)
        {
            var type = (RecordType)bytes[at];
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at + 1, 4));
            if (length > int.MaxValue || bytes.Length - at - RecordHeaderSize < length)
                break;

            var payload = bytes.Slice(at + RecordHeaderSize, (int)length);
            try
            {
                Take(type, new PayloadReader(payload));
            }
            catch (EndOfStreamException)
            {
                // Shorter than its type says: passed over, as one of an unknown type is.
                Recording.Malformed++;
            }
            at += RecordHeaderSize + (int)length;
            ++read;
        }

        _offset += at;
        if (read > 0)
            Recording.Changed();
        return read;
    }

    /// <summary>Reads a whole file at once.</summary>
    public static Recording Read(string path)
    {
        var reader = new BlackBoxReader();
        reader.ReadMore(path);
        return reader.Recording;
    }

    public static Recording Read(Stream stream)
    {
        var reader = new BlackBoxReader();
        reader.ReadMore(stream);
        return reader.Recording;
    }

    /// <summary>Whether the bytes start as a black box does.</summary>
    public static bool LooksLikeBlackBox(ReadOnlySpan<byte> start) => start.Length >= Magic.Length && start[..Magic.Length].SequenceEqual(Magic);

    private void ReadHeader(ReadOnlySpan<byte> header)
    {
        if (!LooksLikeBlackBox(header))
            throw new InvalidDataException("Not a black box of the simulator.");
        int version = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
        if (version != FormatVersion)
            throw new InvalidDataException($"A black box of version {version}, this reader knows version {FormatVersion}.");
        Recording.Version = version;
    }

    private void Take(RecordType type, PayloadReader r)
    {
        var rec = Recording;
        switch (type)
        {
            case RecordType.Info:
                rec.Info = new RecordingInfo(r.Text(), r.Text(), r.U16(), r.U16(), r.F64(), r.Text(), r.Text(),
                                             r.F32(), r.F32(), r.F32());
                break;
            case RecordType.Track:
            {
                int index = (int)r.U32();
                string name = r.Text();
                var kind = (TrackKind)r.U8();
                int branch = r.U8();
                int category = r.U8();
                uint count = r.U32();
                if (count > r.Remaining / 12)
                    throw new EndOfStreamException();
                var points = new Vector3[count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = r.Vec3();
                rec.AddTrack(new MapTrack(index, name, kind, branch, category, points));
                break;
            }
            case RecordType.Signal:
                rec.AddSignal(new MapSignal((int)r.U32(), r.Text(), r.DVec3()));
                break;
            case RecordType.Vehicle:
                rec.AddVehicle(new VehicleInfo((int)r.U32(), r.Text(), r.Text(), r.Text(), r.F32(), r.F32(), r.U8()));
                break;
            case RecordType.VehicleRemoved:
                rec.RemoveVehicle(r.F64(), (int)r.U32());
                break;
            case RecordType.Cab:
            {
                var sample = new CabSample(r.F64(), (int)r.U32(), r.DVec3(), r.F32(), r.F32(), r.F32(), (Driver)r.U8(),
                                           (CabFlags)r.U8(), r.I8(), r.I8(), r.I8(), r.F32(), r.F32(), r.F32(), r.F32(),
                                           r.F32(),
                                           new AiReference(r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32()),
                                           (CabDevices)r.U32(), r.F32(), r.F32(), r.F32(), (Lights)r.U16(), (Lights)r.U16());
                // The cab, the train protection and the lateral acceleration came later: a recording
                // made before them ends here.
                if (r.Remaining >= 6)
                    sample = sample with { CabEnd = r.I8(), Security = (SecurityFlags)r.U8(), LateralAcceleration = r.F32() };
                // What the driver went by and the lights asked for came later still.
                if (r.Remaining >= 5)
                    sample = sample with { Movement = (Movement)r.U8(), LightHintHead = (short)r.U16(), LightHintTail = (short)r.U16() };
                // The lamps the vehicles at the ends have, which a pattern is checked against, later again.
                if (r.Remaining >= 4)
                    sample = sample with { EquippedHead = (Lights)r.U16(), EquippedTail = (Lights)r.U16() };
                // What drives it, the doors' permits and the rest, later again; the arguments are read in their order.
                if (r.Remaining >= 54)
                    sample = sample with
                    {
                        Extra = new CabExtra((EngineKind)r.U8(), r.U8(), r.U8(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(),
                                             r.F32(), r.F32(), r.F32(), r.I8(), r.U8(), r.U8(), r.F32(), r.F32(), r.F32(), r.F32())
                    };
                rec.AddCab(sample);
                break;
            }
            case RecordType.StopPoint:
            {
                var point = new MapStopPoint((int)r.U32(), r.Text(), r.DVec3(), r.F32(), r.F32());
                // The heading came later: a recording made before it ends here.
                if (r.Remaining >= 4)
                    point = point with { Heading = r.F32() };
                rec.AddStopPoint(point);
                break;
            }
            case RecordType.HornPoint:
                rec.AddHornPoint(new MapHornPoint((int)r.U32(), r.Text(), r.DVec3(), r.F32(), r.F32()));
                break;
            case RecordType.Timetable:
            {
                double time = r.F64();
                int vehicle = (int)r.U32();
                string train = r.Text(), category = r.Text(), from = r.Text(), to = r.Text();
                float vmax = r.F32();
                uint count = r.U32();
                // A row holds at least its empty name, four numbers and a flag.
                if (count > r.Remaining / 18)
                    throw new EndOfStreamException();
                var rows = new TimetableRow[count];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = new TimetableRow(r.Text(), r.F32(), r.F32(), r.F32(), r.F32(), r.U8() != 0);
                rec.AddTimetable(new Timetable(time, vehicle, train, category, from, to, vmax, rows));
                break;
            }
            case RecordType.Station:
                rec.AddStation(new StationEvent(r.F64(), (int)r.U32(), (StationKind)r.U8(), r.U16(), r.Text(), r.DVec3(),
                                                r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32()));
                break;
            case RecordType.Camera:
                rec.AddCamera(new CameraState(r.F64(), (CameraMode)r.U8(), r.U8(), (int)r.U32(), r.DVec3(), r.F32(), r.F32()));
                break;
            case RecordType.Coupling:
                rec.AddCoupling(new CouplingChange(r.F64(), (int)r.U32(), r.U8(), (int)r.U32(), r.U8(), (CouplingFlags)r.U8(),
                                                   (CouplingFlags)r.U8(), (int)r.U32(), r.DVec3()));
                break;
            case RecordType.HornRequest:
                rec.AddHornRequest(new HornRequest(r.F64(), (int)r.U32(), r.F32(), r.F32(), r.DVec3()));
                break;
            case RecordType.Vehicles:
            {
                double time = r.F64();
                bool keyframe = r.U8() != 0;
                uint count = r.U32();
                if (count > r.Remaining / 25)
                    throw new EndOfStreamException();
                var states = new VehicleState[count];
                for (int i = 0; i < states.Length; i++)
                    states[i] = new VehicleState((int)r.U32(), r.Vec3(), r.F32(), r.F32(), (Driver)r.U8());
                rec.AddLook(new VehicleLook(time, keyframe, states));
                break;
            }
            case RecordType.SwitchState:
                rec.AddSwitch(new SwitchChange(r.F64(), (int)r.U32(), r.U8()));
                break;
            case RecordType.SignalAspect:
                rec.AddAspect(new AspectChange(r.F64(), (int)r.U32(), r.F32(), r.F32(), r.Text()));
                break;
            case RecordType.Command:
            {
                double time = r.F64();
                int vehicle = (int)r.U32();
                bool pressed = r.U8() != 0;
                double param1 = r.F64(), param2 = r.F64();
                rec.AddCommand(new CommandEntry(time, vehicle, pressed, param1, param2, r.Text()));
                break;
            }
            case RecordType.DriverChange:
                rec.AddDriverChange(new DriverChange(r.F64(), (int)r.U32(), (Driver)r.U8()));
                break;
            case RecordType.SignalPassed:
            {
                double time = r.F64();
                int vehicle = (int)r.U32();
                int signal = unchecked((int)r.U32());
                rec.AddPassing(new SignalPassing(time, vehicle, signal, r.Text(), r.DVec3(), r.F32(), r.F32(), r.F32(),
                                                 r.F32(), (PassingFlags)r.U8()));
                break;
            }
            case RecordType.UserVehicle:
            {
                var change = new UserVehicleChange(r.F64(), (int)r.U32(), r.U8() != 0);
                if (r.Remaining >= 1)
                    change = change with { CabEnd = r.I8() };
                rec.AddUserVehicle(change);
                break;
            }
            case RecordType.Clock:
                rec.AddClock(new ClockEntry(r.F64(), r.U16(), r.U16(), r.F64()));
                break;
            case RecordType.End:
                rec.EndTime = r.F64();
                break;
            default:
                // A type of a later version: its length lets it be passed over.
                rec.Unknown++;
                break;
        }
    }

    /// <summary>The fields of a record in the order they come, little-endian. A field past the end throws.</summary>
    private ref struct PayloadReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _at;

        public PayloadReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _at = 0;
        }

        public int Remaining => _bytes.Length - _at;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (Remaining < count)
                throw new EndOfStreamException();
            var span = _bytes.Slice(_at, count);
            _at += count;
            return span;
        }

        public byte U8() => Take(1)[0];
        public sbyte I8() => unchecked((sbyte)Take(1)[0]);
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
        public Vector3 Vec3() => new(F32(), F32(), F32());
        public Position DVec3() => new(F64(), F64(), F64());

        /// <summary>UTF-8 after its length in 7 bit groups, low first, as BinaryWriter writes a string.</summary>
        public string Text()
        {
            int length = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (shift > 28)
                    throw new EndOfStreamException();
                byte b = U8();
                length |= (b & 0x7f) << shift;
                if (b < 0x80)
                    break;
            }
            if (length < 0)
                throw new EndOfStreamException();
            return length == 0 ? "" : Encoding.UTF8.GetString(Take(length));
        }
    }
}
