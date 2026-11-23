namespace StarterNG.Domain.Settings;

/// <summary>
/// What the simulator does with graphics values, mirrored here so the starter writes
/// eu07.ini the way the simulator will run it (utilities/Globals.cpp), and how far the
/// starter lets them go: no further than the old starter did (MaSzyna-EU07/Starter).
/// </summary>
public static class GraphicsRules
{
    /// <summary>maxcabtexturesize: the old starter's list ended at 4096.</summary>
    public const int MaxCabTextureSize = 4096;

    /// <summary>The projection range of shadowtune: the old starter's list ended at 400.</summary>
    public const int MaxShadowRange = 400;

    /// <summary>
    /// The frame buffer, in percent of the window. The old starter's choice of buffer
    /// (gfx.framebuffer.fidelity) is not read by the simulator, so it always had the window's size.
    /// </summary>
    public const int MaxBufferScale = 100;

    /// <summary>Brings the values within the ranges the simulator clamps them to, and the starter's caps.</summary>
    public static void Normalize(SimulatorSettings s)
    {
        s.MaxTextureSize = PowerOfTwo(s.MaxTextureSize, 64, 8192);
        s.MaxCabTextureSize = PowerOfTwo(s.MaxCabTextureSize, 512, MaxCabTextureSize);
        s.ShadowMapResolution = PowerOfTwo(s.ShadowMapResolution, 512, 8192);
        s.ShadowProjectionRange = s.ShadowProjectionRange > MaxShadowRange ? MaxShadowRange : s.ShadowProjectionRange;
        s.BufferScalePercent = Clamp(s.BufferScalePercent, 25, MaxBufferScale);
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
