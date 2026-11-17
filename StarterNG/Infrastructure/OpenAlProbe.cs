using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StarterNG.Infrastructure;

/// <summary>What the simulator's OpenAL library offers for its sound settings.</summary>
public sealed record AudioChoices(IReadOnlyList<string> Devices, IReadOnlyList<string> HrtfProfiles, IReadOnlyList<string> Resamplers)
{
    public static AudioChoices None { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// Asks the OpenAL library beside the simulator, the one it plays through, for the output devices,
/// the HRTF data sets and the resamplers. The data sets and the resamplers are known only with a
/// device open, so the default one is opened for a moment. An older library (OpenAL Soft before
/// 1.19 lists no resamplers), or none at all, gives what it can and empty lists for the rest.
/// </summary>
public static unsafe class OpenAlProbe
{
    private const int AlcDeviceSpecifier = 0x1005;
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int AlcNumHrtfSpecifiers = 0x1994;
    private const int AlcHrtfSpecifier = 0x1995;
    private const int AlNumResamplers = 0x1210;
    private const int AlResamplerName = 0x1213;

    /// <summary>The file of the library in the simulator's folder.</summary>
    public static string LibraryFile => OperatingSystem.IsWindows() ? "OpenAL32.dll" : "libopenal.so.1";

    public static AudioChoices Read(string gameFolder)
    {
        // The library stays loaded: OpenAL Soft keeps threads of its own, unloading it under them is not safe.
        if (!NativeLibrary.TryLoad(Path.Combine(gameFolder, LibraryFile), out IntPtr library))
            return AudioChoices.None;
        try
        {
            return new Api(library).Read();
        }
        catch (EntryPointNotFoundException)
        {
            return AudioChoices.None;
        }
    }

    /// <summary>The entry points of one library; the extension ones are null where it lacks them.</summary>
    private sealed class Api
    {
        private readonly delegate* unmanaged[Cdecl]<IntPtr, byte*, byte> _alcIsExtensionPresent;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int, byte*> _alcGetString;
        private readonly delegate* unmanaged[Cdecl]<byte*, IntPtr> _alcOpenDevice;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, byte> _alcCloseDevice;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int*, IntPtr> _alcCreateContext;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, byte> _alcMakeContextCurrent;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, void> _alcDestroyContext;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, void> _alcGetIntegerv;
        private readonly delegate* unmanaged[Cdecl]<int, int> _alGetInteger;
        private readonly delegate* unmanaged[Cdecl]<IntPtr, int, int, byte*> _alcGetStringiSOFT;
        private readonly delegate* unmanaged[Cdecl]<int, int, byte*> _alGetStringiSOFT;

        public Api(IntPtr library)
        {
            _alcIsExtensionPresent = (delegate* unmanaged[Cdecl]<IntPtr, byte*, byte>)NativeLibrary.GetExport(library, "alcIsExtensionPresent");
            _alcGetString = (delegate* unmanaged[Cdecl]<IntPtr, int, byte*>)NativeLibrary.GetExport(library, "alcGetString");
            _alcOpenDevice = (delegate* unmanaged[Cdecl]<byte*, IntPtr>)NativeLibrary.GetExport(library, "alcOpenDevice");
            _alcCloseDevice = (delegate* unmanaged[Cdecl]<IntPtr, byte>)NativeLibrary.GetExport(library, "alcCloseDevice");
            _alcCreateContext = (delegate* unmanaged[Cdecl]<IntPtr, int*, IntPtr>)NativeLibrary.GetExport(library, "alcCreateContext");
            _alcMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, byte>)NativeLibrary.GetExport(library, "alcMakeContextCurrent");
            _alcDestroyContext = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(library, "alcDestroyContext");
            _alcGetIntegerv = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, void>)NativeLibrary.GetExport(library, "alcGetIntegerv");
            _alGetInteger = (delegate* unmanaged[Cdecl]<int, int>)NativeLibrary.GetExport(library, "alGetInteger");
            // OpenAL Soft exports its extension functions too; a library without them leaves these null.
            NativeLibrary.TryGetExport(library, "alcGetStringiSOFT", out IntPtr getStringi);
            _alcGetStringiSOFT = (delegate* unmanaged[Cdecl]<IntPtr, int, int, byte*>)getStringi;
            NativeLibrary.TryGetExport(library, "alGetStringiSOFT", out IntPtr alGetStringi);
            _alGetStringiSOFT = (delegate* unmanaged[Cdecl]<int, int, byte*>)alGetStringi;
        }

        public AudioChoices Read()
        {
            int deviceList = HasExtension("ALC_ENUMERATE_ALL_EXT\0"u8) ? AlcAllDevicesSpecifier : AlcDeviceSpecifier;
            List<string> devices = NameList(_alcGetString(IntPtr.Zero, deviceList));

            IntPtr device = _alcOpenDevice(null);
            if (device == IntPtr.Zero)
                return new AudioChoices(devices, Array.Empty<string>(), Array.Empty<string>());
            try
            {
                return new AudioChoices(devices, HrtfProfiles(device), Resamplers(device));
            }
            finally
            {
                _alcCloseDevice(device);
            }
        }

        private bool HasExtension(ReadOnlySpan<byte> name)
        {
            fixed (byte* text = name)
                return _alcIsExtensionPresent(IntPtr.Zero, text) != 0;
        }

        private List<string> HrtfProfiles(IntPtr device)
        {
            var names = new List<string>();
            if (_alcGetStringiSOFT == null)
                return names;
            int count = 0;
            _alcGetIntegerv(device, AlcNumHrtfSpecifiers, 1, &count);
            for (int index = 0; index < count; index++)
                names.Add(Utf8(_alcGetStringiSOFT(device, AlcHrtfSpecifier, index)));
            return names;
        }

        private List<string> Resamplers(IntPtr device)
        {
            var names = new List<string>();
            if (_alGetStringiSOFT == null)
                return names;
            IntPtr context = _alcCreateContext(device, null);
            if (context == IntPtr.Zero)
                return names;
            try
            {
                _alcMakeContextCurrent(context);
                int count = _alGetInteger(AlNumResamplers);
                for (int index = 0; index < count; index++)
                    names.Add(Utf8(_alGetStringiSOFT(AlResamplerName, index)));
            }
            finally
            {
                _alcMakeContextCurrent(IntPtr.Zero);
                _alcDestroyContext(context);
            }
            return names;
        }
    }

    private static string Utf8(byte* text) => text == null ? "" : Marshal.PtrToStringUTF8((IntPtr)text) ?? "";

    /// <summary>A list of null-terminated names ending with an empty one, as OpenAL gives the devices.</summary>
    private static List<string> NameList(byte* list)
    {
        var names = new List<string>();
        if (list == null)
            return names;
        while (*list != 0)
        {
            int length = 0;
            while (list[length] != 0)
                length++;
            names.Add(Encoding.UTF8.GetString(list, length));
            list += length + 1;
        }
        return names;
    }
}
