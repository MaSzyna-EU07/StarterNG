using System;
using System.Collections.Generic;
using System.Globalization;
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

    // Keys set by the player's hand since the file was read: written as the screen has them,
    // even where that is what the starter shows for a value of the file it does not know.
    private readonly HashSet<string> _chosen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="keys"/> were set by hand - a control moved, a level or the defaults
    /// put on them, the move to the standard mode: a save writes them, and a value of the
    /// file the starter does not know goes. Until then it stays as it is.
    /// </summary>
    public void Choose(IEnumerable<string> keys) => _chosen.UnionWith(keys);

    /// <summary>The value of the file is chosen again, as it is: it stays in the file.</summary>
    public void Unchoose(IEnumerable<string> keys) => _chosen.ExceptWith(keys);

    public bool IsChosen(string key) => _chosen.Contains(key);

    /// <summary>
    /// Whether the file's value of <paramref name="key"/> means what <paramref name="shown"/> does,
    /// written its own way: yes and tak, 45 and 45.0, the UART features in another order.
    /// </summary>
    public bool FileMeans(string key, string? shown)
    {
        string? raw = FileValueOf(key);
        return raw is null || shown is null ? raw == shown : Means(key, raw, shown);
    }

    private static bool Means(string key, string raw, string shown) =>
        string.Equals(key, "uartfeature", StringComparison.OrdinalIgnoreCase)
            ? Uart.FeaturesStand(raw, shown)
            : SameValue(raw, shown);

    /// <summary>The key as the file writes it, where the simulator gets to it; null where it does not.</summary>
    public string? FileValueOf(string key) =>
        _config.IsPastEnd(key) || _config.GetRaw(key) is not { Length: > 0 } raw ? null : raw;

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

    /// <summary>The last save found eu07.ini changed by something else and kept those changes: what is held was read again.</summary>
    public bool LastSaveMerged { get; private set; }

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
        // Nothing of an older starter to bring over: the settings start in the standard mode.
        Settings.MovedToStandard = true;
        Save();
    }

    /// <summary>
    /// A copy of eu07.ini beside it, the file name and <paramref name="suffix"/>, before
    /// something changes many keys at once; one already there is left as it is.
    /// </summary>
    public void KeepCopy(string suffix)
    {
        string path = string.IsNullOrEmpty(_savePath) ? _paths.UserConfigPath() : _savePath;
        string copy = path + suffix;
        try
        {
            if (_files.FileExists(path) && !_files.FileExists(copy))
                _files.CopyFile(path, copy, overwrite: false);
        }
        catch (Exception e)
        {
            _log.Log($"{copy} could not be made: {e.Message}");
        }
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
        _chosen.Clear();
        // after the snapshot, so the next save writes it; a profile of before goes the same way as the ini would
        if (!LoadFailed)
            SettingsSerializer.MoveToTrainTapes(Settings);
        TouchConfigAge();
    }

    /// <summary>
    /// What lets some machines run the simulator at all: the card it is put on (offloading
    /// to the discrete one fails with some drivers) and OpenGL ES where desktop OpenGL does
    /// not work. The move to the standard mode, done without asking, leaves them as they are.
    /// </summary>
    public static readonly IReadOnlySet<string> KeptOnMoveToStandard =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "starter.gpu", "gfx.usegles" };

    /// <summary>
    /// The mode is this installation's, like its simulator and its card: another file, a
    /// backup or a profile of an older starter, says nothing of it. Without this, its
    /// "starter.settings.standard no" would move the player again on the next start, and its
    /// "starter.settings.advanced yes" would open the advanced mode past the warning.
    /// The returned action puts the mode back as it was when this was called.
    /// </summary>
    private Action KeepMode()
    {
        bool advanced = Settings.AdvancedSettings, moved = Settings.MovedToStandard;
        return () =>
        {
            Settings.AdvancedSettings = advanced;
            Settings.MovedToStandard = moved;
        };
    }

    /// <summary>
    /// Reads a profile into the settings; true when it read cleanly. A read that failed
    /// would leave defaults to be saved over the real settings, so those are read again.
    /// </summary>
    public bool TryLoadProfile(string path)
    {
        if (_files.FileExists(path))
        {
            var mode = KeepMode();
            LoadFrom(path);
            if (!LoadFailed)
            {
                mode();
                return true;
            }
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

    private void SaveTo(string path)
    {
        LastSaveFailed = true;
        if (LoadFailed)
        {
            _log.Log($"{path} not saved: the settings could not be read, what is held is defaults");
            return;
        }

        // Edited by other hands since it was read or written here: their file is what the changes
        // go onto, so a key the player left alone keeps what was put there meanwhile.
        bool merged = false;
        if (string.Equals(path, _savePath, StringComparison.Ordinal) && IsConfigNewerOnDisk())
        {
            try
            {
                _config = ConfigFile.Parse(_files.ReadAllText(path, _encoding));
                merged = true;
            }
            catch (Exception ex)
            {
                _log.Log($"re-reading {path} before saving", ex);
            }
        }

        ApplyChanges(_config, Changes(Settings));
        if (merged)
        {
            // What is held follows the file: the player's changes and the others' together.
            _serializer.Read(Settings, _config);
        }
        _synced = Render(Settings);
        LastSaveMerged = merged;

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

    private static void ApplyChanges(ConfigFile config, IReadOnlyList<IniChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.After is null)
                config.Remove(change.Key);
            else
                config.Set(change.Key, change.After);
        }
    }

    /// <summary>
    /// Takes the settings of another eu07.ini (a backup, a file from another machine)
    /// into the current ones, keeping what belongs to this installation: the chosen
    /// simulator, its card, the last scenery and the window state. The lines of this
    /// user's file the starter does not manage stay as they are. Nothing is written until Save.
    /// </summary>
    public bool ImportFrom(string path)
    {
        ConfigFile imported;
        try
        {
            imported = ConfigFile.Parse(_files.ReadAllText(path, _encoding));
        }
        catch (Exception ex)
        {
            _log.Log($"importing {path}", ex);
            return false;
        }

        var s = Settings;
        bool selectAutomatically = s.SelectExeAutomatically;
        string executable = s.ExecutablePath;
        var gpu = s.Gpu;
        string lastScenery = s.LastScenery;
        bool maximized = s.WindowMaximized;
        var mode = KeepMode();

        _serializer.Read(s, imported);

        s.SelectExeAutomatically = selectAutomatically;
        s.ExecutablePath = executable;
        s.Gpu = gpu;
        s.LastScenery = lastScenery;
        s.WindowMaximized = maximized;
        mode();
        // an ini of before the tapes of the trains goes the same way as a profile
        SettingsSerializer.MoveToTrainTapes(s);
        return true;
    }

    /// <summary>
    /// Writes the settings as a complete eu07.ini somewhere else - this user's file
    /// with the settings applied - leaving the file itself and its state alone.
    /// </summary>
    /// <param name="settings">What to write, when not the settings held: a page's edits, still not taken in.</param>
    public bool ExportTo(string path, SimulatorSettings? settings = null)
    {
        if (LoadFailed)
        {
            _log.Log($"{path} not written: the settings could not be read, what is held is defaults");
            return false;
        }

        var copy = ConfigFile.Parse(_config.ToText());
        ApplyChanges(copy, Changes(settings ?? Settings));

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                _files.CreateDirectory(directory);

            _files.WriteAllBytesAtomic(path, _encoding.GetBytes(copy.ToText()));
            return true;
        }
        catch (Exception ex)
        {
            _log.Log($"exporting {path}", ex);
            return false;
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
    /// The keys the starter knows that the file gives a value it cannot read, with that value:
    /// the setting shows its default, while the file keeps its own until the setting is changed.
    /// A value read another way (tak, 1.0, a clamped number) is no unknown one.
    /// </summary>
    public IReadOnlyDictionary<string, string> UnreadableValues()
    {
        var unreadable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var defaults = Render(WithFile(new ConfigFile()));
        var read = Render(WithFile(_config));
        var overCap = SettingsSerializer.OverCapKeys(_config);
        foreach (var (key, fallback) in defaults)
        {
            // Read, only brought down to the cap.
            if (NotReadAlone.Contains(key) || overCap.Contains(key))
                continue;
            if (_config.IsPastEnd(key) || _config.GetRaw(key) is not { Length: > 0 } raw)
                continue;
            if (read.TryGetValue(key, out string? shown) && shown == fallback && !Means(key, raw, shown))
                unreadable[key] = raw;
        }

        foreach (var key in SimulatorKeys.All)
        {
            if (_config.IsPastEnd(key.Key) || _config.GetRaw(key.Key) is not { Length: > 0 } raw)
                continue;
            string first = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            bool readable = key.Kind switch
            {
                SimulatorKeyKind.Toggle => Bool(first) is not null,
                SimulatorKeyKind.Number => double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                _ => true
            };
            if (!readable)
                unreadable[key.Key] = raw;
        }

        // Kept as written, and offered on the list under its own word.
        if (!_config.IsPastEnd("gfxrenderer") && WithFile(_config).RendererWord is { } renderer)
            unreadable["gfxrenderer"] = renderer;
        return unreadable;
    }

    // Written from other keys, or rewritten on purpose whatever they say: what they hold is no setting of their own.
    private static readonly HashSet<string> NotReadAlone = new(StringComparer.OrdinalIgnoreCase)
    {
        "mousescale", "gfxrenderer", "gfx.shadowmap.enabled", "gfx.framebuffer.width", "gfx.framebuffer.height"
    };

    private SimulatorSettings WithFile(ConfigFile config)
    {
        var s = new SimulatorSettings();
        _serializer.Read(s, config);
        return s;
    }

    // Two numbers written apart that the file and the screen mean alike: 45 and 45.0, 0.1 and 1e-1.
    private static bool SameNumber(double x, double y) =>
        Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)));

    // The same value in other words: yes and tak, 45 and 45.0.
    private static bool SameValue(string raw, string shown)
    {
        var a = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var b = shown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                continue;
            if (double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(b[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && SameNumber(x, y))
                continue;
            if (Bool(a[i]) is { } p && p == Bool(b[i]))
                continue;
            return false;
        }
        return true;
    }

    private static bool? Bool(string word) => word.ToLowerInvariant() switch
    {
        "yes" or "true" or "1" or "tak" => true,
        "no" or "false" or "0" or "nie" => false,
        _ => null
    };

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
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Above the starter's caps the file is brought down, though nothing on screen moved.
        var overCap = SettingsSerializer.OverCapKeys(_config);

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
            if (_chosen.Contains(key))
                return false;
            if (string.Equals(key, "mousescale", StringComparison.OrdinalIgnoreCase) &&
                !SettingsSerializer.MouseScaleStands(_config.GetRaw(key)))
                return false;
            if (overCap.Contains(key))
                return false;
            // The simulator goes by these to the letter: a buffer of a window's size stretched
            // over the monitor, a monitor's name it does not find. The file's own is no choice to keep.
            if (AsWritten(key) && _config.GetRaw(key) != value)
                return false;
            string? synced = _synced.GetValueOrDefault(key);
            if (shown.TryGetValue(key, out var onScreen) && onScreen.Shown == value && onScreen.Held == synced)
                return true;
            return _config.Has(key) && synced == value;
        }

        bool AsWritten(string key) =>
            string.Equals(key, "fullscreenmonitor", StringComparison.OrdinalIgnoreCase) ||
            (settings.FullscreenWindowed &&
             SettingsSerializer.FramebufferKeys.Contains(key, StringComparer.OrdinalIgnoreCase));

        void Add(string key, string? value)
        {
            // To the simulator a key past endconfig is not set yet, whatever it says.
            string? before = value is not null && _config.IsPastEnd(key) ? null : _config.GetRaw(key);
            // A key gone from the file can be found gone more ways than one.
            if (before != value && listed.Add(key))
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
        keys = SettingsSerializer.WithCompanions(keys);
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

    // When the file stood as it was last asked about; a save still goes onto what it holds.
    private DateTime _askedAboutUtc = DateTime.MinValue;

    /// <summary>
    /// Whether the file changed elsewhere since it was read here, written here or last asked
    /// about. Declined (<see cref="LeaveChangeOnDisk"/>), a change is not asked about again.
    /// </summary>
    public bool HasChangeOnDiskToAskAbout() =>
        IsConfigNewerOnDisk() && DiskWriteTimeUtc() is { } written && written > _askedAboutUtc;

    /// <summary>
    /// The player chose not to read the file again. The screen stays as it is, but what was
    /// written in elsewhere is no more lost for that: the next save goes onto it.
    /// </summary>
    public void LeaveChangeOnDisk() => _askedAboutUtc = DiskWriteTimeUtc() ?? _clock.UtcNow;

    private DateTime? DiskWriteTimeUtc()
    {
        string path = string.IsNullOrEmpty(_savePath) ? _paths.UserConfigPath() : _savePath;
        try
        {
            return _files.FileExists(path) ? _files.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception)
        {
            return null;
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
