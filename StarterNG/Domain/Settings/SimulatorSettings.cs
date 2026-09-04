using System;

namespace StarterNG.Domain.Settings;

/// <summary>How the simulator should treat the battery switch when a run starts.</summary>
public enum BatteryDefault { Default = 0, AlwaysOff = 1, AlwaysOn = 2 }

/// <summary>What is wrong with the executable the starter would launch.</summary>
public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform }

/// <summary>
/// Everything the starter writes into eu07.ini, plus the handful of preferences
/// it keeps for itself.
/// </summary>
/// <remarks>
/// Plain state with sensible defaults, and nothing else: reading and writing the
/// ini is <c>Infrastructure.Settings.SettingsSerializer</c>, locating the file
/// and the executable is the rest of that folder, and the load/save lifecycle is
/// <c>Application.SettingsStore</c>. Splitting those out is what makes it
/// possible to test the ini mapping without a disk.
/// </remarks>
public sealed class SimulatorSettings
{
    /// <summary>
    /// False until the ini names a language, which is how the first run knows to
    /// follow the operating system instead.
    /// </summary>
    public bool LanguageWasSet { get; internal set; }

    /// <summary>
    /// Display name of the starter's language, as the picker shows it ("Polski",
    /// "English", "中文"). Resolved from <see cref="LanguageCode"/> at startup.
    /// </summary>
    public string Language = "English";

    /// <summary>
    /// The language's code, as its startercfg/lang/*.xml declares it and as
    /// eu07.ini stores it. This is what the simulator reads, so it has to carry
    /// whatever language the user actually picked rather than being folded into
    /// one of two known ones.
    /// </summary>
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
    /// <summary>
    /// Debug log bits beyond the two the UI exposes, carried through a round trip
    /// so a hand-configured log level is not silently narrowed.
    /// </summary>
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

    /// <summary>
    /// Vertical mouse scale as the ini stored it. The UI only exposes one
    /// sensitivity, so the authored vertical magnitude is carried through a
    /// round trip rather than being overwritten.
    /// </summary>
    internal double MouseScaleYMagnitude { get; set; } = 0.2;

    /// <summary>
    /// Shadow tuning values beyond the two the UI edits, carried through a round
    /// trip unchanged.
    /// </summary>
    internal string[] ShadowTuneExtra { get; set; } = { "400", "300" };
}
