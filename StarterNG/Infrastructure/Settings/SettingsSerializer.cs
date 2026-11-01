using System;
using System.Globalization;
using System.Linq;
using System.Text;
using StarterNG.Classes;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Settings;

public sealed class SettingsSerializer
{
    public void Read(SimulatorSettings s, ConfigFile c)
    {
        s.LanguageWasSet = c.Has("lang");
        s.LanguageCode = c.GetString("lang", "");
        s.Language = s.LanguageCode;
        s.Fullscreen = c.GetBool("fullscreen", false);
        s.PauseWhenInactive = c.GetBool("inactivepause", true);
        s.NetworkServer = c.Has("network.server");
        s.FpsAverage = c.GetDouble("fpsaverage", 0);
        s.PauseOnStart = c.GetBool("pause", false);

        var mouse = c.GetTokens("mousescale");
        if (mouse.Length >= 1 && double.TryParse(mouse[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double mx))
        {
            s.InvertMouseHorizontal = mx < 0;
            s.MouseScaleXMagnitude = Math.Abs(mx);
            s.CursorSensitivity = SensitivityStep(mx);
        }
        if (mouse.Length >= 2 && double.TryParse(mouse[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double my))
        {
            s.InvertMouseVertical = my < 0;
            s.MouseScaleYMagnitude = Math.Abs(my);
        }

        s.IgnoreGamepad = !c.GetBool("input.gamepad", true);
        s.FeedbackMode = Clamp(c.GetInt("feedbackmode", 1), 0, 5);
        s.FeedbackPort = c.GetInt("feedbackport", 888);
        s.UartEnabled = c.Has("uart");
        var uart = c.GetTokens("uart");
        s.UartPort = uart.ElementAtOrDefault(0) ?? "";
        s.UartBaud = ParseInt(uart.ElementAtOrDefault(1) ?? "", 0) is > 0 and var baud ? baud : Uart.DefaultBaud;
        s.UartUpdateTime = uart.Length >= 3 &&
                           double.TryParse(uart[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double every) &&
                           every >= 0
            ? every
            : Uart.DefaultUpdateTime;
        s.UartTune = Uart.TuneInFile(c.GetString("uarttune", ""));
        s.UartDebug = c.GetBool("uartdebug", false);
        if (c.Has("uartfeature"))
        {
            var uf = Uart.FeaturesOn(c.GetRaw("uartfeature"));
            s.UartMain = uf.Contains("main"); s.UartScnd = uf.Contains("scnd"); s.UartTrain = uf.Contains("train");
            s.UartLocal = uf.Contains("local"); s.UartRadioVolume = uf.Contains("radiovolume"); s.UartRadioChannel = uf.Contains("radiochannel");
            s.UartDynamicBrake = uf.Contains("dynamicbrake");
        }
        else
        {
            s.UartMain = s.UartScnd = s.UartTrain = s.UartLocal = true;
            s.UartRadioVolume = s.UartRadioChannel = s.UartDynamicBrake = false;
        }

        s.SelectExeAutomatically = c.GetBool("starter.exe.auto", true);
        s.ExecutablePath = c.GetString("starter.exe.path", "eu07.exe");
        s.DebugMode = c.GetBool("debugmode", false);
        s.VirtualShunting = c.GetBool("ai.trainman", true);
        s.LogMissingVehicleFiles = c.GetBool("starter.logmissingvehicles", false);

        s.RenderEngine = SimulatorSettings.RenderEngineOf(c.GetString("gfxrenderer", "full"));
        s.Width = c.GetInt("width", 1280);
        s.Height = c.GetInt("height", 720);
        s.BufferScalePercent = ReadBufferScale(c);
        s.MaxTextureSize = c.GetInt("maxtexturesize", 4096);
        s.MaxCabTextureSize = c.GetInt("maxcabtexturesize", 4096);
        s.TextureFiltering = Math.Max(1, c.GetDouble("anisotropicfiltering", 8));
        s.Multisampling = Clamp(c.GetInt("multisampling", 2), 0, 4);
        s.DynamicLights = Clamp(c.GetInt("dynamiclights", 7), 0, 7);
        s.DrawRangeFactor = c.GetDouble("gfx.drawrange.factor.max", 3.0);
        s.VSync = c.GetBool("vsync", false);
        s.Smoke = c.GetBool("gfx.smoke", true);
        s.SmokeFidelity = Clamp(c.GetInt("gfx.smoke.fidelity", 1), 1, 4);
        s.ChromaticAberration = c.GetBool("gfx.postfx.chromaticaberration.enabled", true);
        s.MotionBlur = c.GetBool("gfx.postfx.motionblur.enabled", true);
        s.ExtraEffects = c.GetBool("gfx.extraeffects", true);
        s.EnvMap = c.GetBool("gfx.envmap.enabled", true);
        s.UseVbo = c.GetBool("usevbo", true);
        s.RenderShadows = c.GetBool("shadows", true);
        // A negative rate counts as its size; none at all, as never.
        double reflections = Math.Abs(c.GetDouble("gfx.reflections.framerate", 1 / 300.0));
        s.ReflectionsFramerate = reflections > 0 ? reflections : 1 / 300.0;

        // map size, unread, projection range, unread
        var shadowTune = c.GetTokens("shadowtune");
        if (shadowTune.Length >= 1) s.ShadowMapResolution = ParseInt(shadowTune[0], 2048);
        if (shadowTune.Length >= 3) s.ShadowProjectionRange = (int)Math.Round(ParseDouble(shadowTune[2], 250));
        if (shadowTune.Length >= 2) s.ShadowTuneUnused[0] = shadowTune[1];
        if (shadowTune.Length >= 4) s.ShadowTuneUnused[1] = shadowTune[3];

        s.CabShadowsRange = (int)Math.Round(c.GetDouble("gfx.shadows.cab.range", 0));
        s.ShadowRankCutoff = c.GetInt("gfx.shadow.rank.cutoff", 3);
        s.ReflectionsFidelity = Clamp(c.GetInt("gfx.reflections.fidelity", 0), 0, 2);
        s.FieldOfView = Clamp((int)Math.Round(c.GetDouble("fieldofview", 45)), 10, 75);
        s.PythonScreens = c.GetBool("python.enabled", true);
        s.PythonThreadedUpload = c.GetBool("python.threadedupload", true);
        // The simulator takes any rate above zero; at or below it, no limit.
        double fpsLimit = c.GetDouble("fpslimit", 0);
        s.FpsLimitEnabled = fpsLimit > 0;
        if (s.FpsLimitEnabled) s.FpsLimit = fpsLimit;
        s.ShadowAngleLimit = c.GetDouble("gfx.shadow.angle.min", 0.2);
        s.FullscreenWindowed = c.GetBool("fullscreenwindowed", false);
        s.RenderAngleVulkan = string.Equals(c.GetString("gfx.angleplatform", ""), "vulkan", StringComparison.OrdinalIgnoreCase);

        s.SplineFidelity = Clamp(c.GetInt("splinefidelity", 1), 1, 4);
        s.FullPhysics = c.GetBool("fullphysics", true);
        s.EnableTraction = c.GetBool("enabletraction", true);
        s.LiveTraction = c.GetBool("livetraction", true);
        s.PhysicsLog = c.GetBool("physicslog", false);
        int debugLog = c.GetInt("debuglog", 3);
        s.DebugLog = (debugLog & 1) != 0;
        s.DebugLogVisible = (debugLog & 2) != 0;
        s.DebugLogExtraBits = debugLog & ~3;
        s.MultipleLogs = c.GetBool("multiplelogs", false);
        s.DisplaySimulation = !c.GetBool("gfx.skiprendering", false);
        s.CrashDamage = c.GetBool("crashdamage", true);
        s.Friction = c.GetDouble("friction", 1.0);
        s.BrakeStep = c.GetDouble("brakestep", 1.0);
        s.BrakeSpeed = c.GetDouble("brakespeed", 3.0);

        s.SoundEnabled = c.GetBool("soundenabled", true);
        s.Volume = Clamp((int)Math.Round(c.GetDouble("sound.volume", 1.0) * 50), 0, 100);
        s.RadioVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.radio", 0.75) * 100), 0, 100);
        s.VehiclesVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.vehicle", 1.0) * 100), 0, 100);
        s.PositionalVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.positional", 1.0) * 100), 0, 100);
        s.AmbientVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.ambient", 1.0) * 100), 0, 100);
        s.SkipPipeline = c.GetBool("gfx.skippipeline", false);
        s.PythonScreenUpdateRate = c.Has("python.updatetime")
            ? Clamp(c.GetInt("python.updatetime", 200), 0, 10000)
            : MapOldPyScreenPriority(c.GetString("pyscreenrendererpriority", "normal"));

        s.CompressTextures = c.GetBool("compresstex", true);
        s.ScaleSpeculars = c.GetBool("scalespeculars", true);
        s.UseGLES = c.GetBool("gfx.usegles", false);
        s.ShaderGamma = c.GetBool("gfx.shadergamma", false);
        s.ScreenMipmaps = c.GetBool("python.mipmaps", true);
        s.ExtendedModelConversion = c.GetInt("convertmodels", 0) != 0;
        s.GfxResourceMove = c.GetBool("gfx.resource.move", false);
        s.GfxResourceSweep = c.GetBool("gfx.resource.sweep", true);
        s.TrainPhysicsThreads = Clamp(c.GetInt("async.trainThreads", 0), 0, Environment.ProcessorCount);
        s.IgnoreIrrelevantTrains = c.GetBool("starter.ignoreirrelevant", false);

        s.AutoCloseStarter = c.GetBool("starter.autoclose", false);
        s.LargeThumbnails = c.GetBool("starter.largethumbnails", false);
        s.AutoExpandSceneryTree = c.GetBool("starter.expandtree", false);
        s.CpuNotice = c.GetBool("starter.cpunotice", true);
        s.Gpu = string.Equals(c.GetString("starter.gpu", "discrete"), "system", StringComparison.OrdinalIgnoreCase)
            ? GpuPreference.System
            : GpuPreference.Discrete;
        // An unrecognised value means "do not interfere" rather than the nearest
        // state: clamping upwards would silently force every consist ready.
        s.ReadyOverride = c.GetInt("starter.batterydefault", 0) switch
        {
            1 => ConsistReadyOverride.AlwaysCold,
            2 => ConsistReadyOverride.AlwaysReady,
            _ => ConsistReadyOverride.Scenery
        };
        s.ShowAiVehicles = c.GetBool("starter.show.ai", true);
        s.DrivableOnly = c.GetBool("starter.drivableonly", true);
        s.ShowArchivalSceneries = c.GetBool("starter.show.archival", true);
        s.HideArchivalVehicles = c.GetBool("starter.hide.archivalvehicles", true);
        s.LastScenery = c.GetString("starter.last.scenery", "");
        s.WindowMaximized = c.GetBool("starter.window.maximized", false);

        s.Extra.Clear();
        foreach (var key in SimulatorKeys.All)
            if (c.Has(key.Key))
                s.Extra[key.Key] = c.GetRaw(key.Key) ?? string.Empty;
    }

    /// <summary>
    /// Keys the simulator reads a set number of words after, whatever stands there:
    /// one the file holds short takes the next keys in, so a save puts it right
    /// even where nobody changed it.
    /// </summary>
    public static readonly string[] FixedLengthKeys = { "uart", "uarttune", "uartfeature" };

    /// <summary>Keys written only when they say something, besides those of <see cref="SimulatorKeys"/>.</summary>
    public static readonly string[] OptionalKeys = { "fpslimit", "gfx.angleplatform" };

    /// <summary>Keys no simulator reads any more, taken out of the file on a save.</summary>
    public static readonly string[] ObsoleteKeys =
    {
        "gfx.postfx.tonemapping",
        "starter.developertools",
    };

    public void Write(SimulatorSettings s, ConfigFile c)
    {
        c.Set("lang", string.IsNullOrWhiteSpace(s.LanguageCode)
            ? "en"
            : s.LanguageCode.Trim().ToLowerInvariant());
        c.SetBool("fullscreen", s.Fullscreen);
        c.SetBool("inactivepause", s.PauseWhenInactive);
        c.SetBool("pause", s.PauseOnStart);

        double magnitude = SensitivityStep(s.MouseScaleXMagnitude) == s.CursorSensitivity
            ? s.MouseScaleXMagnitude
            : s.CursorSensitivity;
        double mx = magnitude * (s.InvertMouseHorizontal ? -1 : 1);
        double my = s.MouseScaleYMagnitude * (s.InvertMouseVertical ? -1 : 1);
        c.Set("mousescale",
            $"{mx.ToString("R", CultureInfo.InvariantCulture)} " +
            $"{my.ToString("R", CultureInfo.InvariantCulture)}");

        c.SetBool("input.gamepad", !s.IgnoreGamepad);
        c.SetInt("feedbackmode", s.FeedbackMode);
        c.SetInt("feedbackport", s.FeedbackPort);
        // The simulator reads three words after uart and eighteen numbers after
        // uarttune, whatever stands there - with fewer, the next keys of the file
        // go in their place.
        if (s.UartEnabled && Uart.IsPortName(s.UartPort) && s.UartBaud > 0 && s.UartUpdateTime >= 0)
            c.Set("uart", $"{s.UartPort.Trim()} {s.UartBaud.ToString(CultureInfo.InvariantCulture)} " +
                          s.UartUpdateTime.ToString("0.###", CultureInfo.InvariantCulture));
        if (Uart.NormalizeTune(s.UartTune) is { } tune) c.Set("uarttune", tune);
        c.SetBool("uartdebug", s.UartDebug);
        var sb = new StringBuilder();
        if (s.UartMain) sb.Append("main|");
        if (s.UartScnd) sb.Append("scnd|");
        if (s.UartTrain) sb.Append("train|");
        if (s.UartLocal) sb.Append("local|");
        if (s.UartRadioVolume) sb.Append("radiovolume|");
        if (s.UartRadioChannel) sb.Append("radiochannel|");
        if (s.UartDynamicBrake) sb.Append("dynamicbrake|");
        // Left bare, the key would have the simulator take the next four words for
        // its old yes/no form.
        c.Set("uartfeature", sb.Length > 0 ? sb.ToString() : Uart.NoFeatures);

        c.SetBool("starter.exe.auto", s.SelectExeAutomatically);
        c.Set("starter.exe.path", s.ExecutablePath);
        c.SetBool("debugmode", s.DebugMode);
        c.SetBool("ai.trainman", s.VirtualShunting);
        c.SetBool("starter.logmissingvehicles", s.LogMissingVehicleFiles);

        c.Set("gfxrenderer", SimulatorSettings.RenderEngines[Clamp(s.RenderEngine, 0, SimulatorSettings.RenderEngines.Length - 1)]);
        c.SetInt("width", s.Width);
        c.SetInt("height", s.Height);
        c.SetInt("starter.bufferscale", s.BufferScalePercent);
        int fbw = (int)Math.Round(s.Width * s.BufferScalePercent / 100.0);
        int fbh = (int)Math.Round(s.Height * s.BufferScalePercent / 100.0);
        // The size for the simulators before gfx.buffer.percent, which pass that one over and the other way round.
        c.SetInt("gfx.framebuffer.width", fbw);
        c.SetInt("gfx.framebuffer.height", fbh);
        c.SetInt("gfx.buffer.percent", s.BufferScalePercent);
        c.SetInt("maxtexturesize", s.MaxTextureSize);
        c.SetInt("maxcabtexturesize", s.MaxCabTextureSize);
        c.SetDouble("anisotropicfiltering", s.TextureFiltering);
        c.SetInt("multisampling", s.Multisampling);
        c.SetInt("dynamiclights", s.DynamicLights);
        c.SetDouble("gfx.drawrange.factor.max", s.DrawRangeFactor);
        c.SetBool("vsync", s.VSync);
        c.SetBool("gfx.smoke", s.Smoke);
        c.SetInt("gfx.smoke.fidelity", s.SmokeFidelity);
        c.SetBool("gfx.postfx.chromaticaberration.enabled", s.ChromaticAberration);
        c.SetBool("gfx.postfx.motionblur.enabled", s.MotionBlur);
        c.SetBool("gfx.extraeffects", s.ExtraEffects);
        c.SetBool("gfx.envmap.enabled", s.EnvMap);
        c.SetBool("usevbo", s.UseVbo);
        c.SetBool("shadows", s.RenderShadows);
        c.SetBool("gfx.shadowmap.enabled", s.RenderShadows);
        c.Set("gfx.reflections.framerate", s.ReflectionsFramerate.ToString("0.######", CultureInfo.InvariantCulture));
        c.Set("shadowtune",
            $"{s.ShadowMapResolution} {s.ShadowTuneUnused[0]} " +
            $"{s.ShadowProjectionRange} {s.ShadowTuneUnused[1]}");
        c.SetInt("gfx.shadows.cab.range", s.CabShadowsRange);
        c.SetInt("gfx.shadow.rank.cutoff", s.ShadowRankCutoff);
        c.SetInt("gfx.reflections.fidelity", s.ReflectionsFidelity);
        c.SetInt("fieldofview", s.FieldOfView);
        c.SetBool("python.enabled", s.PythonScreens);
        c.SetBool("python.threadedupload", s.PythonThreadedUpload);
        if (s.FpsLimitEnabled) c.SetDouble("fpslimit", s.FpsLimit);
        c.SetDouble("gfx.shadow.angle.min", s.ShadowAngleLimit);
        c.SetBool("fullscreenwindowed", s.FullscreenWindowed);
        if (s.RenderAngleVulkan) c.Set("gfx.angleplatform", "vulkan");

        c.SetInt("splinefidelity", s.SplineFidelity);
        c.SetBool("fullphysics", s.FullPhysics);
        c.SetBool("enabletraction", s.EnableTraction);
        c.SetBool("livetraction", s.LiveTraction);
        c.SetBool("physicslog", s.PhysicsLog);
        c.SetInt("debuglog",
            (s.DebugLog ? 1 : 0) | (s.DebugLogVisible ? 2 : 0) | s.DebugLogExtraBits);
        c.SetBool("multiplelogs", s.MultipleLogs);
        c.SetBool("gfx.skiprendering", !s.DisplaySimulation);
        c.SetBool("crashdamage", s.CrashDamage);
        c.SetDouble("friction", s.Friction);
        c.SetDouble("brakestep", s.BrakeStep);
        c.SetDouble("brakespeed", s.BrakeSpeed);

        c.SetBool("soundenabled", s.SoundEnabled);
        c.SetDouble("sound.volume", s.Volume / 50.0);
        c.SetDouble("sound.volume.radio", s.RadioVolume / 100.0);
        c.SetDouble("sound.volume.vehicle", s.VehiclesVolume / 100.0);
        c.SetDouble("sound.volume.positional", s.PositionalVolume / 100.0);
        c.SetDouble("sound.volume.ambient", s.AmbientVolume / 100.0);
        c.SetBool("gfx.skippipeline", s.SkipPipeline);
        c.SetInt("python.updatetime", s.PythonScreenUpdateRate);

        c.SetBool("compresstex", s.CompressTextures);
        c.SetBool("scalespeculars", s.ScaleSpeculars);
        c.SetBool("gfx.usegles", s.UseGLES);
        c.SetBool("gfx.shadergamma", s.ShaderGamma);
        c.SetBool("python.mipmaps", s.ScreenMipmaps);
        c.Set("convertmodels", s.ExtendedModelConversion ? "143" : "0");
        c.SetBool("gfx.resource.move", s.GfxResourceMove);
        c.SetBool("gfx.resource.sweep", s.GfxResourceSweep);
        c.SetInt("async.trainThreads", s.TrainPhysicsThreads);
        c.SetBool("starter.ignoreirrelevant", s.IgnoreIrrelevantTrains);

        c.SetBool("starter.autoclose", s.AutoCloseStarter);
        c.SetBool("starter.largethumbnails", s.LargeThumbnails);
        c.SetBool("starter.expandtree", s.AutoExpandSceneryTree);
        c.SetBool("starter.cpunotice", s.CpuNotice);
        c.Set("starter.gpu", s.Gpu == GpuPreference.System ? "system" : "discrete");
        c.SetInt("starter.batterydefault", (int)s.ReadyOverride);
        c.SetBool("starter.show.ai", s.ShowAiVehicles);
        c.SetBool("starter.drivableonly", s.DrivableOnly);
        c.SetBool("starter.show.archival", s.ShowArchivalSceneries);
        c.SetBool("starter.hide.archivalvehicles", s.HideArchivalVehicles);
        if (!string.IsNullOrWhiteSpace(s.LastScenery))
            c.Set("starter.last.scenery", s.LastScenery);
        c.SetBool("starter.window.maximized", s.WindowMaximized);

        // A key left bare would take the next line's key for its value, so an
        // emptied one goes.
        foreach (var key in SimulatorKeys.All)
            if (s.Extra.TryGetValue(key.Key, out string? value) && !string.IsNullOrWhiteSpace(value))
                c.Set(key.Key, value.Trim());
    }

    // What the simulator goes by: up to 4 a part of the window, above it percents.
    private static int ReadBufferScale(ConfigFile c)
    {
        if (!c.Has("gfx.buffer.percent"))
            return Clamp(c.GetInt("starter.bufferscale", 100), 25, 200);

        double value = c.GetDouble("gfx.buffer.percent", 100);
        return Clamp((int)Math.Round(value > 4 ? value : value * 100), 25, 200);
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static int SensitivityStep(double mouseScale) =>
        (int)Math.Clamp(Math.Round(Math.Abs(mouseScale)), 1, 5);

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;


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
