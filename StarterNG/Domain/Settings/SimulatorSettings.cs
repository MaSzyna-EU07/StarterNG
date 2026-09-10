using System;

namespace StarterNG.Domain.Settings;

// Values match the key eu07.ini has carried since the old starter.
public enum ConsistReadyOverride { Scenery = 0, AlwaysCold = 1, AlwaysReady = 2 }

public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform }

public sealed class SimulatorSettings
{
    public bool LanguageWasSet { get; internal set; }

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

    public ConsistReadyOverride ReadyOverride = ConsistReadyOverride.Scenery;

    public bool ShowAiVehicles = true;
    public bool DrivableOnly = true;
    public bool ShowArchivalSceneries = true;
    public bool HideArchivalVehicles = true;
    public string LastScenery = "";

    public bool WindowMaximized;

    public static readonly string[] RenderEngines =
        { "full", "legacy", "simpleshader", "simple", "off", "experimental" };

    public static readonly int[] AnisotropySteps = { 1, 2, 4, 8, 16 };

    internal double MouseScaleYMagnitude { get; set; } = 0.2;

    internal string[] ShadowTuneExtra { get; set; } = { "400", "300" };
}
