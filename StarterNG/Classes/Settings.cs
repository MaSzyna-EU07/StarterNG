using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace StarterNG.Classes;

/// <summary>
/// Application settings backing the Settings view, persisted to the simulator's
/// <c>eu07.ini</c>.
///
/// <para><b>Where it loads/saves.</b> The active file lives next to the
/// simulator's own config: <c>%APPDATA%\MaSzyna\eu07.ini</c> on Windows or
/// <c>~/.config/MaSzyna/eu07.ini</c> elsewhere. If that file does not exist yet,
/// defaults are read from an <c>eu07.ini</c> in the launcher's working directory
/// (the same fallback the simulator uses). Saving always writes to the per-user
/// path.</para>
///
/// <para><b>Unknown keys are kept.</b> The whole file is round-tripped through
/// <see cref="ConfigFile"/>; only the keys the UI actually edits are rewritten,
/// so any extra settings the launcher does not model survive a save.</para>
///
/// <para>Some mappings are best-effort where the wiki does not document an exact
/// key; those are marked with NOTE and can be adjusted against the running exe.</para>
/// </summary>
public sealed class Settings
{
    public static Settings Instance { get; } = new();

    /// <summary>
    /// Pulls the current values out of the Settings view into this instance.
    /// The view assigns this when it is constructed; null if the view was never
    /// opened (in which case Save just re-writes the loaded values).
    /// </summary>
    public Action? CaptureFromUi { get; set; }

    /// <summary>True if the loaded config explicitly contained a <c>lang</c> entry.</summary>
    public bool LanguageWasSet { get; private set; }

    // ── General ───────────────────────────────────────────────────────────
    public string Language = "English";          // lang  (pl/en)
    public bool Fullscreen;                       // fullscreen
    public bool PauseWhenInactive = true;         // inactivepause
    public bool PauseOnStart;                     // pause
    public int CursorSensitivity = 3;             // |mousescale x|
    public bool InvertMouseHorizontal;            // mousescale x < 0
    public bool InvertMouseVertical;              // mousescale y < 0

    // ── Communication ─────────────────────────────────────────────────────
    public bool IgnoreGamepad;                    // input.gamepad (inverted)
    public int FeedbackMode;                      // feedbackmode (0-5)

    // ── Other ─────────────────────────────────────────────────────────────
    public bool SelectExeAutomatically = true;    // starter.exe.auto  (launcher-only)
    public string ExecutablePath = "eu07.exe";    // starter.exe.path  (launcher-only)
    public bool DebugMode;                         // debugmode
    public bool VirtualShunting;                   // ai.trainman

    // ── Graphics ──────────────────────────────────────────────────────────
    public int RenderEngine;                       // gfxrenderer (index, see RenderEngines)
    public int Width = 1280;                       // width
    public int Height = 720;                       // height
    public int BufferScalePercent = 100;           // starter.bufferscale (NOTE: launcher-only)
    public int MaxTextureSize = 4096;              // maxtexturesize
    public int MaxCabTextureSize = 4096;           // maxcabtexturesize
    public int TextureFiltering = 8;               // anisotropicfiltering (1/2/4/8/16)
    public int Multisampling = 2;                  // multisampling (0-3)
    public double DrawRangeFactor = 1.0;           // gfx.drawrange.factor.max
    public bool VSync = true;                      // vsync
    public bool Smoke = true;                      // gfx.smoke
    public int SmokeFidelity = 1;                  // gfx.smoke.fidelity (1-4)
    public int Tonemapping;                        // gfx.postfx.tonemapping (NOTE: 0=reinhard,1=aces)
    public bool ChromaticAberration;               // gfx.postfx.chromaticaberration.enabled
    public bool MotionBlur = true;                 // gfx.postfx.motionblur.enabled
    public bool ExtraEffects = true;               // gfx.extraeffects
    public bool EnvMap = true;                      // gfx.envmap.enabled
    public bool UseVbo = true;                     // usevbo
    public bool RenderShadows = true;              // shadows + gfx.shadowmap.enabled
    public int ReflectionsFramerate = 60;          // gfx.reflections.framerate
    public int ShadowMapResolution = 4096;         // shadowtune[0]
    public int ShadowProjectionRange = 250;        // shadowtune[1]
    public int CabShadowsRange = 30;               // gfx.shadows.cab.range
    public int ShadowRankCutoff = 3;               // gfx.shadow.rank.cutoff (NOTE)
    public int ReflectionsFidelity = 2;            // gfx.reflections.fidelity (0-2)
    public int FieldOfView = 45;                   // fieldofview (15-75) NOTE: 'fovSlider' in XAML
    public bool PythonScreens = true;              // python.enabled
    public bool PythonThreadedUpload = true;       // python.threadedupload
    public int ScreenRendererPriority = 4;         // pyscreenrendererpriority (1-5, see ScreenPriorities)

    // ── Physics ───────────────────────────────────────────────────────────
    public int SplineFidelity = 1;                 // splinefidelity (1-4)
    public bool FullPhysics = true;                // fullphysics
    public bool EnableTraction = true;             // enabletraction (pantograph can break)
    public bool LiveTraction;                      // livetraction
    public bool PhysicsLog;                        // physicslog (speedometer tapes)
    public bool DebugLog = true;                   // debuglog (3 / 0)
    public bool MultipleLogs;                      // multiplelogs
    public bool DisplaySimulation = true;          // gfx.skiprendering (inverted)
    public bool CrashDamage = true;                // crashdamage

    // ── Sound ─────────────────────────────────────────────────────────────
    public bool SoundEnabled = true;               // soundenabled
    public int Volume = 100;                        // sound.volume          (0-2 → 0-100)
    public int RadioVolume = 80;                    // sound.volume.radio    (0-1 → 0-100)
    public int VehiclesVolume = 100;                // sound.volume.vehicle
    public int PositionalVolume = 100;              // sound.volume.positional
    public int AmbientVolume = 80;                  // sound.volume.ambient
    public int PausedVolume;                        // sound.volume.paused

    // ── Starter (launcher-only, stored under starter.* keys) ───────────────
    public bool AutoCloseStarter;                  // starter.autoclose
    public bool LargeThumbnails;                   // starter.largethumbnails
    public bool AutoExpandSceneryTree;             // starter.expandtree

    // List filters (launcher-only). Default on.
    public bool ShowAiVehicles = true;             // starter.show.ai (show //$o "-" consists)
    public bool DrivableOnly = true;               // starter.drivableonly (hide //$decor consists)
    public bool ShowArchivalSceneries = true;      // starter.show.archival

    /// <summary>gfxrenderer tokens in the order shown by the render-engine combo.</summary>
    public static readonly string[] RenderEngines =
        { "full", "legacy", "simpleshader", "simple", "off", "experimental" };

    /// <summary>Anisotropic-filtering steps for the texture-quality slider (1-5).</summary>
    public static readonly int[] AnisotropySteps = { 1, 2, 4, 8, 16 };

    /// <summary>pyscreenrendererpriority tokens indexed by the slider value (1-5).</summary>
    public static readonly string[] ScreenPriorities =
        { "off", "idle", "lowest", "lower", "normal" };

    // Tokens of mousescale / shadowtune that the UI does not edit but must keep.
    private double _mouseScaleYMagnitude = 0.5;
    private string[] _shadowTuneExtra = { "400", "300" }; // tokens [2], [3]

    private ConfigFile _config = new();
    private string _savePath = string.Empty;

    /// <summary>Resolves the per-user eu07.ini path for the current OS.</summary>
    public static string UserConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "MaSzyna", "eu07.ini");
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".config", "MaSzyna", "eu07.ini");
        }
        // Last resort: alongside the launcher.
        return Path.Combine(AppContext.BaseDirectory, "eu07.ini");
    }

    /// <summary>Default config shipped in the launcher's working directory.</summary>
    private static string DefaultConfigPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), "eu07.ini");

    /// <summary>
    /// The simulator executable to launch. With auto-selection on, the working
    /// directory is scanned for an <c>eu07*</c> binary - <c>eu07</c> on Linux/macOS,
    /// <c>eu07.exe</c> on Windows - so the same procedure works on every OS. With it
    /// off, the explicitly chosen path is used.
    /// </summary>
    public string ResolveExecutable()
    {
        if (!SelectExeAutomatically && !string.IsNullOrWhiteSpace(ExecutablePath))
            return ExecutablePath;

        string canonical = OperatingSystem.IsWindows() ? "eu07.exe" : "eu07";
        try
        {
            string? best = null;
            foreach (string path in Directory.GetFiles(Directory.GetCurrentDirectory(), "eu07*"))
            {
                if (!IsExecutableCandidate(path))
                    continue;
                string file = Path.GetFileName(path);
                if (string.Equals(file, canonical, StringComparison.OrdinalIgnoreCase))
                    return path; // exact canonical name wins
                if (best == null || file.Length < Path.GetFileName(best).Length)
                    best = path; // otherwise prefer the shortest eu07* name
            }
            if (best != null)
                return best;
        }
        catch { /* fall through to the canonical name */ }

        return canonical;
    }

    // Filters the eu07* matches down to plausible launcher binaries, skipping data
    // and config files (eu07.ini, eu07.log, libraries, …).
    private static bool IsExecutableCandidate(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".ini" or ".log" or ".txt" or ".cfg" or ".config" or ".json"
                or ".dll" or ".so" or ".dat" or ".bak" or ".csv" or ".xml")
            return false;
        if (OperatingSystem.IsWindows())
            return ext == ".exe";
        // Linux / macOS: no extension (eu07) or a known binary suffix
        return ext.Length == 0 || ext is ".x86_64" or ".run" or ".appimage";
    }

    private static readonly Encoding FileEncoding = ResolveEncoding();

    private static Encoding ResolveEncoding()
    {
        // eu07.ini comments use Polish diacritics in Windows-1250; preserve them.
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>Loads settings from the per-user file, or the working-dir default.</summary>
    public void Load()
    {
        _savePath = UserConfigPath();
        string source = File.Exists(_savePath) ? _savePath : DefaultConfigPath();

        try
        {
            _config = File.Exists(source)
                ? ConfigFile.Parse(File.ReadAllText(source, FileEncoding))
                : new ConfigFile();
        }
        catch
        {
            _config = new ConfigFile();
        }

        ReadFromConfig();
    }

    /// <summary>Captures the latest UI values (if the view is open) and saves.</summary>
    public void CaptureAndSave()
    {
        CaptureFromUi?.Invoke();
        Save();
    }

    /// <summary>Persists settings to the per-user eu07.ini, keeping unknown keys.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_savePath))
            _savePath = UserConfigPath();

        WriteToConfig();

        try
        {
            string? dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_savePath, _config.ToText(), FileEncoding);
        }
        catch
        {
            // Could not write (permissions / path). Leave values in memory.
        }
    }

    // ── config → fields ───────────────────────────────────────────────────
    private void ReadFromConfig()
    {
        var c = _config;

        // General
        LanguageWasSet = c.Has("lang");
        Language = c.GetString("lang", "en").ToLowerInvariant() == "pl" ? "Polski" : "English";
        Fullscreen = c.GetBool("fullscreen", false);
        PauseWhenInactive = c.GetBool("inactivepause", true);
        PauseOnStart = c.GetBool("pause", false);

        var mouse = c.GetTokens("mousescale");
        if (mouse.Length >= 1 && double.TryParse(mouse[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double mx))
        {
            InvertMouseHorizontal = mx < 0;
            CursorSensitivity = Clamp((int)Math.Round(Math.Abs(mx)), 1, 5);
        }
        if (mouse.Length >= 2 && double.TryParse(mouse[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double my))
        {
            InvertMouseVertical = my < 0;
            _mouseScaleYMagnitude = Math.Abs(my);
        }

        // Communication
        IgnoreGamepad = !c.GetBool("input.gamepad", true);
        FeedbackMode = Clamp(c.GetInt("feedbackmode", 0), 0, 5);

        // Other
        SelectExeAutomatically = c.GetBool("starter.exe.auto", true);
        ExecutablePath = c.GetString("starter.exe.path", "eu07.exe");
        DebugMode = c.GetBool("debugmode", false);
        VirtualShunting = c.GetBool("ai.trainman", false);

        // Graphics
        RenderEngine = IndexOf(RenderEngines, c.GetString("gfxrenderer", "full"), 0);
        Width = c.GetInt("width", 1280);
        Height = c.GetInt("height", 720);
        BufferScalePercent = Clamp(c.GetInt("starter.bufferscale", 100), 50, 200);
        MaxTextureSize = c.GetInt("maxtexturesize", 4096);
        MaxCabTextureSize = c.GetInt("maxcabtexturesize", 4096);
        TextureFiltering = c.GetInt("anisotropicfiltering", 8);
        Multisampling = Clamp(c.GetInt("multisampling", 2), 0, 3);
        DrawRangeFactor = c.GetDouble("gfx.drawrange.factor.max", 1.0);
        VSync = c.GetBool("vsync", true);
        Smoke = c.GetBool("gfx.smoke", true);
        SmokeFidelity = Clamp(c.GetInt("gfx.smoke.fidelity", 1), 1, 4);
        Tonemapping = c.GetString("gfx.postfx.tonemapping", "reinhard").ToLowerInvariant() == "aces" ? 1 : 0;
        ChromaticAberration = c.GetBool("gfx.postfx.chromaticaberration.enabled", false);
        MotionBlur = c.GetBool("gfx.postfx.motionblur.enabled", true);
        ExtraEffects = c.GetBool("gfx.extraeffects", true);
        EnvMap = c.GetBool("gfx.envmap.enabled", true);
        UseVbo = c.GetBool("usevbo", true);
        RenderShadows = c.GetBool("shadows", true);
        ReflectionsFramerate = c.GetInt("gfx.reflections.framerate", 60);

        var shadowTune = c.GetTokens("shadowtune");
        if (shadowTune.Length >= 1) ShadowMapResolution = ParseInt(shadowTune[0], 4096);
        if (shadowTune.Length >= 2) ShadowProjectionRange = ParseInt(shadowTune[1], 250);
        if (shadowTune.Length >= 4) _shadowTuneExtra = new[] { shadowTune[2], shadowTune[3] };

        CabShadowsRange = (int)Math.Round(c.GetDouble("gfx.shadows.cab.range", 30));
        ShadowRankCutoff = c.GetInt("gfx.shadow.rank.cutoff", 3);
        ReflectionsFidelity = Clamp(c.GetInt("gfx.reflections.fidelity", 2), 0, 2);
        FieldOfView = Clamp((int)Math.Round(c.GetDouble("fieldofview", 45)), 15, 75);
        PythonScreens = c.GetBool("python.enabled", true);
        PythonThreadedUpload = c.GetBool("python.threadedupload", true);
        ScreenRendererPriority = IndexOf(ScreenPriorities, c.GetString("pyscreenrendererpriority", "lower"), 3) + 1;

        // Physics
        SplineFidelity = Clamp(c.GetInt("splinefidelity", 1), 1, 4);
        FullPhysics = c.GetBool("fullphysics", true);
        EnableTraction = c.GetBool("enabletraction", true);
        LiveTraction = c.GetBool("livetraction", false);
        PhysicsLog = c.GetBool("physicslog", false);
        DebugLog = c.GetInt("debuglog", 3) != 0;
        MultipleLogs = c.GetBool("multiplelogs", false);
        DisplaySimulation = !c.GetBool("gfx.skiprendering", false);
        CrashDamage = c.GetBool("crashdamage", true);

        // Sound
        SoundEnabled = c.GetBool("soundenabled", true);
        Volume = Clamp((int)Math.Round(c.GetDouble("sound.volume", 1.5) * 50), 1, 100);
        RadioVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.radio", 0.8) * 100), 1, 100);
        VehiclesVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.vehicle", 1.0) * 100), 1, 100);
        PositionalVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.positional", 1.0) * 100), 1, 100);
        AmbientVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.ambient", 0.8) * 100), 1, 100);
        PausedVolume = Clamp((int)Math.Round(c.GetDouble("sound.volume.paused", 0.0) * 100), 0, 100);

        // Starter
        AutoCloseStarter = c.GetBool("starter.autoclose", false);
        LargeThumbnails = c.GetBool("starter.largethumbnails", false);
        AutoExpandSceneryTree = c.GetBool("starter.expandtree", false);
        ShowAiVehicles = c.GetBool("starter.show.ai", true);
        DrivableOnly = c.GetBool("starter.drivableonly", true);
        ShowArchivalSceneries = c.GetBool("starter.show.archival", true);
    }

    // ── fields → config ───────────────────────────────────────────────────
    private void WriteToConfig()
    {
        var c = _config;

        // General
        c.Set("lang", Language == "Polski" ? "pl" : "en");
        c.SetBool("fullscreen", Fullscreen);
        c.SetBool("inactivepause", PauseWhenInactive);
        c.SetBool("pause", PauseOnStart);

        double mx = CursorSensitivity * (InvertMouseHorizontal ? -1 : 1);
        double my = _mouseScaleYMagnitude * (InvertMouseVertical ? -1 : 1);
        c.Set("mousescale",
            $"{mx.ToString("0.###", CultureInfo.InvariantCulture)} " +
            $"{my.ToString("0.###", CultureInfo.InvariantCulture)}");

        // Communication
        c.SetBool("input.gamepad", !IgnoreGamepad);
        c.SetInt("feedbackmode", FeedbackMode);

        // Other
        c.SetBool("starter.exe.auto", SelectExeAutomatically);
        c.Set("starter.exe.path", ExecutablePath);
        c.SetBool("debugmode", DebugMode);
        c.SetBool("ai.trainman", VirtualShunting);

        // Graphics
        c.Set("gfxrenderer", RenderEngines[Clamp(RenderEngine, 0, RenderEngines.Length - 1)]);
        c.SetInt("width", Width);
        c.SetInt("height", Height);
        c.SetInt("starter.bufferscale", BufferScalePercent);
        c.SetInt("maxtexturesize", MaxTextureSize);
        c.SetInt("maxcabtexturesize", MaxCabTextureSize);
        c.SetInt("anisotropicfiltering", TextureFiltering);
        c.SetInt("multisampling", Multisampling);
        c.SetDouble("gfx.drawrange.factor.max", DrawRangeFactor);
        c.SetBool("vsync", VSync);
        c.SetBool("gfx.smoke", Smoke);
        c.SetInt("gfx.smoke.fidelity", SmokeFidelity);
        c.Set("gfx.postfx.tonemapping", Tonemapping == 1 ? "aces" : "reinhard");
        c.SetBool("gfx.postfx.chromaticaberration.enabled", ChromaticAberration);
        c.SetBool("gfx.postfx.motionblur.enabled", MotionBlur);
        c.SetBool("gfx.extraeffects", ExtraEffects);
        c.SetBool("gfx.envmap.enabled", EnvMap);
        c.SetBool("usevbo", UseVbo);
        c.SetBool("shadows", RenderShadows);
        c.SetBool("gfx.shadowmap.enabled", RenderShadows);
        c.SetInt("gfx.reflections.framerate", ReflectionsFramerate);
        c.Set("shadowtune",
            $"{ShadowMapResolution} {ShadowProjectionRange} " +
            $"{_shadowTuneExtra[0]} {_shadowTuneExtra[1]}");
        c.SetInt("gfx.shadows.cab.range", CabShadowsRange);
        c.SetInt("gfx.shadow.rank.cutoff", ShadowRankCutoff);
        c.SetInt("gfx.reflections.fidelity", ReflectionsFidelity);
        c.SetInt("fieldofview", FieldOfView);
        c.SetBool("python.enabled", PythonScreens);
        c.SetBool("python.threadedupload", PythonThreadedUpload);
        c.Set("pyscreenrendererpriority",
            ScreenPriorities[Clamp(ScreenRendererPriority - 1, 0, ScreenPriorities.Length - 1)]);

        // Physics
        c.SetInt("splinefidelity", SplineFidelity);
        c.SetBool("fullphysics", FullPhysics);
        c.SetBool("enabletraction", EnableTraction);
        c.SetBool("livetraction", LiveTraction);
        c.SetBool("physicslog", PhysicsLog);
        c.SetInt("debuglog", DebugLog ? 3 : 0);
        c.SetBool("multiplelogs", MultipleLogs);
        c.SetBool("gfx.skiprendering", !DisplaySimulation);
        c.SetBool("crashdamage", CrashDamage);

        // Sound
        c.SetBool("soundenabled", SoundEnabled);
        c.SetDouble("sound.volume", Volume / 50.0);
        c.SetDouble("sound.volume.radio", RadioVolume / 100.0);
        c.SetDouble("sound.volume.vehicle", VehiclesVolume / 100.0);
        c.SetDouble("sound.volume.positional", PositionalVolume / 100.0);
        c.SetDouble("sound.volume.ambient", AmbientVolume / 100.0);
        c.SetDouble("sound.volume.paused", PausedVolume / 100.0);

        // Starter
        c.SetBool("starter.autoclose", AutoCloseStarter);
        c.SetBool("starter.largethumbnails", LargeThumbnails);
        c.SetBool("starter.expandtree", AutoExpandSceneryTree);
        c.SetBool("starter.show.ai", ShowAiVehicles);
        c.SetBool("starter.drivableonly", DrivableOnly);
        c.SetBool("starter.show.archival", ShowArchivalSceneries);
    }

    // ── small helpers ──────────────────────────────────────────────────────
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
}
