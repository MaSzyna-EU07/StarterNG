namespace StarterNG.Domain.Settings;

/// <summary>
/// What the sliders offer, in either mode: the old starter's ranges (MaSzyna-EU07/Starter,
/// src/uMain.dfm), the tops of which <see cref="GraphicsRules"/> holds to. A value the file
/// has below these is shown as it is, so opening and saving the settings never moves it.
/// </summary>
public static class SliderRanges
{
    /// <summary>maxtexturesize: its list began at 512.</summary>
    public const int MinTextureSize = 512;

    /// <summary>The map size of shadowtune: its list began at 1024.</summary>
    public const int MinShadowMap = 1024;

    /// <summary>dynamiclights: 1 to 7.</summary>
    public const int MinDynamicLights = 1;

    /// <summary>The cursor's speed: its fastest was a mousescale of 3.2.</summary>
    public const int MaxCursorSensitivity = 3;

    /// <summary>gfx.reflections.framerate, in updates a second.</summary>
    public static readonly double[] ReflectionUpdates = { 1, 3, 10, 25, 60 };

    /// <summary>gfx.drawrange.factor.max: normal, high and very high.</summary>
    public static readonly double[] DrawRangeFactors = { 1, 2, 3 };

    /// <summary>gfx.shadows.cab.range, in metres: off, then 10 to 100.</summary>
    public static readonly double[] CabShadowRanges = { 0, 10, 20, 30, 50, 100 };

    /// <summary>fpslimit: none, or up to 240, where the fastest usual monitors run.</summary>
    public static readonly double[] FrameRateLimits =
        { 0, 20, 25, 30, 40, 45, 50, 60, 72, 75, 90, 100, 120, 144, 165, 180, 200, 240 };
}
