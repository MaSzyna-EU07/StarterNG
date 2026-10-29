namespace StarterNG.Domain.Settings;

/// <summary>
/// What the simulator does with combinations of graphics settings, mirrored here so
/// the starter only offers what takes effect and writes eu07.ini the way the
/// simulator will run it (utilities/Globals.cpp, rendering/vulkan14, rendering/opengl46).
/// </summary>
public static class GraphicsRules
{
    public static bool IsDlss(AntiAliasingMode mode) => mode is AntiAliasingMode.Dlss or AntiAliasingMode.Dlaa;

    public static bool IsTemporal(AntiAliasingMode mode) => mode == AntiAliasingMode.Taa || IsDlss(mode);

    public static bool IsVulkan(SimulatorSettings s) => s.Backend == GraphicsBackend.Vulkan;

    /// <summary>
    /// DLSS is the Vulkan renderer's only (the OpenGL one takes TAA instead), and the
    /// DLSS SDK has no macOS libraries.
    /// </summary>
    public static bool DlssAllowed(SimulatorSettings s, bool dlssPlatform) => dlssPlatform && IsVulkan(s);

    public static bool DlssActive(SimulatorSettings s, bool dlssPlatform) =>
        DlssAllowed(s, dlssPlatform) && IsDlss(s.AntiAliasing);

    /// <summary>The frame generation goes with gfx.antialiasing dlss or dlaa, nothing else.</summary>
    public static bool FrameGenerationAllowed(SimulatorSettings s, bool dlssPlatform) => DlssActive(s, dlssPlatform);

    /// <summary>Reflex (VK_NV_low_latency2) is the Vulkan renderer's only.</summary>
    public static bool ReflexAllowed(SimulatorSettings s) => IsVulkan(s);

    /// <summary>The frame generation turns Reflex on whatever gfx.reflex says.</summary>
    public static bool ReflexForced(SimulatorSettings s, bool dlssPlatform) =>
        FrameGenerationAllowed(s, dlssPlatform) && s.FrameGeneration > 1;

    /// <summary>Ray traced reflections: the Vulkan renderer on a GPU with ray queries.</summary>
    public static bool RayTracingAllowed(SimulatorSettings s) => IsVulkan(s);

    /// <summary>The render scale is ignored by DLAA, which always draws at the size of the window.</summary>
    public static bool BufferScaleUsed(SimulatorSettings s, bool dlssPlatform) =>
        !(DlssActive(s, dlssPlatform) && s.AntiAliasing == AntiAliasingMode.Dlaa);

    /// <summary>TAA and DLSS run in the post-processing which gfx.skippipeline leaves out.</summary>
    public static bool TemporalWithoutPipeline(SimulatorSettings s) => s.SkipPipeline && IsTemporal(s.AntiAliasing);

    /// <summary>With the clustered lights the headlights come from the clusters; dynamiclights only caps the old path.</summary>
    public static bool DynamicLightsUsed(SimulatorSettings s) => !s.LightsClustered;

    /// <summary>The ambient occlusion goes into the lighting only with GTAO.</summary>
    public static bool AoLightingUsed(SimulatorSettings s) => s.AmbientOcclusion == 2;

    /// <summary>
    /// The AO in the lighting needs the depth of the whole scene drawn before its colour. SSR, contact
    /// shadows and gfx.depthprepass.scene draw it anyway; without them it is an extra pass (CPU time on OpenGL).
    /// </summary>
    public static bool AoLightingAddsDepthPrepass(SimulatorSettings s) =>
        AoLightingUsed(s) && s.AoLighting && s.Ssr == 0 && s.ContactShadows == 0 && !s.DepthPrepassScene;

    public static bool CabProbeUsed(SimulatorSettings s) => s.CabProbe > 0;

    /// <summary>
    /// The light bounced around the cab comes from the cab lamp as a light, which needs the clustered lights
    /// and gfx.lights.cab; without them the probe gives the reflections only.
    /// </summary>
    public static bool CabProbeLights(SimulatorSettings s) => CabProbeUsed(s) && s.LightsClustered && s.CabLampLight;

    /// <summary>
    /// Brings the settings to what the simulator would run with: the Vulkan-only
    /// features off on OpenGL, DLSS falling back to TAA, values within the ranges
    /// the simulator clamps them to.
    /// </summary>
    public static void Normalize(SimulatorSettings s, bool dlssPlatform)
    {
        if (IsDlss(s.AntiAliasing) && !DlssAllowed(s, dlssPlatform))
            s.AntiAliasing = AntiAliasingMode.Taa;

        s.FrameGeneration = FrameGenerationAllowed(s, dlssPlatform) ? Clamp(s.FrameGeneration, 1, 4) : 1;
        s.Reflex = ReflexAllowed(s) ? Clamp(s.Reflex, 0, 2) : 0;
        if (!RayTracingAllowed(s))
            s.RayTracing = false;

        if (!IsKnownDlssPreset(s.DlssPreset))
            s.DlssPreset = "default";

        s.MaxTextureSize = PowerOfTwo(s.MaxTextureSize, 64, 8192);
        s.MaxCabTextureSize = PowerOfTwo(s.MaxCabTextureSize, 512, 8192);
        s.ShadowMapResolution = PowerOfTwo(s.ShadowMapResolution, 512, 8192);
        s.BufferScalePercent = Clamp(s.BufferScalePercent, 25, 200);
        s.LightShadows = Clamp(s.LightShadows, 0, 3);
        s.ContactShadows = Clamp(s.ContactShadows, 0, 3);
        s.AmbientOcclusion = Clamp(s.AmbientOcclusion, 0, 2);
        s.AoSlices = Clamp(s.AoSlices, 1, 8);
        s.AoSteps = Clamp(s.AoSteps, 1, 16);
        s.CabProbe = Clamp(s.CabProbe, 0, 3);
        s.CabProbeInterval = Clamp(s.CabProbeInterval, 1, 120);
        s.CabProbeBounce = s.CabProbeBounce < 0 ? 0 : (s.CabProbeBounce > 4 ? 4 : s.CabProbeBounce);
        s.Ssr = Clamp(s.Ssr, 0, 3);
        s.RayTracingRange = Clamp(s.RayTracingRange, 10, 5000);
        s.FogQuality = Clamp(s.FogQuality, 1, 4);
        s.Clouds = Clamp(s.Clouds, 0, 4);
        s.GrassDistance = Clamp(s.GrassDistance, 5, 500);
        s.GrassDensity = s.GrassDensity < 0 ? 0 : (s.GrassDensity > 4 ? 4 : s.GrassDensity);
        s.PrecipitationParticles = Clamp(s.PrecipitationParticles, 0, 1 << 20);
    }

    public static readonly string[] DlssPresets = { "default", "j", "k", "l", "m" };

    public static bool IsKnownDlssPreset(string? preset)
    {
        foreach (string known in DlssPresets)
            if (string.Equals(known, preset, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>The power of two nearest below, as the simulator rounds texture and shadow map sizes.</summary>
    private static int PowerOfTwo(int v, int lo, int hi)
    {
        v = Clamp(v, lo, hi);
        int p = lo;
        while (p * 2 <= v)
            p *= 2;
        return p;
    }
}
