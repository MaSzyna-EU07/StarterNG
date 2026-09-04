using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Settings;

/// <summary>
/// Where the simulator's eu07.ini lives: per user on each platform, with the
/// installation's own copy as the template for a first run.
/// </summary>
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

    /// <summary>The user's own settings file, which is what the starter writes.</summary>
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

    /// <summary>The ini shipped with the installation, read when the user has none.</summary>
    public string InstallationConfigPath() => _paths.FromRoot(ConfigFileName);

    public string UserConfigDirectory() =>
        Path.GetDirectoryName(UserConfigPath()) ?? _environment.BaseDirectory;
}
