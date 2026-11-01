using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StarterNG.Domain.Settings;

public enum SimulatorKeyKind { Toggle, Number, Text, Choice }

/// <summary>
/// An eu07.ini key the simulator reads that needs no logic of the starter's own:
/// a switch, a number or a word, shown as one row of the settings. The default
/// is the simulator's own, from Globals.h, used while the file does not carry
/// the key.
/// </summary>
public sealed record SimulatorKey(
    string Key,
    SimulatorKeyKind Kind,
    string Default,
    string Category,
    string Section,
    double Minimum = 0,
    double Maximum = 0,
    double Step = 1,
    string Unit = "",
    bool ZeroIsOff = false,
    int Repeat = 1,
    string[]? Words = null)
{
    /// <summary>The label's translation key; the explanation's adds "Desc".</summary>
    public string LabelKey => "Ini." + Key;

    public string DescriptionKey => LabelKey + ".Desc";

    /// <summary>The translation key of a word of a choice; the empty one leaves the key out of the file.</summary>
    public static string WordKey(string word) =>
        "IniWord." + (word.Length == 0 ? "same" : word.Replace(' ', '_'));

    /// <summary>The word of <paramref name="raw"/> among those offered, or else the default: the row then shows that and leaves the file alone.</summary>
    public string ParseChoice(string raw)
    {
        string word = string.Join(' ', raw.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Words is not null && Words.Contains(word) ? word : Default;
    }

    public static bool ParseToggle(string raw) =>
        FirstToken(raw).ToLowerInvariant() is "yes" or "true" or "1" or "tak";

    public static string FormatToggle(bool value) => value ? "yes" : "no";

    /// <summary>The key's value in <paramref name="s"/>, in the file's words: what is set, or else the default.</summary>
    public string RawIn(SimulatorSettings s) => s.Extra.TryGetValue(Key, out string? raw) ? raw : Default;

    public double ParseNumber(string raw) =>
        double.TryParse(FirstToken(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.Parse(Default, CultureInfo.InvariantCulture);

    /// <summary>
    /// Shake has a factor per axis; the row moves them together, so a number is
    /// written as many times as the key takes it.
    /// </summary>
    public string FormatNumber(double value)
    {
        string one = value.ToString("0.####", CultureInfo.InvariantCulture);
        return Repeat <= 1 ? one : string.Join(' ', Enumerable.Repeat(one, Repeat));
    }

    private static string FirstToken(string raw)
    {
        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 ? tokens[0] : string.Empty;
    }
}

public static class SimulatorKeys
{
    public const string General = "general";
    public const string Graphics = "graphics";
    public const string Sound = "sound";
    public const string Physics = "physics";
    public const string Advanced = "advanced";

    private const string NewRenderer = "GfxColNewRenderer";

    private static SimulatorKey Toggle(string key, bool value, string category, string section) =>
        new(key, SimulatorKeyKind.Toggle, SimulatorKey.FormatToggle(value), category, section);

    private static SimulatorKey Number(string key, double value, string category, string section,
                                       double min, double max, double step, string unit = "",
                                       bool zeroIsOff = false, int repeat = 1) =>
        new(key, SimulatorKeyKind.Number, value.ToString(CultureInfo.InvariantCulture), category, section,
            min, max, step, unit, zeroIsOff, repeat);

    private static SimulatorKey Text(string key, string category, string section) =>
        new(key, SimulatorKeyKind.Text, string.Empty, category, section);

    private static SimulatorKey Choice(string key, string value, string category, string section, params string[] words) =>
        new(key, SimulatorKeyKind.Choice, value, category, section, Words: words);

    public static SimulatorKey Named(string key) => All.First(k => k.Key == key);

    public static IReadOnlyList<SimulatorKey> All { get; } = new[]
    {
        Number("ui.scale", 1, General, "IniSectionInterface", 0.5, 3, 0.05, "×"),
        Number("ui.fontsize", 13, General, "IniSectionInterface", 8, 32, 1, " px"),
        Number("ui.bg.opacity", 0.94, General, "IniSectionInterface", 0.1, 1, 0.05),
        Toggle("gui.defaultwindows", true, General, "IniSectionInterface"),
        Toggle("gui.trainingdefault", false, General, "IniSectionInterface"),
        Toggle("gui.showtranscripts", true, General, "IniSectionInterface"),
        Toggle("loadinglog", false, General, "IniSectionInterface"),
        Toggle("captureonstart", true, General, "IniSectionInterface"),
        Toggle("mousecontrol", true, General, "IniSectionInterface"),
        Text("fullscreenmonitor", General, "IniSectionInterface"),
        Text("screenshotsdir", General, "IniSectionInterface"),

        Number("basedrawrange", 2500, Graphics, "IniSectionRanges", 500, 10000, 250, " m"),
        Number("heightbase", 1, Graphics, "IniSectionRanges", 0.5, 3, 0.05, "×"),
        Number("targetfps", 0, Graphics, "IniSectionRanges", 0, 240, 5, " FPS", zeroIsOff: true),
        Number("sleeperdistance", 250, Graphics, "IniSectionRanges", 0, 5000, 50, " m", zeroIsOff: true),
        Number("gfx.reflections.range_instances", 750, Graphics, "IniSectionRanges", 0, 3000, 50, " m"),
        Number("gfx.reflections.range_vehicles", 250, Graphics, "IniSectionRanges", 0, 2000, 50, " m"),
        Toggle("gfx.textures.streaming", false, Graphics, "IniSectionRanges"),
        Number("gfx.textures.releasedistance", 0, Graphics, "IniSectionRanges", 0, 10, 0.5, "×", zeroIsOff: true),
        Toggle("skyenabled", true, Graphics, "IniSectionRanges"),
        Toggle("rendercab", true, Graphics, "IniSectionRanges"),
        Number("gfx.postfx.motionblur.shutter", 0.01, Graphics, "IniSectionRanges", 0.001, 0.1, 0.001, " s"),
        Number("python.fpslimit", 100, Graphics, "IniSectionRanges", 5, 200, 5, " FPS"),
        Toggle("python.vsync", true, Graphics, "IniSectionRanges"),
        Toggle("python.displaywindows", false, Graphics, "IniSectionRanges"),
        Toggle("newaircouplers", true, Graphics, "IniSectionRanges"),
        Toggle("smoothtraction", true, Graphics, "IniSectionRanges"),

        // Read by the OpenGL 4.6 and Vulkan 1.4 simulators; the older ones pass them over.
        Choice("gfx.antialiasing", "", Graphics, NewRenderer, "", "taa", "dlss", "dlaa"),
        Choice("gfx.dlss.preset", "default", Graphics, NewRenderer, "default", "k", "j", "m", "l"),
        Choice("gfx.framegeneration", "off", Graphics, NewRenderer, "off", "x2", "x3", "x4"),
        Choice("gfx.reflex", "off", Graphics, NewRenderer, "off", "on", "boost"),
        Toggle("gfx.raytracing", false, Graphics, NewRenderer),
        Number("gfx.raytracing.range", 500, Graphics, NewRenderer, 10, 5000, 10, " m"),
        Text("gfx.vulkan.device", Graphics, NewRenderer),
        Toggle("gfx.lights.clustered", true, Graphics, NewRenderer),
        Choice("gfx.lights.shadows", "medium", Graphics, NewRenderer, "no", "low", "medium", "high"),
        Choice("gfx.contactshadows", "no", Graphics, NewRenderer, "no", "low", "medium", "high"),
        Toggle("gfx.shadow.sdsm", false, Graphics, NewRenderer),
        Choice("gfx.postfx.ao", "gtao", Graphics, NewRenderer, "no", "ssao", "gtao"),
        Choice("gfx.postfx.ao.quality", "3 4", Graphics, NewRenderer, "3 4", "4 6", "6 8"),
        Toggle("gfx.postfx.ao.lighting", true, Graphics, NewRenderer),
        Choice("gfx.ssr", "medium", Graphics, NewRenderer, "no", "low", "medium", "high"),
        Choice("gfx.cabprobe", "medium", Graphics, NewRenderer, "no", "low", "medium", "high"),
        Number("gfx.cabprobe.interval", 4, Graphics, NewRenderer, 1, 16, 1),
        Toggle("gfx.cabprobe.reflections", true, Graphics, NewRenderer),
        Number("gfx.cabprobe.bounce", 1, Graphics, NewRenderer, 0, 4, 0.25),
        Choice("gfx.fog", "volumetric", Graphics, NewRenderer, "analytic", "volumetric"),
        Choice("gfx.fog.quality", "medium", Graphics, NewRenderer, "low", "medium", "high", "ultra"),
        Choice("gfx.clouds", "medium", Graphics, NewRenderer, "no", "low", "medium", "high", "ultra"),
        Toggle("gfx.clouds.shadows", true, Graphics, NewRenderer),
        Toggle("gfx.grass", true, Graphics, NewRenderer),
        Number("gfx.grass.distance", 60, Graphics, NewRenderer, 5, 300, 5, " m"),
        Number("gfx.grass.fadedistance", 70, Graphics, NewRenderer, 10, 350, 10, " m"),
        Number("gfx.grass.density", 1, Graphics, NewRenderer, 0, 2, 0.25, "×"),
        Number("gfx.precipitation.particles", 65536, Graphics, NewRenderer, 0, 131072, 16384),
        Toggle("gfx.postfx.bloom", true, Graphics, NewRenderer),
        Toggle("gfx.postfx.autoexposure", true, Graphics, NewRenderer),

        Number("sound.maxsources", 30, Sound, "IniSectionAudio", 8, 256, 8),
        Toggle("sound.asyncload", false, Sound, "IniSectionAudio"),

        Toggle("loadtraction", true, Physics, "IniSectionPhysics"),
        Toggle("rollfix", true, Physics, "IniSectionPhysics"),
        Number("shakefactor", 1, Physics, "IniSectionPhysics", 0, 3, 0.1, "×", repeat: 3),
        Toggle("map.manualswitchcontrol", false, Physics, "IniSectionPhysics"),
        Number("map.highlightdistance", 3000, Physics, "IniSectionPhysics", 0, 10000, 250, " m", zeroIsOff: true),

        Toggle("showsystemconsole", false, Advanced, "IniSectionConsole"),
    };
}
