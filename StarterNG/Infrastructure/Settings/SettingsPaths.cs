using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Settings;

public sealed class SettingsPaths
{
    private const string ConfigFileName = "eu07.ini";
    private const string VendorFolder = "MaSzyna";

    private readonly IEnvironment _environment;
    private readonly IGamePaths _paths;

    public SettingsPaths(IEnvironment environment, IGamePaths paths)
    {
        _environment = environment;
        _paths = paths;
    }

    public string UserConfigPath()
    {
        if (_environment.IsWindows)
        {
            string? appData = _environment.GetVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, VendorFolder, ConfigFileName);
        }
        else if (_environment.IsMacOS)
        {
            string? home = _environment.GetVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, "Library", "Application Support", VendorFolder, ConfigFileName);
        }
        else
        {
            string? home = _environment.GetVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".config", VendorFolder, ConfigFileName);
        }

        return Path.Combine(_environment.BaseDirectory, ConfigFileName);
    }

    public string InstallationConfigPath() => _paths.FromRoot(ConfigFileName);

    /// <summary>
    /// Where the starter keeps its own copy of the vehicle database. Per user
    /// rather than under the installation: that one can be read-only, is a working
    /// copy someone updates from under us, and the copy is ours, not the game's.
    /// </summary>
    public string VehicleCacheDirectory() => Path.Combine(UserConfigDirectory(), "vehicles");

    /// <summary>The day of the last update check and the release it found.</summary>
    public string UpdateCheckPath() => Path.Combine(UserConfigDirectory(), "updatecheck.txt");

    public string UserConfigDirectory() =>
        Path.GetDirectoryName(UserConfigPath()) ?? _environment.BaseDirectory;
}
