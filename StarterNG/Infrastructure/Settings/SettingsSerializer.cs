using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StarterNG.Classes;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Settings;

/// <summary>
/// Maps <see cref="SimulatorSettings"/> onto the simulator's eu07.ini and back.
/// </summary>
/// <remarks>
/// Every key the starter understands is named exactly once, here. Reads clamp
/// and default so a hand-edited ini cannot put the UI into an impossible state,
/// and writes preserve the keys the starter does not manage - which is why a
/// round trip through this class is worth testing on its own.
/// </remarks>
public sealed class SettingsSerializer
{
    public void Read(SimulatorSettings s, ConfigFile c)
    {

        s.LanguageWasSet = c.Has("lang");
        // Kept as the code the file holds. Startup hands it to the localisation
        // service, which resolves it to a display name and writes that back; the
        // starter must not decide here that an unrecognised code means English,
        // because the same key is what tells the simulator which language to run
        // in.
        s.LanguageCode = c.GetString("lang", "");
        s.Language = s.LanguageCode;
        s.Fullscreen = c.GetBool("fullscreen", false);
        s.PauseWhenInactive = c.GetBool("inactivepause", true);
        s.PauseOnStart = c.GetBool("pause", false);

        var mouse = c.GetTokens("mousescale");
        if (mouse.Length >= 1 && double.TryParse(mouse[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double mx))
        {
            s.InvertMouseHorizontal = mx < 0;
            s.CursorSensitivity = Clamp((int)Math.Round(Math.Abs(mx)), 1, 5);
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
        s.UartPort = c.GetString("uart", "");
        s.UartTune = c.GetString("uarttune", "");
        s.UartDebug = c.GetBool("uartdebug", false);
        if (c.Has("uartfeature"))
        {
            var uartTokens = (c.GetRaw("uartfeature") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            var uf = new HashSet<string>(uartTokens, StringComparer.OrdinalIgnoreCase);
            s.UartMain = uf.Contains("main"); s.UartScnd = uf.Contains("scnd"); s.UartTrain = uf.Contains("train");
            s.UartLocal = uf.Contains("local"); s.UartRadioVolume = uf.Contains("radiovolume"); s.UartRadioChannel = uf.Contains("radiochannel");
        }
        else
        {
            s.UartMain = s.UartScnd = s.UartTrain = s.UartLocal = true;
            s.UartRadioVolume = s.UartRadioChannel = false;
        }

        s.SelectExeAutomatically = c.GetBool("starter.exe.auto", true);
        s.ExecutablePath = c.GetString("starter.exe.path", "eu07.exe");
        s.DebugMode = c.GetBool("debugmode", false);
        s.VirtualShunting = c.GetBool("ai.trainman", true);
        s.LogMissingVehicleFiles = c.GetBool("starter.logmissingvehicles", false);

        s.RenderEngine = IndexOf(SimulatorSettings.RenderEngines, c.GetString("gfxrenderer", "full"), 0);
        s.Width = c.GetInt("width", 1280);
        s.Height = c.GetInt("height", 720);
        s.BufferScalePercent = Clamp(c.GetInt("starter.bufferscale", 100), 50, 200);
        s.MaxTextureSize = c.GetInt("maxtexturesize", 4096);
        s.MaxCabTextureSize = c.GetInt("maxcabtexturesize", 4096);
        s.TextureFiltering = c.GetInt("anisotropicfiltering", 8);
        s.Multisampling = Clamp(c.GetInt("multisampling", 2), 0, 3);
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
        s.ReflectionsFramerate = c.GetInt("gfx.reflections.framerate", 60);

        var shadowTune = c.GetTokens("shadowtune");
        if (shadowTune.Length >= 1) s.ShadowMapResolution = ParseInt(shadowTune[0], 2048);
        if (shadowTune.Length >= 2) s.ShadowProjectionRange = ParseInt(shadowTune[1], 250);
        if (shadowTune.Length >= 4) s.ShadowTuneExtra = new[] { shadowTune[2], shadowTune[3] };

        s.CabShadowsRange = (int)Math.Round(c.GetDouble("gfx.shadows.cab.range", 0));
        s.ShadowRankCutoff = c.GetInt("gfx.shadow.rank.cutoff", 3);
        s.ReflectionsFidelity = Clamp(c.GetInt("gfx.reflections.fidelity", 0), 0, 2);
        s.FieldOfView = Clamp((int)Math.Round(c.GetDouble("fieldofview", 45)), 15, 75);
        s.PythonScreens = c.GetBool("python.enabled", true);
        s.PythonThreadedUpload = c.GetBool("python.threadedupload", true);
        s.FpsLimitEnabled = c.Has("fpslimit");
        s.FpsLimit = Clamp(c.GetInt("fpslimit", 30), 1, 100);
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
        s.Volume = Clamp((int)Math.Round(c.GetDouble("sound.volume", 1.0) * 50), 1, 100);
        s.RadioVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.radio", 0.75) * 100), 1, 100);
        s.VehiclesVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.vehicle", 1.0) * 100), 1, 100);
        s.PositionalVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.positional", 1.0) * 100), 1, 100);
        s.AmbientVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.ambient", 1.0) * 100), 1, 100);
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
        s.BatteryDefault = (BatteryDefault)Clamp(c.GetInt("starter.batterydefault", 0), 0, 2);
        s.ShowAiVehicles = c.GetBool("starter.show.ai", true);
        s.DrivableOnly = c.GetBool("starter.drivableonly", true);
        s.ShowArchivalSceneries = c.GetBool("starter.show.archival", true);
        s.HideArchivalVehicles = c.GetBool("starter.hide.archivalvehicles", true);
        s.LastScenery = c.GetString("starter.last.scenery", "");
        s.WindowMaximized = c.GetBool("starter.window.maximized", false);
    }

    private static readonly string[] ObsoleteKeys =
    {
        "gfx.postfx.tonemapping",
    };

    public void Write(SimulatorSettings s, ConfigFile c)
    {

        foreach (string key in ObsoleteKeys)
            c.Remove(key);

        c.Set("lang", string.IsNullOrWhiteSpace(s.LanguageCode) ? "en" : s.LanguageCode);
        c.SetBool("fullscreen", s.Fullscreen);
        c.SetBool("inactivepause", s.PauseWhenInactive);
        c.SetBool("pause", s.PauseOnStart);

        double mx = s.CursorSensitivity * (s.InvertMouseHorizontal ? -1 : 1);
        double my = s.MouseScaleYMagnitude * (s.InvertMouseVertical ? -1 : 1);
        c.Set("mousescale",
            $"{mx.ToString("0.###", CultureInfo.InvariantCulture)} " +
            $"{my.ToString("0.###", CultureInfo.InvariantCulture)}");

        c.SetBool("input.gamepad", !s.IgnoreGamepad);
        c.SetInt("feedbackmode", s.FeedbackMode);
        c.SetInt("feedbackport", s.FeedbackPort);
        if (s.UartEnabled && !string.IsNullOrWhiteSpace(s.UartPort)) c.Set("uart", s.UartPort); else c.Remove("uart");
        c.Set("uarttune", s.UartTune);
        c.SetBool("uartdebug", s.UartDebug);
        var sb = new StringBuilder();
        if (s.UartMain) sb.Append("main|");
        if (s.UartScnd) sb.Append("scnd|");
        if (s.UartTrain) sb.Append("train|");
        if (s.UartLocal) sb.Append("local|");
        if (s.UartRadioVolume) sb.Append("radiovolume|");
        if (s.UartRadioChannel) sb.Append("radiochannel|");
        c.Set("uartfeature", sb.ToString());

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
        c.SetInt("gfx.framebuffer.width", fbw);
        c.SetInt("gfx.framebuffer.height", fbh);
        c.SetInt("maxtexturesize", s.MaxTextureSize);
        c.SetInt("maxcabtexturesize", s.MaxCabTextureSize);
        c.SetInt("anisotropicfiltering", s.TextureFiltering);
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
        c.SetInt("gfx.reflections.framerate", s.ReflectionsFramerate);
        c.Set("shadowtune",
            $"{s.ShadowMapResolution} {s.ShadowProjectionRange} " +
            $"{s.ShadowTuneExtra[0]} {s.ShadowTuneExtra[1]}");
        c.SetInt("gfx.shadows.cab.range", s.CabShadowsRange);
        c.SetInt("gfx.shadow.rank.cutoff", s.ShadowRankCutoff);
        c.SetInt("gfx.reflections.fidelity", s.ReflectionsFidelity);
        c.SetInt("fieldofview", s.FieldOfView);
        c.SetBool("python.enabled", s.PythonScreens);
        c.SetBool("python.threadedupload", s.PythonThreadedUpload);
        if (s.FpsLimitEnabled) c.SetInt("fpslimit", s.FpsLimit); else c.Remove("fpslimit");
        c.SetDouble("gfx.shadow.angle.min", s.ShadowAngleLimit);
        c.SetBool("fullscreenwindowed", s.FullscreenWindowed);
        if (s.RenderAngleVulkan) c.Set("gfx.angleplatform", "vulkan"); else c.Remove("gfx.angleplatform");

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
        c.SetInt("starter.batterydefault", (int)s.BatteryDefault);
        c.SetBool("starter.show.ai", s.ShowAiVehicles);
        c.SetBool("starter.drivableonly", s.DrivableOnly);
        c.SetBool("starter.show.archival", s.ShowArchivalSceneries);
        c.SetBool("starter.hide.archivalvehicles", s.HideArchivalVehicles);
        if (!string.IsNullOrWhiteSpace(s.LastScenery))
            c.Set("starter.last.scenery", s.LastScenery);
        else
            c.Remove("starter.last.scenery");
        c.SetBool("starter.window.maximized", s.WindowMaximized);
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
