using System;
using System.IO;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Application;

public interface ISettingsCapture
{
    void CaptureInto(SimulatorSettings settings);
}

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

    public SimulatorSettings Settings { get; } = new();

    public string LoadedFrom { get; private set; } = string.Empty;

    public string SavePath => _savePath;

    public void RegisterCapture(ISettingsCapture capture) => _capture = capture;

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
            _log.Log($"reading {path}", ex);
            _config = new ConfigFile();
        }

        _serializer.Read(Settings, _config);
        TouchConfigAge();
    }

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

    public bool IsConfigNewerOnDisk()
    {
        string path = string.IsNullOrEmpty(_savePath) ? _paths.UserConfigPath() : _savePath;
        try
        {
            if (!_files.FileExists(path) || _configAgeUtc == DateTime.MinValue)
                return false;

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
