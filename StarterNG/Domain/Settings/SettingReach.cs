using System;
using System.Collections.Generic;

namespace StarterNG.Domain.Settings;

/// <summary>Why the simulator about to be started would pass a key over.</summary>
public enum IdleReason
{
    None,
    WindowsBuildOnly,
    LegacyRendererOnly,
    /// <summary>Read by the full OpenGL renderer alone.</summary>
    ModernRendererOnly,
    /// <summary>Read by the OpenGL renderers, which the experimental one is not.</summary>
    OpenGlRenderersOnly,
    NeedsNetworkServer,
    /// <summary>The draw range is only stretched by the frame rate regulator, which is off.</summary>
    NeedsFrameRateTarget,
    /// <summary>fullscreenwindowed takes the monitor's own mode instead.</summary>
    MonitorModeInstead,
    VulkanOnly
}

/// <summary>What about the simulator to be started decides which keys it heeds.</summary>
/// <param name="WindowsBuild">The executable is a Windows build, under Wine or not.</param>
/// <param name="Renderer">The gfxrenderer word, as <see cref="SimulatorSettings.RenderEngines"/> has it.</param>
/// <param name="NetworkServer">eu07.ini starts a network.server.</param>
/// <param name="TargetFps">targetfps as eu07.ini sets it, or null where it does not.</param>
/// <param name="FpsAverage">fpsaverage, the older regulator's target; 0 for none.</param>
/// <param name="FullscreenWindowed">fullscreenwindowed is on.</param>
public sealed record SimulatorTarget(bool WindowsBuild, string Renderer, bool NetworkServer,
                                     double? TargetFps = null, double FpsAverage = 0,
                                     bool FullscreenWindowed = false);

/// <summary>The renderers a gfxrenderer word picks, as application.cpp tells them apart.</summary>
public enum RendererKind { Modern, Legacy, Experimental }

/// <summary>
/// Where an eu07.ini key takes effect, from the simulator's sources: a key it
/// reads but leaves alone on the platform, render path or setup at hand.
/// </summary>
public static class SettingReach
{
    // The keyboard lights, LPT and PoKeys behind feedback live in Console.cpp,
    // which only the Windows build compiles; the system console and ANGLE are
    // Windows-only in application.cpp.
    private static readonly HashSet<string> WindowsBuildOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "feedbackmode", "feedbackport", "showsystemconsole", "gfx.angleplatform"
    };

    // Read by openglrenderer.cpp alone; the modern path forces VBOs on.
    private static readonly HashSet<string> LegacyRendererOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "smoothtraction", "gfx.shadows.cab.range", "usevbo"
    };

    // Read by opengl33renderer.cpp and the shaders it builds (glsl_common.cpp) alone.
    private static readonly HashSet<string> ModernRendererOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "gfx.postfx.chromaticaberration.enabled", "gfx.postfx.motionblur.enabled",
        "gfx.postfx.motionblur.shutter", "gfx.extraeffects", "gfx.envmap.enabled", "gfx.skippipeline",
        "rendercab", "sleeperdistance"
    };

    // Read by both OpenGL renderers but not by the experimental one, which sets its
    // own anisotropy, draws no smoke and has no frame rate regulator.
    private static readonly HashSet<string> OpenGlRenderersOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "anisotropicfiltering", "multisampling", "dynamiclights", "gfx.smoke", "gfx.smoke.fidelity",
        "gfx.reflections.framerate", "gfx.reflections.range_vehicles", "gfx.shadow.angle.min",
        "scalespeculars", "gfx.drawrange.factor.max"
    };

    private static readonly HashSet<string> VulkanRendererOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "gfx.dlss.preset", "gfx.framegeneration", "gfx.reflex", "gfx.raytracing", "gfx.raytracing.range", "gfx.vulkan.device"
    };

    // application.cpp: fullscreenwindowed takes its size from the monitor and always
    // goes full screen. "width, height" is how the settings screen names that row.
    private static readonly HashSet<string> OverriddenByMonitorMode = new(StringComparer.OrdinalIgnoreCase)
    {
        "width, height", "fullscreen"
    };

    public static IdleReason Why(string key, SimulatorTarget target)
    {
        // The starter's own keys mean nothing to any simulator, and need not.
        if (key.StartsWith("starter.", StringComparison.OrdinalIgnoreCase))
            return IdleReason.None;
        if (WindowsBuildOnly.Contains(key) && !target.WindowsBuild)
            return IdleReason.WindowsBuildOnly;
        if (OverriddenByMonitorMode.Contains(key) && target.FullscreenWindowed)
            return IdleReason.MonitorModeInstead;

        var renderer = KindOf(target.Renderer);
        if (LegacyRendererOnly.Contains(key) && renderer != RendererKind.Legacy)
            return IdleReason.LegacyRendererOnly;
        if (ModernRendererOnly.Contains(key) && renderer != RendererKind.Modern)
            return IdleReason.ModernRendererOnly;
        if (OpenGlRenderersOnly.Contains(key) && renderer == RendererKind.Experimental)
            return IdleReason.OpenGlRenderersOnly;
        if (VulkanRendererOnly.Contains(key) && !IsVulkan(target.Renderer))
            return IdleReason.VulkanOnly;

        // on_focus_change pauses only a simulator that serves the network.
        if (string.Equals(key, "inactivepause", StringComparison.OrdinalIgnoreCase) && !target.NetworkServer)
            return IdleReason.NeedsNetworkServer;
        if (string.Equals(key, "gfx.drawrange.factor.max", StringComparison.OrdinalIgnoreCase) &&
            !DrawRangeRegulated(target))
            return IdleReason.NeedsFrameRateTarget;
        return IdleReason.None;
    }

    /// <summary>
    /// Whether the renderer moves the draw range with the frame rate - the only use
    /// of gfx.drawrange.factor.max: targetfps does it, as the older fpsaverage does.
    /// </summary>
    private static bool DrawRangeRegulated(SimulatorTarget target) =>
        target.FpsAverage != 0 || (target.TargetFps ?? 0) != 0;

    /// <summary>"full", its other names and the Vulkan renderer are the modern kind, "experimental" its own; every other word the legacy one.</summary>
    public static RendererKind KindOf(string renderer) => renderer.ToLowerInvariant() switch
    {
        "full" or "default" or "opengl" or "gl46" or "vulkan" or "vulkan14" => RendererKind.Modern,
        "experimental" => RendererKind.Experimental,
        _ => RendererKind.Legacy
    };

    private static bool IsVulkan(string renderer) => renderer.ToLowerInvariant() is "vulkan" or "vulkan14";
}
