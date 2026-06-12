namespace StarterNG.Classes;

/// <summary>
/// Application settings backing the Settings view. Load/Save are stubs for now;
/// the real persistence will be implemented later.
/// </summary>
public sealed class Settings
{
    public static Settings Instance { get; } = new();

    // General
    public string Language = "English";
    public bool Fullscreen;
    public bool PauseWhenInactive;
    public bool PauseOnStart;
    public int CursorSensitivity = 3;
    public bool InvertMouseHorizontal;
    public bool InvertMouseVertical;

    // Other
    public string ExecutablePath = "eu07.exe"; // game executable used to launch
    public bool SelectExeAutomatically = true;
    public bool DebugMode;
    public bool VirtualShunting;

    // Graphics (subset)
    public string Resolution = "1280x720";
    public bool VSync = true;
    public bool RenderShadows = true;

    // Sound
    public bool SoundEnabled = true;
    public int Volume = 100;

    // Starter
    public bool AutoCloseStarter;
    public bool LargeThumbnails;
    public bool AutoExpandSceneryTree;

    /// <summary>Loads settings from disk. TODO: implement persistence.</summary>
    public void Load()
    {
        // intentionally empty for now
    }

    /// <summary>Persists settings to disk. TODO: implement persistence.</summary>
    public void Save()
    {
        // intentionally empty for now
    }
}
