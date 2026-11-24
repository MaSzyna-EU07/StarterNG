using System;
using System.Collections.Generic;

namespace StarterNG.Domain.Settings;

// Values match the key eu07.ini has carried since the old starter.
public enum ConsistReadyOverride { Scenery = 0, AlwaysCold = 1, AlwaysReady = 2 }

public enum ExeProblem { None, NotFound, NotExecutable, WrongPlatform, NotAProgram }

// multisampling: 0 off, then 2, 4 and 8 samples.
public enum AntiAliasingMode { Off, Msaa2, Msaa4, Msaa8 }

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

    /// <summary>input.gamepad no: the recommended setting, so the starter's default, though the simulator's own is yes.</summary>
    public bool IgnoreGamepad = true;
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
    public AntiAliasingMode AntiAliasing = AntiAliasingMode.Msaa4;
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

    // Only the legacy renderer, or only Windows builds, read these.
    public bool UseVbo = true;
    public bool RenderAngleVulkan;
    public bool UseGLES;
    public int CabShadowsRange;

    /// <summary>Updates of the reflections a second; the simulator's own is one in five minutes.</summary>
    public double ReflectionsFramerate = 1 / 300.0;
    public int ShadowMapResolution = 2048;
    /// <summary>The third number of shadowtune; the second and fourth go unread.</summary>
    public int ShadowProjectionRange = 250;
    public int ShadowRankCutoff = 3;
    public int ReflectionsFidelity;
    public int FieldOfView = 45;
    public bool PythonScreens = true;
    public bool PythonThreadedUpload = true;
    public bool FpsLimitEnabled;
    public double FpsLimit = 30;
    public double ShadowAngleLimit = 0.2;
    public bool FullscreenWindowed;
    /// <summary>fullscreenmonitor: the monitor a full screen opens on, as "name:x,y"; empty for the primary one.</summary>
    public string FullscreenMonitor = "";

    public int SplineFidelity = 1;
    public bool FullPhysics = true;
    public bool EnableTraction = true;
    public bool LiveTraction = true;
    public bool PhysicsLog;
    /// <summary>
    /// The black box of the session, and with it the tapes (.dat) of the vehicles the player was in, written when the
    /// simulator closes: the tapes without the log of every vehicle. With no entry of its own it goes with the physics log.
    /// </summary>
    public bool BlackBox;
    /// <summary>Whether eu07.ini had a line of its own for the black box when read: it is kept saying what is wanted.</summary>
    public bool BlackBoxInIni;
    /// <summary>
    /// The physics logs (.dat) of the trains only, "physicslog.trains": of each driver of rail vehicles, not of the
    /// passengers nor of the vehicles of the roads. The tapes of every train, far fewer files than the log of every vehicle.
    /// </summary>
    public bool PhysicsLogTrains;
    /// <summary>Whether eu07.ini had a line for the logs of the trains when read: kept, else written only when on.</summary>
    public bool PhysicsLogTrainsInIni;
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

    /// <summary>
    /// Every setting on show, the ones a player can break the game with too. Off, the standard
    /// mode keeps the settings, the consists and the scenario to what a driver needs.
    /// </summary>
    public bool AdvancedSettings;

    /// <summary>
    /// The settings were brought into the standard mode once, on the first start of this
    /// starter: the advanced ones to their defaults, the sliders into their ranges.
    /// </summary>
    public bool MovedToStandard;

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
    /// The gfxrenderer words the starter offers. "default" is the full renderer, the one to use
    /// wherever it runs; the other two draw with the legacy one. The experimental renderer is
    /// not offered: the simulator a player has is built without it.
    /// </summary>
    public static readonly string[] RenderEngines = { "default", "legacy", "simple" };

    /// <summary>
    /// The words of the file that stay as they are while the choice stands: those application.cpp
    /// on master knows, but "experimental". Any other word starts the full renderer with a line in
    /// the log, so a save puts "default" in its place.
    /// </summary>
    public static bool KeptRendererWord(string? word) =>
        word?.Trim().ToLowerInvariant() is "default" or "legacy" or "simple" or "simpleshader" or "off";

    /// <summary>Where <paramref name="word"/> stands among <see cref="RenderEngines"/>, as the simulator reads it.</summary>
    public static int RenderEngineOf(string word) => word.Trim().ToLowerInvariant() switch
    {
        "legacy" or "simpleshader" or "off" => 1,
        "simple" => 2,
        _ => 0
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
    /// The first mousescale number of each level of <see cref="CursorSensitivity"/>, 1 to 4, as
    /// the old starter wrote them (MaSzyna-EU07/Starter, src/uSettings.pas): low, standard, high
    /// and very high, negative with <see cref="InvertMouseHorizontal"/>. Whatever else the file
    /// has there goes on the next save.
    /// </summary>
    internal static readonly double[] MouseScales = { 1.0, 1.5, 2.3, 3.2 };

    /// <summary>The first mousescale number <paramref name="level"/> stands for.</summary>
    internal static double MouseScaleOf(int level) => MouseScales[Math.Clamp(level, 1, MouseScales.Length) - 1];

    /// <summary>
    /// The level the old starter showed for a first mousescale number: low below 1.3, high
    /// above 1.9, very high above 2.7 and standard between, whatever its sign.
    /// </summary>
    internal static int MouseLevelOf(double scale)
    {
        double size = Math.Abs(scale);
        return size > 2.7 ? 4 : size > 1.9 ? 3 : size < 1.3 ? 1 : 2;
    }

    /// <summary>
    /// The second mousescale number, as the old starter always wrote it: 0.5, or
    /// -0.5 with <see cref="InvertMouseVertical"/>. Whatever else the file has there
    /// goes on the next save.
    /// </summary>
    internal const double MouseScaleY = 0.5;

    /// <summary>The numbers of shadowtune the simulator skips, kept as the file has them.</summary>
    internal string[] ShadowTuneUnused { get; set; } = { "250", "300" };
}
