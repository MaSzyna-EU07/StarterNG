using System;
using System.Collections.Generic;

namespace StarterNG.Domain.Settings;

// Values match the key eu07.ini has carried since the old starter.
public enum ConsistReadyOverride { Scenery = 0, AlwaysCold = 1, AlwaysReady = 2 }

public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform, NotAProgram }

public sealed class SimulatorSettings
{
    public bool LanguageWasSet { get; internal set; }

    /// <summary>eu07.ini starts a network.server, which the starter leaves to the file.</summary>
    public bool NetworkServer { get; internal set; }

    /// <summary>fpsaverage: the older regulator's frame rate target, which the starter leaves as it is.</summary>
    public double FpsAverage { get; internal set; }

    public string Language = "English";

    public string LanguageCode = "";
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
    public int UartBaud = Uart.DefaultBaud;
    public double UartUpdateTime = Uart.DefaultUpdateTime;
    public string UartTune = "";
    public bool UartDebug;
    public bool UartMain, UartScnd, UartTrain, UartLocal, UartRadioVolume, UartRadioChannel;

    /// <summary>The simulator's dynamicbrake feature, which has no box in the starter: kept as the file has it.</summary>
    public bool UartDynamicBrake;

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
    public double TextureFiltering = 8;
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
    /// <summary>Updates of the reflections a second; the simulator's own is one in five minutes.</summary>
    public double ReflectionsFramerate = 1 / 300.0;
    public int ShadowMapResolution = 2048;
    /// <summary>The third number of shadowtune; the second and fourth go unread.</summary>
    public int ShadowProjectionRange = 250;
    public int CabShadowsRange;
    public int ShadowRankCutoff = 3;
    public int ReflectionsFidelity;
    public int FieldOfView = 45;
    public bool PythonScreens = true;
    public bool PythonThreadedUpload = true;
    public bool FpsLimitEnabled;
    public double FpsLimit = 30;
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
    internal int DebugLogExtraBits { get; set; }

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

    /// <summary>Warn at start when the processor cannot run the official simulator builds.</summary>
    public bool CpuNotice = true;

    /// <summary>The card a native Linux simulator is started on, on a machine with two.</summary>
    public GpuPreference Gpu = GpuPreference.Discrete;

    public ConsistReadyOverride ReadyOverride = ConsistReadyOverride.Scenery;

    public bool ShowAiVehicles = true;
    public bool DrivableOnly = true;
    public bool ShowArchivalSceneries = true;
    public bool HideArchivalVehicles = true;
    public string LastScenery = "";

    public bool WindowMaximized;

    /// <summary>
    /// The <see cref="SimulatorKeys"/> as eu07.ini carries them, by key. One the
    /// file does not set is absent, and stays out of the file until changed.
    /// </summary>
    public Dictionary<string, string> Extra { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy to try changes on, apart from these settings.</summary>
    public SimulatorSettings Copy()
    {
        var copy = (SimulatorSettings)MemberwiseClone();
        copy.Extra = new Dictionary<string, string>(Extra, StringComparer.OrdinalIgnoreCase);
        copy.ShadowTuneUnused = (string[])ShadowTuneUnused.Clone();
        return copy;
    }

    /// <summary>
    /// The gfxrenderer words the simulator tells apart (application.cpp): every
    /// other word - the "simpleshader" and "off" older starters offered among them -
    /// gets the legacy renderer. A simulator with the OpenGL 4.6 and Vulkan 1.4
    /// renderers runs everything but "vulkan" as the first, and "vulkan" too when
    /// it was built without it or the card cannot run it.
    /// </summary>
    public static readonly string[] RenderEngines = { "full", "legacy", "simple", "experimental", "vulkan" };

    /// <summary>Where <paramref name="word"/> stands among <see cref="RenderEngines"/>, as the simulator reads it.</summary>
    public static int RenderEngineOf(string word) => word.Trim().ToLowerInvariant() switch
    {
        "full" or "default" or "opengl" or "gl46" => 0,
        "simple" => 2,
        "experimental" => 3,
        "vulkan" or "vulkan14" => 4,
        _ => 1
    };

    /// <summary>
    /// The least shadow range the simulator keeps for a map size: a map above
    /// 2048 px stretches over proportionally more ground.
    /// </summary>
    public static int ShadowRangeFloor(int mapSize) => mapSize <= 2048 ? 75 : 75 * mapSize / 2048;

    /// <summary>
    /// How far shadows actually reach. The full renderer's cascades stretch with a
    /// map above 2048 px (opengl33renderer.cpp), on top of the floor; the legacy
    /// one draws a single map out to the range itself.
    /// </summary>
    public static int ShadowReach(int range, int mapSize, bool legacyRenderer)
    {
        int reach = Math.Max(range, ShadowRangeFloor(mapSize));
        return legacyRenderer || mapSize <= 2048 ? reach : reach * (mapSize / 2048);
    }

    /// <summary>
    /// The file's own first mousescale number. The slider takes whole steps: this
    /// stands for as long as <see cref="CursorSensitivity"/> is the step nearest it.
    /// </summary>
    internal double MouseScaleXMagnitude { get; set; } = 2;

    internal double MouseScaleYMagnitude { get; set; } = 0.2;

    /// <summary>The numbers of shadowtune the simulator skips, kept as the file has them.</summary>
    internal string[] ShadowTuneUnused { get; set; } = { "250", "300" };
}
