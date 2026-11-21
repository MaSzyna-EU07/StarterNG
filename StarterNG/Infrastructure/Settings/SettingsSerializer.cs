using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StarterNG.Classes;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Settings;

public sealed class SettingsSerializer
{
    /// <summary>
    /// An ini of before the tapes of the trains, its recording turned on ("blackbox yes", with the physics log of every
    /// vehicle, hundreds of files on a big scenery): moved to the tapes of the trains, the log of every vehicle off.
    /// Once: saved, the line of the trains' tapes is in the file, even turned off again before. A change of the
    /// starter's, after the file is read, so saving writes it.
    /// </summary>
    public static void MoveToTrainTapes(SimulatorSettings s)
    {
        if (s.PhysicsLogTrainsInIni || !s.BlackBoxInIni || !s.BlackBox)
            return;
        s.PhysicsLogTrains = true;
        s.PhysicsLogTrainsInIni = true;
        s.PhysicsLog = false;
    }

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

        // A zero (or NaN/Infinity) scale locks that axis in the simulator: the
        // camera then turns only one way. Nothing in the starter writes such a
        // number on purpose, so X takes the default. Of Y only the sign is read:
        // it is written 0.5 or -0.5, as the old starter always wrote it.
        var mouse = c.GetTokens("mousescale");
        if (mouse.Length >= 1 && TryParseMouseScale(mouse[0], out double mx))
        {
            s.InvertMouseHorizontal = mx < 0;
            s.MouseScaleXMagnitude = Math.Abs(mx);
            s.CursorSensitivity = SensitivityStep(mx);
        }
        if (mouse.Length >= 1)
            s.InvertMouseVertical = mouse.Length >= 2 && TryParseMouseScale(mouse[1], out double my) && my < 0;

        s.IgnoreGamepad = !c.GetBool("input.gamepad", false);
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

        s.RenderEngine = SimulatorSettings.RenderEngineOf(c.GetString("gfxrenderer", "default"));
        s.Width = c.GetInt("width", 1280);
        s.Height = c.GetInt("height", 720);
        s.BufferScalePercent = ReadBufferScale(c);
        s.MaxTextureSize = c.GetInt("maxtexturesize", 4096);
        s.MaxCabTextureSize = c.GetInt("maxcabtexturesize", 4096);
        s.TextureFiltering = Math.Max(1, c.GetDouble("anisotropicfiltering", 8));
        s.AntiAliasing = ReadAntiAliasing(c);
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
        s.FullscreenMonitor = c.GetString("fullscreenmonitor", "").Trim();
        s.RenderAngleVulkan = string.Equals(c.GetString("gfx.angleplatform", ""), "vulkan", StringComparison.OrdinalIgnoreCase);

        s.SplineFidelity = Clamp(c.GetInt("splinefidelity", 1), 1, 4);
        s.FullPhysics = c.GetBool("fullphysics", true);
        s.EnableTraction = c.GetBool("enabletraction", true);
        s.LiveTraction = c.GetBool("livetraction", true);
        s.PhysicsLog = c.GetBool("physicslog", false);
        s.PhysicsLogTrainsInIni = c.Has("physicslog.trains");
        s.PhysicsLogTrains = c.GetBool("physicslog.trains", false);
        // the simulator records the black box along with the physics logs when the ini says nothing of it
        s.BlackBoxInIni = c.Has("blackbox");
        s.BlackBox = s.BlackBoxInIni ? c.GetBool("blackbox", false) : s.PhysicsLog || s.PhysicsLogTrains;
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
        s.AdvancedSettings = c.GetBool("starter.settings.advanced", false);
        s.MovedToStandard = c.GetBool("starter.settings.standard", false);
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

        GraphicsRules.Normalize(s);
    }

    /// <summary>
    /// Keys the simulator reads a set number of words after, whatever stands there:
    /// one the file holds short takes the next keys in, so a save puts it right
    /// even where nobody changed it.
    /// </summary>
    public static readonly string[] FixedLengthKeys = { "uart", "uarttune", "uartfeature" };

    /// <summary>Keys written only when they say something, besides those of <see cref="SimulatorKeys"/>.</summary>
    public static readonly string[] OptionalKeys = { "fpslimit", "gfx.angleplatform", "fullscreenmonitor" };

    /// <summary>Keys no simulator reads any more, taken out of the file on a save.</summary>
    public static readonly string[] ObsoleteKeys =
    {
        "gfx.postfx.tonemapping",
        "starter.developertools",
        // The OpenGL 4.6 and Vulkan renderers', which the simulator's master does not have.
        "gfx.antialiasing", "gfx.buffer.percent", "gfx.vulkan.device", "gfx.dlss.preset", "gfx.framegeneration",
        "gfx.reflex", "gfx.raytracing", "gfx.raytracing.range", "gfx.ssr", "gfx.lights.clustered", "gfx.lights.shadows",
        "gfx.contactshadows", "gfx.shadow.sdsm", "gfx.postfx.ao", "gfx.postfx.ao.lighting", "gfx.postfx.ao.quality",
        "gfx.cabprobe", "gfx.cabprobe.interval", "gfx.cabprobe.bounce", "gfx.cabprobe.reflections", "gfx.fog",
        "gfx.fog.quality", "gfx.clouds", "gfx.clouds.shadows", "gfx.postfx.bloom", "gfx.postfx.autoexposure",
        "gfx.grass", "gfx.grass.distance", "gfx.grass.fadedistance", "gfx.grass.density", "gfx.precipitation.particles",
    };

    /// <summary>
    /// The settings into <paramref name="c"/> as the simulator would run with them,
    /// which leaves <paramref name="settings"/> as they are: a preview writes them too.
    /// </summary>
    public void Write(SimulatorSettings settings, ConfigFile c)
    {
        var s = settings.Copy();
        GraphicsRules.Normalize(s);

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
        double my = SimulatorSettings.MouseScaleY * (s.InvertMouseVertical ? -1 : 1);
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
        // The simulator draws into a buffer of this size and stretches it to the window.
        c.SetInt("gfx.framebuffer.width", (int)Math.Round(s.Width * s.BufferScalePercent / 100.0));
        c.SetInt("gfx.framebuffer.height", (int)Math.Round(s.Height * s.BufferScalePercent / 100.0));
        c.SetInt("maxtexturesize", s.MaxTextureSize);
        c.SetInt("maxcabtexturesize", s.MaxCabTextureSize);
        c.SetDouble("anisotropicfiltering", s.TextureFiltering);
        c.SetInt("multisampling", MultisamplingLevel(s.AntiAliasing));
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
        if (!string.IsNullOrWhiteSpace(s.FullscreenMonitor)) c.Set("fullscreenmonitor", s.FullscreenMonitor.Trim());
        if (s.RenderAngleVulkan) c.Set("gfx.angleplatform", "vulkan");

        c.SetInt("splinefidelity", s.SplineFidelity);
        c.SetBool("fullphysics", s.FullPhysics);
        c.SetBool("enabletraction", s.EnableTraction);
        c.SetBool("livetraction", s.LiveTraction);
        c.SetBool("physicslog", s.PhysicsLog);
        // written where it says more than the physics logs, or where the file has it already: an ini without it
        // stays so, and a line written once and no longer saying more goes away on the next save
        if (s.BlackBoxInIni || s.BlackBox != (s.PhysicsLog || s.PhysicsLogTrains))
            c.SetBool("blackbox", s.BlackBox);
        // a simulator before it doesn't know the line: an ini without it stays so while it is off
        if (s.PhysicsLogTrainsInIni || s.PhysicsLogTrains)
            c.SetBool("physicslog.trains", s.PhysicsLogTrains);
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
        c.SetBool("starter.settings.advanced", s.AdvancedSettings);
        c.SetBool("starter.settings.standard", s.MovedToStandard);
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

    private static int ReadBufferScale(ConfigFile c) =>
        Clamp(BufferScaleIn(c), 25, GraphicsRules.MaxBufferScale);

    // The starter's own percent, or the framebuffer the file sets against its window.
    private static int BufferScaleIn(ConfigFile c)
    {
        if (c.Has("starter.bufferscale"))
            return c.GetInt("starter.bufferscale", 100);
        int width = c.GetInt("width", 0);
        int framebuffer = c.GetInt("gfx.framebuffer.width", 0);
        return width > 0 && framebuffer > 0 ? (int)Math.Round(framebuffer * 100.0 / width) : 100;
    }

    /// <summary>
    /// The keys of <paramref name="c"/> set above what the starter lets them go to
    /// (<see cref="GraphicsRules"/>): the next save brings them down.
    /// </summary>
    public static IReadOnlySet<string> OverCapKeys(ConfigFile c)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (c.GetInt("maxcabtexturesize", 0) > GraphicsRules.MaxCabTextureSize)
            keys.Add("maxcabtexturesize");
        var shadowTune = c.GetTokens("shadowtune");
        if (shadowTune.Length >= 3 && ParseDouble(shadowTune[2], 0) > GraphicsRules.MaxShadowRange)
            keys.Add("shadowtune");
        if (BufferScaleIn(c) > GraphicsRules.MaxBufferScale)
            keys.UnionWith(new[] { "starter.bufferscale", "gfx.framebuffer.width", "gfx.framebuffer.height" });
        return keys;
    }

    private static AntiAliasingMode ReadAntiAliasing(ConfigFile c) => c.GetInt("multisampling", 2) switch
    {
        <= 0 => AntiAliasingMode.Off,
        1 => AntiAliasingMode.Msaa2,
        2 => AntiAliasingMode.Msaa4,
        _ => AntiAliasingMode.Msaa8
    };

    private static int MultisamplingLevel(AntiAliasingMode mode) => mode switch
    {
        AntiAliasingMode.Msaa2 => 1,
        AntiAliasingMode.Msaa4 => 2,
        AntiAliasingMode.Msaa8 => 3,
        _ => 0
    };

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>
    /// Whether the file's own mousescale can stay as it is: a first number neither
    /// zero nor NaN/Infinity, and 0.5 or -0.5 after it. Otherwise a save writes the
    /// one the starter holds.
    /// </summary>
    public static bool MouseScaleStands(string? raw)
    {
        string[] tokens = (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 &&
               TryParseMouseScale(tokens[0], out _) &&
               TryParseMouseScale(tokens[1], out double y) &&
               Math.Abs(y) == SimulatorSettings.MouseScaleY;
    }

    private static bool TryParseMouseScale(string token, out double scale) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
        double.IsFinite(scale) && scale != 0;

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
