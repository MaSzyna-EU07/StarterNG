using System;

namespace StarterNG.Domain.Settings;

// Values match the key eu07.ini has carried since the old starter.
public enum ConsistReadyOverride { Scenery = 0, AlwaysCold = 1, AlwaysReady = 2 }

public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform }

// gfxrenderer: the opengl 4.6 renderer ("full", which the simulator reads as "default") or the vulkan 1.4 one.
public enum GraphicsBackend { OpenGL = 0, Vulkan = 1 }

// gfx.antialiasing. Dlss and Dlaa are the vulkan renderer's only; the simulator falls back to taa elsewhere.
public enum AntiAliasingMode { Off, Msaa2, Msaa4, Msaa8, Taa, Dlss, Dlaa }

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
    public bool DeveloperTools;

    public GraphicsBackend Backend = GraphicsBackend.OpenGL;
    public string VulkanDevice = "";
    public int Width = 1280;
    public int Height = 720;
    public int BufferScalePercent = 100;
    public int MaxTextureSize = 4096;
    public int MaxCabTextureSize = 4096;
    public int TextureFiltering = 8;
    public AntiAliasingMode AntiAliasing = AntiAliasingMode.Msaa4;
    public string DlssPreset = "default";
    public int FrameGeneration = 1;
    public int Reflex;
    public int DynamicLights = 7;
    public double DrawRangeFactor = 3.0;
    public bool VSync;
    public bool Smoke = true;
    public int SmokeFidelity = 1;
    public bool ChromaticAberration = true;
    public bool MotionBlur = true;
    public bool ExtraEffects = true;
    public bool EnvMap = true;
    public bool RenderShadows = true;
    public bool ShadowSdsm;
    public int LightShadows = 2;
    public int ContactShadows;
    public bool LightsClustered = true;
    public int AmbientOcclusion = 2;
    public int AoSlices = 3;
    public int AoSteps = 4;
    public int Ssr = 2;
    public bool RayTracing;
    public int RayTracingRange = 500;
    public bool VolumetricFog = true;
    public int FogQuality = 2;
    public int Clouds = 2;
    public bool CloudShadows = true;
    public bool Grass = true;
    public int GrassDistance = 60;
    public double GrassDensity = 1.0;
    public int PrecipitationParticles = 65536;
    public bool Bloom = true;
    public bool AutoExposure = true;
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

    // "full" rather than "default": every simulator build reads it, the new ones as the opengl 4.6 renderer.
    public static readonly string[] RenderEngines = { "full", "vulkan" };

    public static readonly int[] AnisotropySteps = { 1, 2, 4, 8, 16 };

    internal double MouseScaleYMagnitude { get; set; } = 0.2;

    internal string[] ShadowTuneExtra { get; set; } = { "400", "300" };
}
