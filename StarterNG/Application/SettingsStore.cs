using System;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Application;

/// <summary>
/// A screen that holds settings the user is editing and can be asked to write
/// them back into the model before a save.
/// </summary>
/// <remarks>
/// Replaces the mutable <c>Action? CaptureFromUi</c> the settings model used to
/// expose: the dependency now points the right way, from the store to an
/// interface the view implements.
/// </remarks>
public interface ISettingsCapture
{
    void CaptureInto(SimulatorSettings settings);
}

/// <summary>
/// Owns the settings the starter runs with: where they were read from, when to
/// write them back, and which executable they point at.
/// </summary>
public sealed class SettingsStore
{
    private readonly IFileSystem _files;
    private readonly IClock _clock;
    private readonly IDiagnosticsLog _log;
    private readonly SettingsPaths _paths;
    private readonly SettingsSerializer _serializer;
    private readonly ExecutableLocator _executables;
    private readonly Encoding _encoding;

    private ConfigFile _config = new();
    private string _savePath = string.Empty;
    private DateTime _configAgeUtc = DateTime.MinValue;
    private ISettingsCapture? _capture;

    public SettingsStore(IFileSystem files, IClock clock, IDiagnosticsLog log, SettingsPaths paths,
                         SettingsSerializer serializer, ExecutableLocator executables)
    {
        _files = files;
        _clock = clock;
        _log = log;
        _paths = paths;
        _serializer = serializer;
        _executables = executables;
        _encoding = LegacyText.CodePage1250;
    }

    /// <summary>The settings the application is running with.</summary>
    public SimulatorSettings Settings { get; } = new();

    /// <summary>The file the current settings were read from.</summary>
    public string LoadedFrom { get; private set; } = string.Empty;

    /// <summary>The file a save will write to.</summary>
    public string SavePath => _savePath;

    /// <summary>
    /// Registers the screen that holds pending edits, so a save can collect them
    /// first. Called by the settings view when it is created.
    /// </summary>
    public void RegisterCapture(ISettingsCapture capture) => _capture = capture;

    /// <summary>
    /// Reads the user's settings, falling back to the copy shipped with the
    /// installation on a first run.
    /// </summary>
    public void Load()
    {
        _savePath = _paths.UserConfigPath();
        string source = _files.FileExists(_savePath) ? _savePath : _paths.InstallationConfigPath();
        LoadedFrom = source;
        LoadFrom(source);
    }

    public void LoadFrom(string path)
    {
        try
        {
            _config = _files.FileExists(path)
                ? ConfigFile.Parse(_files.ReadAllText(path, _encoding))
                : new ConfigFile();
        }
        catch (Exception ex)
        {
            // An unreadable ini means defaults, not a failed start.
            _log.Log($"reading {path}", ex);
            _config = new ConfigFile();
        }

        _serializer.Read(Settings, _config);
        TouchConfigAge();
    }

    /// <summary>Collects pending edits from the settings screen, then saves.</summary>
    public void CaptureAndSave()
    {
        _capture?.CaptureInto(Settings);
        Save();
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(_savePath))
            _savePath = _paths.UserConfigPath();

        SaveTo(_savePath);
    }

    public void SaveTo(string path)
    {
        _serializer.Write(Settings, _config);

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                _files.CreateDirectory(directory);

            _files.WriteAllText(path, _config.ToText(), _encoding);
            TouchConfigAge();
        }
        catch (Exception ex)
        {
            _log.Log($"saving {path}", ex);
        }
    }

    /// <summary>Records the ini's timestamp, so an outside edit can be noticed.</summary>
    public void TouchConfigAge()
    {
        string path = string.IsNullOrEmpty(_savePath) ? _paths.UserConfigPath() : _savePath;
        try
        {
            _configAgeUtc = _files.FileExists(path) ? _files.GetLastWriteTimeUtc(path) : _clock.UtcNow;
        }
        catch (Exception)
        {
            _configAgeUtc = _clock.UtcNow;
        }
    }

    /// <summary>
    /// True when something else has written the ini since we last touched it -
    /// the simulator itself, usually, which the starter must not clobber.
    /// </summary>
    public bool IsConfigNewerOnDisk()
    {
        string path = string.IsNullOrEmpty(_savePath) ? _paths.UserConfigPath() : _savePath;
        try
        {
            if (!_files.FileExists(path) || _configAgeUtc == DateTime.MinValue)
                return false;

            // A second of slack: writing our own file must not count as an edit.
            return _files.GetLastWriteTimeUtc(path) > _configAgeUtc + TimeSpan.FromSeconds(1);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string ResolveExecutable() => _executables.Resolve(Settings, out _);

    public string ResolveExecutable(out ExeProblem problem) => _executables.Resolve(Settings, out problem);

}
