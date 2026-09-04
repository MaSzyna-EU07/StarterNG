using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application;

namespace StarterNG.Classes;

public enum BatteryDefault { Default = 0, AlwaysOff = 1, AlwaysOn = 2 }

public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform }

public sealed class Settings
{
    public static Settings Instance { get; } = new();

    public Action? CaptureFromUi { get; set; }

    public bool LanguageWasSet { get; private set; }

    public string Language = "English";
    public bool Fullscreen;
    public bool PauseWhenInactive = true;
    public bool PauseOnStart;
    public int CursorSensitivity = 2;
    public bool InvertMouseHorizontal;
    public bool InvertMouseVertical;

    public bool IgnoreGamepad;
    public int FeedbackMode = 1;
    public int FeedbackPort = 888;
    public bool UartEnabled;
    public string UartPort = "";
    public string UartTune = "";
    public bool UartDebug;
    public bool UartMain, UartScnd, UartTrain, UartLocal, UartRadioVolume, UartRadioChannel;

    public bool SelectExeAutomatically = true;
    public string ExecutablePath = "eu07.exe";
    public bool DebugMode;
    public bool VirtualShunting = true;
    public bool LogMissingVehicleFiles;

    public int RenderEngine;
    public int Width = 1280;
    public int Height = 720;
    public int BufferScalePercent = 100;
    public int MaxTextureSize = 4096;
    public int MaxCabTextureSize = 4096;
    public int TextureFiltering = 8;
    public int Multisampling = 2;
    public int DynamicLights = 7;
    public double DrawRangeFactor = 3.0;
    public bool VSync;
    public bool Smoke = true;
    public int SmokeFidelity = 1;
    public bool ChromaticAberration = true;
    public bool MotionBlur = true;
    public bool ExtraEffects = true;
    public bool EnvMap = true;
    public bool UseVbo = true;
    public bool RenderShadows = true;
    public int ReflectionsFramerate = 60;
    public int ShadowMapResolution = 2048;
    public int ShadowProjectionRange = 250;
    public int CabShadowsRange;
    public int ShadowRankCutoff = 3;
    public int ReflectionsFidelity;
    public int FieldOfView = 45;
    public bool PythonScreens = true;
    public bool PythonThreadedUpload = true;
    public bool FpsLimitEnabled;
    public int FpsLimit = 30;
    public double ShadowAngleLimit = 0.2;
    public bool FullscreenWindowed;
    public bool RenderAngleVulkan;

    public int SplineFidelity = 1;
    public bool FullPhysics = true;
    public bool EnableTraction = true;
    public bool LiveTraction = true;
    public bool PhysicsLog;
    public bool DebugLog = true;
    public bool MultipleLogs;
    public bool DisplaySimulation = true;
    public bool CrashDamage = true;
    public double Friction = 1.0;
    public double BrakeStep = 1.0;
    public double BrakeSpeed = 3.0;

    public bool SoundEnabled = true;
    public int Volume = 50;
    public int RadioVolume = 75;
    public int VehiclesVolume = 100;
    public int PositionalVolume = 100;
    public int AmbientVolume = 100;
    public bool SkipPipeline;
    public int PythonScreenUpdateRate = 200;
    public bool DebugLogVisible;
    private int _debugLogExtraBits;

    public bool CompressTextures = true;
    public bool ScaleSpeculars = true;
    public bool UseGLES;
    public bool ShaderGamma;
    public bool ScreenMipmaps = true;
    public bool ExtendedModelConversion;
    public bool GfxResourceMove;
    public bool GfxResourceSweep = true;
    public int TrainPhysicsThreads;
    public bool IgnoreIrrelevantTrains;

    public bool AutoCloseStarter;
    public bool LargeThumbnails;
    public bool AutoExpandSceneryTree;
    public BatteryDefault BatteryDefault = BatteryDefault.Default;

    public bool ShowAiVehicles = true;
    public bool DrivableOnly = true;
    public bool ShowArchivalSceneries = true;
    public bool HideArchivalVehicles = true;
    public string LastScenery = "";

    /// <summary>
    /// Starter's own window state, the way the old one kept WindowMaximized in
    /// starter.ini. Only the maximised flag is remembered - a normal window
    /// always opens at its design size, centred on the screen.
    /// </summary>
    public bool WindowMaximized;

    public static readonly string[] RenderEngines =
        { "full", "legacy", "simpleshader", "simple", "off", "experimental" };

    public static readonly int[] AnisotropySteps = { 1, 2, 4, 8, 16 };

    private double _mouseScaleYMagnitude = 0.2;
    private string[] _shadowTuneExtra = { "400", "300" };

    private ConfigFile _config = new();
    private string _savePath = string.Empty;
    private DateTime _configAgeUtc = DateTime.MinValue;

    public string LoadedFrom { get; private set; } = string.Empty;

    public string SavePath => _savePath;

    public static string UserConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "MaSzyna", "eu07.ini");
        }
        else if (OperatingSystem.IsMacOS())
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, "Library", "Application Support", "MaSzyna", "eu07.ini");
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".config", "MaSzyna", "eu07.ini");
        }

        return Path.Combine(AppContext.BaseDirectory, "eu07.ini");
    }

    private static string DefaultConfigPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), "eu07.ini");

    public static string UserConfigDirectory() =>
        Path.GetDirectoryName(UserConfigPath()) ?? AppContext.BaseDirectory;

    public string ResolveExecutable() => ResolveExecutable(out _);

    public string ResolveExecutable(out ExeProblem problem)
    {
        if (!SelectExeAutomatically && !string.IsNullOrWhiteSpace(ExecutablePath))
        {
            problem = ValidateExecutable(FullPath(ExecutablePath));
            return ExecutablePath;
        }

        string canonical = OperatingSystem.IsWindows() ? "eu07.exe" : "eu07";
        string? fallback = null;
        try
        {
            var candidates = Directory.GetFiles(Directory.GetCurrentDirectory(), "eu07*")
                .Where(IsExecutableCandidate)
                .OrderBy(p => string.Equals(Path.GetFileName(p), canonical, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => Path.GetFileName(p)!.Length);

            foreach (string path in candidates)
            {
                if (ValidateExecutable(path) == ExeProblem.None)
                {
                    problem = ExeProblem.None;
                    return path;
                }
                fallback ??= path;
            }
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log("looking for the simulator executable", ex);
        }

        if (fallback != null)
        {
            problem = ValidateExecutable(fallback);
            return fallback;
        }

        problem = ExeProblem.NotFound;
        return canonical;
    }

    private static string FullPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);

    public static ExeProblem ValidateExecutable(string path)
    {
        try
        {
            if (!File.Exists(path))
                return ExeProblem.NotFound;

            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode anyExecute =
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(path) & anyExecute) == 0)
                    return ExeProblem.NotExecutable;
            }

            byte[] head = new byte[4];
            using (var fs = File.OpenRead(path))
            {
                if (fs.Read(head, 0, 4) == 4)
                {
                    bool pe = head[0] == 0x4D && head[1] == 0x5A;
                    bool elf = head[0] == 0x7F && head[1] == 0x45 && head[2] == 0x4C && head[3] == 0x46;
                    if (OperatingSystem.IsWindows() && elf)
                        return ExeProblem.WrongPlatform;
                    if (OperatingSystem.IsLinux() && pe)
                        return ExeProblem.WrongPlatform;
                }
            }

            return ExeProblem.None;
        }
        catch
        {
            return ExeProblem.NotFound;
        }
    }

    public static List<string> ListCandidateExecutables(string? directory = null)
    {
        directory ??= Directory.GetCurrentDirectory();
        try
        {
            return Directory.GetFiles(directory, "eu07*")
                .Where(IsExecutableCandidate)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFileName)
                .ToList()!;
        }
        catch { return new List<string>(); }
    }

    private static bool IsExecutableCandidate(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".ini" or ".log" or ".txt" or ".cfg" or ".config" or ".json"
                or ".dll" or ".so" or ".dat" or ".bak" or ".csv" or ".xml")
            return false;
        if (OperatingSystem.IsWindows())
            return ext == ".exe";
        if (OperatingSystem.IsLinux())
            return ext.Length == 0 || ext is ".x86_64" or ".run" or ".appimage";

        return ext.Length == 0 || ext is ".exe" or ".x86_64" or ".run" or ".appimage";
    }

    private static readonly Encoding FileEncoding = ResolveEncoding();

    private static Encoding ResolveEncoding()
    {

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public void Load()
    {
        _savePath = UserConfigPath();
        string source = File.Exists(_savePath) ? _savePath : DefaultConfigPath();
        LoadedFrom = source;
        LoadFrom(source);
    }

    public void CaptureAndSave()
    {
        CaptureFromUi?.Invoke();
        Save();
    }

    public void LoadFrom(string path)
    {
        try
        {
            _config = File.Exists(path)
                ? ConfigFile.Parse(File.ReadAllText(path, FileEncoding))
                : new ConfigFile();
        }
        catch
        {
            _config = new ConfigFile();
        }

        ReadFromConfig();
        TouchConfigAge();
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(_savePath))
            _savePath = UserConfigPath();

        SaveTo(_savePath);
    }

    public void SaveTo(string path)
    {
        WriteToConfig();

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, _config.ToText(), FileEncoding);
            TouchConfigAge();
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log($"saving {path}", ex);
        }
    }

    public void TouchConfigAge()
    {
        string path = string.IsNullOrEmpty(_savePath) ? UserConfigPath() : _savePath;
        try
        {
            _configAgeUtc = File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.UtcNow;
        }
        catch
        {
            _configAgeUtc = DateTime.UtcNow;
        }
    }

    public bool IsConfigNewerOnDisk()
    {
        string path = string.IsNullOrEmpty(_savePath) ? UserConfigPath() : _savePath;
        try
        {
            if (!File.Exists(path) || _configAgeUtc == DateTime.MinValue)
                return false;
            return File.GetLastWriteTimeUtc(path) > _configAgeUtc + TimeSpan.FromSeconds(1);
        }
        catch
        {
            return false;
        }
    }

    public void DumpMissingVehicleLog()
    {
        if (!LogMissingVehicleFiles) return;
        try
        {
            var lines = AppServices.Current.MissingAssets.Scan(AppServices.Current.Library.Vehicles);
            if (lines.Count == 0) return;
            Infrastructure.Diagnostics.Log(lines);
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log("Missing vehicle log", ex);
        }
    }

    private void ReadFromConfig()
    {
        var c = _config;

        LanguageWasSet = c.Has("lang");
        Language = c.GetString("lang", "en").ToLowerInvariant() == "pl" ? "Polski" : "English";
        Fullscreen = c.GetBool("fullscreen", false);
        PauseWhenInactive = c.GetBool("inactivepause", true);
        PauseOnStart = c.GetBool("pause", false);

        var mouse = c.GetTokens("mousescale");
        if (mouse.Length >= 1 && double.TryParse(mouse[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double mx))
        {
            InvertMouseHorizontal = mx < 0;
            CursorSensitivity = Clamp((int)Math.Round(Math.Abs(mx)), 1, 5);
        }
        if (mouse.Length >= 2 && double.TryParse(mouse[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double my))
        {
            InvertMouseVertical = my < 0;
            _mouseScaleYMagnitude = Math.Abs(my);
        }

        IgnoreGamepad = !c.GetBool("input.gamepad", true);
        FeedbackMode = Clamp(c.GetInt("feedbackmode", 1), 0, 5);
        FeedbackPort = c.GetInt("feedbackport", 888);
        UartEnabled = c.Has("uart");
        UartPort = c.GetString("uart", "");
        UartTune = c.GetString("uarttune", "");
        UartDebug = c.GetBool("uartdebug", false);
        if (c.Has("uartfeature"))
        {
            var uartTokens = (c.GetRaw("uartfeature") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            var uf = new HashSet<string>(uartTokens, StringComparer.OrdinalIgnoreCase);
            UartMain = uf.Contains("main"); UartScnd = uf.Contains("scnd"); UartTrain = uf.Contains("train");
            UartLocal = uf.Contains("local"); UartRadioVolume = uf.Contains("radiovolume"); UartRadioChannel = uf.Contains("radiochannel");
        }
        else
        {
            UartMain = UartScnd = UartTrain = UartLocal = true;
            UartRadioVolume = UartRadioChannel = false;
        }

        SelectExeAutomatically = c.GetBool("starter.exe.auto", true);
        ExecutablePath = c.GetString("starter.exe.path", "eu07.exe");
        DebugMode = c.GetBool("debugmode", false);
        VirtualShunting = c.GetBool("ai.trainman", true);
        LogMissingVehicleFiles = c.GetBool("starter.logmissingvehicles", false);

        RenderEngine = IndexOf(RenderEngines, c.GetString("gfxrenderer", "full"), 0);
        Width = c.GetInt("width", 1280);
        Height = c.GetInt("height", 720);
        BufferScalePercent = Clamp(c.GetInt("starter.bufferscale", 100), 50, 200);
        MaxTextureSize = c.GetInt("maxtexturesize", 4096);
        MaxCabTextureSize = c.GetInt("maxcabtexturesize", 4096);
        TextureFiltering = c.GetInt("anisotropicfiltering", 8);
        Multisampling = Clamp(c.GetInt("multisampling", 2), 0, 3);
        DynamicLights = Clamp(c.GetInt("dynamiclights", 7), 0, 7);
        DrawRangeFactor = c.GetDouble("gfx.drawrange.factor.max", 3.0);
        VSync = c.GetBool("vsync", false);
        Smoke = c.GetBool("gfx.smoke", true);
        SmokeFidelity = Clamp(c.GetInt("gfx.smoke.fidelity", 1), 1, 4);
        ChromaticAberration = c.GetBool("gfx.postfx.chromaticaberration.enabled", true);
        MotionBlur = c.GetBool("gfx.postfx.motionblur.enabled", true);
        ExtraEffects = c.GetBool("gfx.extraeffects", true);
        EnvMap = c.GetBool("gfx.envmap.enabled", true);
        UseVbo = c.GetBool("usevbo", true);
        RenderShadows = c.GetBool("shadows", true);
        ReflectionsFramerate = c.GetInt("gfx.reflections.framerate", 60);

        var shadowTune = c.GetTokens("shadowtune");
        if (shadowTune.Length >= 1) ShadowMapResolution = ParseInt(shadowTune[0], 2048);
        if (shadowTune.Length >= 2) ShadowProjectionRange = ParseInt(shadowTune[1], 250);
        if (shadowTune.Length >= 4) _shadowTuneExtra = new[] { shadowTune[2], shadowTune[3] };

        CabShadowsRange = (int)Math.Round(c.GetDouble("gfx.shadows.cab.range", 0));
        ShadowRankCutoff = c.GetInt("gfx.shadow.rank.cutoff", 3);
        ReflectionsFidelity = Clamp(c.GetInt("gfx.reflections.fidelity", 0), 0, 2);
        FieldOfView = Clamp((int)Math.Round(c.GetDouble("fieldofview", 45)), 15, 75);
        PythonScreens = c.GetBool("python.enabled", true);
        PythonThreadedUpload = c.GetBool("python.threadedupload", true);
        FpsLimitEnabled = c.Has("fpslimit");
        FpsLimit = Clamp(c.GetInt("fpslimit", 30), 1, 100);
        ShadowAngleLimit = c.GetDouble("gfx.shadow.angle.min", 0.2);
        FullscreenWindowed = c.GetBool("fullscreenwindowed", false);
        RenderAngleVulkan = string.Equals(c.GetString("gfx.angleplatform", ""), "vulkan", StringComparison.OrdinalIgnoreCase);

        SplineFidelity = Clamp(c.GetInt("splinefidelity", 1), 1, 4);
        FullPhysics = c.GetBool("fullphysics", true);
        EnableTraction = c.GetBool("enabletraction", true);
        LiveTraction = c.GetBool("livetraction", true);
        PhysicsLog = c.GetBool("physicslog", false);
        int debugLog = c.GetInt("debuglog", 3);
        DebugLog = (debugLog & 1) != 0;
        DebugLogVisible = (debugLog & 2) != 0;
        _debugLogExtraBits = debugLog & ~3;
        MultipleLogs = c.GetBool("multiplelogs", false);
        DisplaySimulation = !c.GetBool("gfx.skiprendering", false);
        CrashDamage = c.GetBool("crashdamage", true);
        Friction = c.GetDouble("friction", 1.0);
        BrakeStep = c.GetDouble("brakestep", 1.0);
        BrakeSpeed = c.GetDouble("brakespeed", 3.0);

        SoundEnabled = c.GetBool("soundenabled", true);
        Volume = Clamp((int)Math.Round(c.GetDouble("sound.volume", 1.0) * 50), 1, 100);
        RadioVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.radio", 0.75) * 100), 1, 100);
        VehiclesVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.vehicle", 1.0) * 100), 1, 100);
        PositionalVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.positional", 1.0) * 100), 1, 100);
        AmbientVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.ambient", 1.0) * 100), 1, 100);
        SkipPipeline = c.GetBool("gfx.skippipeline", false);
        PythonScreenUpdateRate = c.Has("python.updatetime")
            ? Clamp(c.GetInt("python.updatetime", 200), 0, 10000)
            : MapOldPyScreenPriority(c.GetString("pyscreenrendererpriority", "normal"));

        CompressTextures = c.GetBool("compresstex", true);
        ScaleSpeculars = c.GetBool("scalespeculars", true);
        UseGLES = c.GetBool("gfx.usegles", false);
        ShaderGamma = c.GetBool("gfx.shadergamma", false);
        ScreenMipmaps = c.GetBool("python.mipmaps", true);
        ExtendedModelConversion = c.GetInt("convertmodels", 0) != 0;
        GfxResourceMove = c.GetBool("gfx.resource.move", false);
        GfxResourceSweep = c.GetBool("gfx.resource.sweep", true);
        TrainPhysicsThreads = Clamp(c.GetInt("async.trainThreads", 0), 0, Environment.ProcessorCount);
        IgnoreIrrelevantTrains = c.GetBool("starter.ignoreirrelevant", false);

        AutoCloseStarter = c.GetBool("starter.autoclose", false);
        LargeThumbnails = c.GetBool("starter.largethumbnails", false);
        AutoExpandSceneryTree = c.GetBool("starter.expandtree", false);
        BatteryDefault = (BatteryDefault)Clamp(c.GetInt("starter.batterydefault", 0), 0, 2);
        ShowAiVehicles = c.GetBool("starter.show.ai", true);
        DrivableOnly = c.GetBool("starter.drivableonly", true);
        ShowArchivalSceneries = c.GetBool("starter.show.archival", true);
        HideArchivalVehicles = c.GetBool("starter.hide.archivalvehicles", true);
        LastScenery = c.GetString("starter.last.scenery", "");
        WindowMaximized = c.GetBool("starter.window.maximized", false);
    }

    private static readonly string[] ObsoleteKeys =
    {
        "gfx.postfx.tonemapping",
    };

    private void WriteToConfig()
    {
        var c = _config;

        foreach (string key in ObsoleteKeys)
            c.Remove(key);

        c.Set("lang", Language == "Polski" ? "pl" : "en");
        c.SetBool("fullscreen", Fullscreen);
        c.SetBool("inactivepause", PauseWhenInactive);
        c.SetBool("pause", PauseOnStart);

        double mx = CursorSensitivity * (InvertMouseHorizontal ? -1 : 1);
        double my = _mouseScaleYMagnitude * (InvertMouseVertical ? -1 : 1);
        c.Set("mousescale",
            $"{mx.ToString("0.###", CultureInfo.InvariantCulture)} " +
            $"{my.ToString("0.###", CultureInfo.InvariantCulture)}");

        c.SetBool("input.gamepad", !IgnoreGamepad);
        c.SetInt("feedbackmode", FeedbackMode);
        c.SetInt("feedbackport", FeedbackPort);
        if (UartEnabled && !string.IsNullOrWhiteSpace(UartPort)) c.Set("uart", UartPort); else c.Remove("uart");
        c.Set("uarttune", UartTune);
        c.SetBool("uartdebug", UartDebug);
        var sb = new StringBuilder();
        if (UartMain) sb.Append("main|");
        if (UartScnd) sb.Append("scnd|");
        if (UartTrain) sb.Append("train|");
        if (UartLocal) sb.Append("local|");
        if (UartRadioVolume) sb.Append("radiovolume|");
        if (UartRadioChannel) sb.Append("radiochannel|");
        c.Set("uartfeature", sb.ToString());

        c.SetBool("starter.exe.auto", SelectExeAutomatically);
        c.Set("starter.exe.path", ExecutablePath);
        c.SetBool("debugmode", DebugMode);
        c.SetBool("ai.trainman", VirtualShunting);
        c.SetBool("starter.logmissingvehicles", LogMissingVehicleFiles);

        c.Set("gfxrenderer", RenderEngines[Clamp(RenderEngine, 0, RenderEngines.Length - 1)]);
        c.SetInt("width", Width);
        c.SetInt("height", Height);
        c.SetInt("starter.bufferscale", BufferScalePercent);
        int fbw = (int)Math.Round(Width * BufferScalePercent / 100.0);
        int fbh = (int)Math.Round(Height * BufferScalePercent / 100.0);
        c.SetInt("gfx.framebuffer.width", fbw);
        c.SetInt("gfx.framebuffer.height", fbh);
        c.SetInt("maxtexturesize", MaxTextureSize);
        c.SetInt("maxcabtexturesize", MaxCabTextureSize);
        c.SetInt("anisotropicfiltering", TextureFiltering);
        c.SetInt("multisampling", Multisampling);
        c.SetInt("dynamiclights", DynamicLights);
        c.SetDouble("gfx.drawrange.factor.max", DrawRangeFactor);
        c.SetBool("vsync", VSync);
        c.SetBool("gfx.smoke", Smoke);
        c.SetInt("gfx.smoke.fidelity", SmokeFidelity);
        c.SetBool("gfx.postfx.chromaticaberration.enabled", ChromaticAberration);
        c.SetBool("gfx.postfx.motionblur.enabled", MotionBlur);
        c.SetBool("gfx.extraeffects", ExtraEffects);
        c.SetBool("gfx.envmap.enabled", EnvMap);
        c.SetBool("usevbo", UseVbo);
        c.SetBool("shadows", RenderShadows);
        c.SetBool("gfx.shadowmap.enabled", RenderShadows);
        c.SetInt("gfx.reflections.framerate", ReflectionsFramerate);
        c.Set("shadowtune",
            $"{ShadowMapResolution} {ShadowProjectionRange} " +
            $"{_shadowTuneExtra[0]} {_shadowTuneExtra[1]}");
        c.SetInt("gfx.shadows.cab.range", CabShadowsRange);
        c.SetInt("gfx.shadow.rank.cutoff", ShadowRankCutoff);
        c.SetInt("gfx.reflections.fidelity", ReflectionsFidelity);
        c.SetInt("fieldofview", FieldOfView);
        c.SetBool("python.enabled", PythonScreens);
        c.SetBool("python.threadedupload", PythonThreadedUpload);
        if (FpsLimitEnabled) c.SetInt("fpslimit", FpsLimit); else c.Remove("fpslimit");
        c.SetDouble("gfx.shadow.angle.min", ShadowAngleLimit);
        c.SetBool("fullscreenwindowed", FullscreenWindowed);
        if (RenderAngleVulkan) c.Set("gfx.angleplatform", "vulkan"); else c.Remove("gfx.angleplatform");

        c.SetInt("splinefidelity", SplineFidelity);
        c.SetBool("fullphysics", FullPhysics);
        c.SetBool("enabletraction", EnableTraction);
        c.SetBool("livetraction", LiveTraction);
        c.SetBool("physicslog", PhysicsLog);
        c.SetInt("debuglog",
            (DebugLog ? 1 : 0) | (DebugLogVisible ? 2 : 0) | _debugLogExtraBits);
        c.SetBool("multiplelogs", MultipleLogs);
        c.SetBool("gfx.skiprendering", !DisplaySimulation);
        c.SetBool("crashdamage", CrashDamage);
        c.SetDouble("friction", Friction);
        c.SetDouble("brakestep", BrakeStep);
        c.SetDouble("brakespeed", BrakeSpeed);

        c.SetBool("soundenabled", SoundEnabled);
        c.SetDouble("sound.volume", Volume / 50.0);
        c.SetDouble("sound.volume.radio", RadioVolume / 100.0);
        c.SetDouble("sound.volume.vehicle", VehiclesVolume / 100.0);
        c.SetDouble("sound.volume.positional", PositionalVolume / 100.0);
        c.SetDouble("sound.volume.ambient", AmbientVolume / 100.0);
        c.SetBool("gfx.skippipeline", SkipPipeline);
        c.SetInt("python.updatetime", PythonScreenUpdateRate);

        c.SetBool("compresstex", CompressTextures);
        c.SetBool("scalespeculars", ScaleSpeculars);
        c.SetBool("gfx.usegles", UseGLES);
        c.SetBool("gfx.shadergamma", ShaderGamma);
        c.SetBool("python.mipmaps", ScreenMipmaps);
        c.Set("convertmodels", ExtendedModelConversion ? "143" : "0");
        c.SetBool("gfx.resource.move", GfxResourceMove);
        c.SetBool("gfx.resource.sweep", GfxResourceSweep);
        c.SetInt("async.trainThreads", TrainPhysicsThreads);
        c.SetBool("starter.ignoreirrelevant", IgnoreIrrelevantTrains);

        c.SetBool("starter.autoclose", AutoCloseStarter);
        c.SetBool("starter.largethumbnails", LargeThumbnails);
        c.SetBool("starter.expandtree", AutoExpandSceneryTree);
        c.SetInt("starter.batterydefault", (int)BatteryDefault);
        c.SetBool("starter.show.ai", ShowAiVehicles);
        c.SetBool("starter.drivableonly", DrivableOnly);
        c.SetBool("starter.show.archival", ShowArchivalSceneries);
        c.SetBool("starter.hide.archivalvehicles", HideArchivalVehicles);
        if (!string.IsNullOrWhiteSpace(LastScenery))
            c.Set("starter.last.scenery", LastScenery);
        else
            c.Remove("starter.last.scenery");
        c.SetBool("starter.window.maximized", WindowMaximized);
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    private static int IndexOf(string[] values, string token, int fallback)
    {
        for (int i = 0; i < values.Length; i++)
            if (string.Equals(values[i], token, StringComparison.OrdinalIgnoreCase))
                return i;
        return fallback;
    }

    private static int MapOldPyScreenPriority(string priority) =>
        priority.ToLowerInvariant() switch
        {
            "lower" => 400,
            "lowest" => 800,
            "idle" => 1600,
            "off" => 0,
            _ => 200
        };
}
