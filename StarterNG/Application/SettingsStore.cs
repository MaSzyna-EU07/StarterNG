using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Keys the screen shows otherwise than the settings hold them - a value its
    /// control cannot show - by key, in the file's terms.
    /// </summary>
    IReadOnlyDictionary<string, ShownValue> ShownUnlikeSettings => new Dictionary<string, ShownValue>();
}

/// <summary>A key as the screen shows it, and as the settings held it when it was put there; null for absent.</summary>
public sealed record ShownValue(string? Shown, string? Held);

/// <summary>A key a save changes: its value in the file before and after; null where it has none.</summary>
public sealed record IniChange(string Key, string? Before, string? After);

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

    // The settings in the file's terms when it was last read or written. A key
    // they still put the same way was not changed here, so the file keeps it as
    // it has it: a value outside what the starter offers, its spacing, its comment.
    private IReadOnlyDictionary<string, string> _synced = new Dictionary<string, string>();

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

    /// <summary>
    /// The last read failed, so <see cref="Settings"/> holds defaults, not the file's.
    /// Nothing is saved until a read works: that would put them in its place.
    /// </summary>
    public bool LoadFailed { get; private set; }

    /// <summary>The last save did not reach the file; the log says why.</summary>
    public bool LastSaveFailed { get; private set; }

    public string SavePath => _savePath;

    public void RegisterCapture(ISettingsCapture capture) => _capture = capture;

    public void Load()
    {
        _savePath = _paths.UserConfigPath();
        string source = _files.FileExists(_savePath) ? _savePath : _paths.InstallationConfigPath();
        LoadedFrom = source;
        LoadFrom(source);
    }

    /// <summary>A player with no settings of their own yet starts at High, in the file at once so the simulator goes by it too.</summary>
    public void StartAtHighWithoutOwnSettings()
    {
        if (LoadFailed || _files.FileExists(_savePath))
            return;

        GraphicsPreset.High.ApplyTo(Settings);
        Save();
    }

    public void LoadFrom(string path)
    {
        try
        {
            _config = _files.FileExists(path)
                ? ConfigFile.Parse(_files.ReadAllText(path, _encoding))
                : new ConfigFile();
            LoadFailed = false;
        }
        catch (Exception ex)
        {
            _log.Log($"reading {path}", ex);
            _config = new ConfigFile();
            LoadFailed = true;
        }

        _serializer.Read(Settings, _config);
        _synced = Render(Settings);
        TouchConfigAge();
    }

    /// <summary>
    /// Reads a profile into the settings; true when it read cleanly. A read that failed
    /// would leave defaults to be saved over the real settings, so those are read again.
    /// </summary>
    public bool TryLoadProfile(string path)
    {
        if (_files.FileExists(path))
        {
            LoadFrom(path);
            if (!LoadFailed)
                return true;
        }

        if (LoadFailed)
            Load();
        return false;
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
        LastSaveFailed = true;
        if (LoadFailed)
        {
            _log.Log($"{path} not saved: the settings could not be read, what is held is defaults");
            return;
        }

        foreach (var change in Changes(Settings))
        {
            if (change.After is null)
                _config.Remove(change.Key);
            else
                _config.Set(change.Key, change.After);
        }
        // Each sets the mode as the simulator reads it, so this one has to come later to count.
        _config.MoveAfter("gfx.antialiasing", "multisampling");
        _synced = Render(Settings);

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                _files.CreateDirectory(directory);

            _files.WriteAllBytesAtomic(path, _encoding.GetBytes(_config.ToText()));
            TouchConfigAge();
            LastSaveFailed = false;
        }
        catch (Exception ex)
        {
            _log.Log($"saving {path}", ex);
        }
    }

    /// <summary>Every key as <paramref name="settings"/> would put it, in the order they are written.</summary>
    public IReadOnlyDictionary<string, string> Render(SimulatorSettings settings)
    {
        var config = new ConfigFile();
        _serializer.Write(settings, config);
        var keys = new OrderedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in config.Entries())
            keys[key] = value;
        return keys;
    }

    /// <summary>
    /// What saving <paramref name="settings"/> would change in the file: the keys
    /// changed since it was read, those it lacks, and the ones no simulator reads.
    /// A key the screen shows otherwise than the file has it is left alone until
    /// moved. One the file leaves bare or past endconfig counts as lacking.
    /// </summary>
    public IReadOnlyList<IniChange> Changes(SimulatorSettings settings)
    {
        var now = Render(settings);
        var shown = _capture?.ShownUnlikeSettings ?? new Dictionary<string, ShownValue>();
        var changes = new List<IniChange>();

        foreach (string key in SettingsSerializer.ObsoleteKeys)
            Add(key, null);
        foreach (string key in SettingsSerializer.FixedLengthKeys.Where(key => !now.ContainsKey(key)))
            Add(key, null);
        foreach (string key in SettingsSerializer.OptionalKeys.Concat(SimulatorKeys.All.Select(key => key.Key)))
            if (!now.ContainsKey(key) && _config.GetRaw(key) is { Length: 0 })
                Add(key, null);
        foreach (var (key, value) in now)
            if (!Kept(key, value))
                Add(key, value);
        foreach (string key in _synced.Keys.Where(key => !now.ContainsKey(key)))
            if (!Kept(key, null))
                Add(key, null);
        return changes;

        bool Kept(string key, string? value)
        {
            // Past endconfig the simulator never gets to it; bare, it takes the next
            // word of the file for its value.
            if (_config.IsPastEnd(key) || _config.GetRaw(key) is { Length: 0 })
                return false;
            if (string.Equals(key, "uartfeature", StringComparison.OrdinalIgnoreCase))
                return Uart.FeaturesStand(_config.GetRaw(key), value);
            if (SettingsSerializer.FixedLengthKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                return _config.GetRaw(key) == value;
            string? synced = _synced.GetValueOrDefault(key);
            if (shown.TryGetValue(key, out var onScreen) && onScreen.Shown == value && onScreen.Held == synced)
                return true;
            return _config.Has(key) && synced == value;
        }

        void Add(string key, string? value)
        {
            // To the simulator a key past endconfig is not set yet, whatever it says.
            string? before = value is not null && _config.IsPastEnd(key) ? null : _config.GetRaw(key);
            if (before != value)
                changes.Add(new IniChange(key, before, value));
        }
    }

    /// <summary>
    /// The keys <paramref name="shown"/> puts otherwise than the settings hold them,
    /// with both values, in the file's terms.
    /// </summary>
    public IReadOnlyDictionary<string, ShownValue> ShownUnlike(SimulatorSettings shown)
    {
        var held = Render(Settings);
        var now = Render(shown);
        var unlike = new Dictionary<string, ShownValue>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in held.Keys.Union(now.Keys, StringComparer.OrdinalIgnoreCase))
        {
            string? before = held.GetValueOrDefault(key), after = now.GetValueOrDefault(key);
            if (before != after)
                unlike[key] = new ShownValue(after, before);
        }
        return unlike;
    }

    /// <summary>
    /// A copy of <paramref name="settings"/> with <paramref name="keys"/> back at
    /// what the simulator goes by when eu07.ini does not set them.
    /// </summary>
    public SimulatorSettings WithDefaults(SimulatorSettings settings, IReadOnlyCollection<string> keys)
    {
        var defaults = new SimulatorSettings();
        _serializer.Read(defaults, new ConfigFile());
        return WithKeysOf(settings, defaults, keys);
    }

    /// <summary>A copy of <paramref name="settings"/> with <paramref name="keys"/> as <paramref name="source"/> has them.</summary>
    public SimulatorSettings WithKeysOf(SimulatorSettings settings, SimulatorSettings source, IReadOnlyCollection<string> keys)
    {
        var taken = Render(source);

        var config = new ConfigFile();
        foreach (var (key, value) in Render(settings))
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                config.Set(key, value);
        foreach (string key in keys)
            if (taken.TryGetValue(key, out string? value))
                config.Set(key, value);

        var copy = settings.Copy();
        _serializer.Read(copy, config);
        return copy;
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
