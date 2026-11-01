using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;
using StarterNG.Domain.Tapes;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;

namespace StarterNG.Application;

public sealed record TapeFile(string Path, string Vehicle, DateTime LastWriteUtc, long Size,
                              TapeRole Role = TapeRole.Other);

/// <summary>One run of the simulator and what is left of its tapes: a later run overwrites those of the vehicles it has too.</summary>
public sealed record TapeSession(TapeLaunch? Launch, DateTime StartedUtc, DateTime LastWrittenUtc, bool Running,
                                 IReadOnlyList<TapeFile> Tapes);

/// <summary>What is known about a tape beyond its samples: the clock it started at, the recorder it is read as, the scenario and the train.</summary>
public sealed record TapeContext(TimeSpan? StartClock, RecorderKind Recorder,
                                 string? Scenery = null, string? Train = null);

/// <summary>The tapes the simulator left in physicslog/ - one per driven vehicle, overwritten every run.</summary>
public sealed class HaslerTapes
{
    private const int RememberedLaunches = 30;
    private static readonly TimeSpan QuietBetweenRuns = TimeSpan.FromMinutes(2);

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;
    private readonly IClock _clock;
    private readonly TimetableLocator _timetables;
    private readonly Func<bool> _simulatorRunning;
    private readonly object _playerLock = new();
    private IReadOnlyList<TapeLaunch>? _launches;
    private (DateTime Written, long Size) _launchesAt;
    private PlayerVehicles _player = PlayerVehicles.None;
    private DateTime _playerLaunch;
    private string? _playerLog;
    private long _playerLogRead;

    public HaslerTapes(IFileSystem files, IGamePaths paths, IDiagnosticsLog log, IClock clock,
                       TimetableLocator timetables, Func<bool> simulatorRunning)
    {
        _files = files;
        _paths = paths;
        _log = log;
        _clock = clock;
        _timetables = timetables;
        _simulatorRunning = simulatorRunning;
    }

    public string Folder => _paths.FromRoot("physicslog");

    /// <summary>Not a .dat, so the simulator's own logs are never mistaken for it, nor it for one.</summary>
    private string LaunchFile => Path.Combine(Folder, "starter-launch.txt");

    /// <summary>
    /// The simulator logs every driven vehicle under its scenery name, with the
    /// time counted from the start; the clock it starts at and what each name is
    /// are only known here. So is whose tape is the player's: a scenery with no
    /// consist picked starts with the free camera, in no vehicle at all.
    /// </summary>
    public void RecordLaunch(Scenery scenery, Trainset? trainset, string? driven)
    {
        var clock = TimeSpan.TryParseExact(scenery.Weather.ScenarioTimeOverride, @"h\:mm",
                                           CultureInfo.InvariantCulture, out var over)
            ? over
            : TimeSpan.TryParseExact(scenery.Weather.Time, @"h\:mm", CultureInfo.InvariantCulture, out var time)
                ? time
                : TimeSpan.Zero;

        var own = trainset?.Vehicles ?? new List<Dynamic>();
        var vehicles = scenery.Trainsets.SelectMany(t => t.Vehicles).Concat(own)
                              .Select(v => new TapeVehicle(v.Name, v.DataFolder ?? "", v.MmdFile ?? ""));
        RecordLaunch(Path.GetFileName(scenery.Path), clock, vehicles, driven,
                     driven is null ? null : own.Select(v => v.Name), scenery.Name,
                     driven is null ? null : _timetables.TrainNumber(scenery, trainset?.Name));
    }

    /// <summary>Notes down, next to the logs the simulator is about to write, what only the starter knows.</summary>
    public void RecordLaunch(string scenery, TimeSpan startClock, IEnumerable<TapeVehicle> vehicles,
                             string? driven = null, IEnumerable<string>? consist = null,
                             string? sceneryName = null, string? train = null)
    {
        var launch = new TapeLaunch(_clock.UtcNow, scenery, startClock,
                                    vehicles.Where(v => !string.IsNullOrEmpty(v.Name))
                                            .DistinctBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                                            .ToList(),
                                    driven, consist?.ToList(), sceneryName, train);
        try
        {
            var noted = Launches().ToList();
            // The log is about to begin anew: what it says of the run before stays with that run.
            if (noted.Count > 0)
                noted[^1] = noted[^1] with { Player = PlayerIn(noted[^1]) };
            noted.Add(launch);

            _files.CreateDirectory(Folder);
            _files.WriteAllText(LaunchFile, string.Concat(noted.TakeLast(RememberedLaunches).Select(l => l.Format())),
                                Encoding.UTF8);
            lock (_playerLock)
                _launches = null;
        }
        catch (Exception ex)
        {
            _log.Log($"writing {LaunchFile}", ex);
        }
    }

    /// <summary>
    /// The launches noted so far, oldest first. Read again only when the file has
    /// changed: this runs every few seconds next to a running simulator.
    /// </summary>
    private IReadOnlyList<TapeLaunch> Launches()
    {
        lock (_playerLock)
        {
            try
            {
                if (!_files.FileExists(LaunchFile))
                    return Array.Empty<TapeLaunch>();

                var at = (_files.GetLastWriteTimeUtc(LaunchFile), _files.GetFileSize(LaunchFile));
                if (_launches is null || at != _launchesAt)
                {
                    _launches = TapeLaunch.ParseAll(_files.ReadAllText(LaunchFile, Encoding.UTF8));
                    _launchesAt = at;
                }
                return _launches;
            }
            catch (Exception ex)
            {
                _log.Log($"reading {LaunchFile}", ex);
                return Array.Empty<TapeLaunch>();
            }
        }
    }

    /// <summary>The simulator's log of this launch: log.txt or, with logs kept, one of its own in logs/.</summary>
    private string? LogOf(TapeLaunch launch)
    {
        var logs = new List<string> { _paths.FromRoot("log.txt") };
        string kept = _paths.FromRoot("logs");
        if (_files.DirectoryExists(kept))
            logs.AddRange(_files.GetFiles(kept, "log (*.txt"));

        return logs.Where(_files.FileExists)
                   .Where(log => launch.Covers(_files.GetLastWriteTimeUtc(log)))
                   .OrderByDescending(_files.GetLastWriteTimeUtc)
                   .FirstOrDefault();
    }

    /// <summary>
    /// Where the player has been in this launch. Only what the log has gained since
    /// the last look is read: this runs every few seconds next to a running simulator.
    /// </summary>
    private PlayerVehicles PlayerIn(TapeLaunch launch)
    {
        lock (_playerLock)
        {
            string? path = null;
            try
            {
                path = LogOf(launch);
                if (path is null)
                    return PlayerVehicles.Unknown;

                long size = _files.GetFileSize(path);
                if (launch.LaunchedUtc != _playerLaunch || path != _playerLog || size < _playerLogRead)
                {
                    _player = PlayerVehicles.None;
                    _playerLaunch = launch.LaunchedUtc;
                    _playerLog = path;
                    _playerLogRead = 0;
                }
                if (size == _playerLogRead)
                    return _player;

                using var stream = _files.OpenRead(path);
                stream.Seek(_playerLogRead, SeekOrigin.Begin);
                using var added = new MemoryStream();
                stream.CopyTo(added);
                byte[] bytes = added.ToArray();

                // A line still being written waits for the next look.
                int end = Array.LastIndexOf(bytes, (byte)'\n') + 1;
                if (end > 0)
                {
                    _player = _player.After(LegacyText.Decode(bytes[..end]));
                    _playerLogRead += end;
                }
            }
            catch (Exception ex)
            {
                _log.Log($"reading {path}", ex);
            }
            return _player;
        }
    }

    /// <summary>The vehicle the player is in, or went off in while the log has not said otherwise, if the starter launched one.</summary>
    public string? DrivenVehicle =>
        Launches().LastOrDefault() is { } launch ? PlayerIn(launch).Current ?? launch.Driven : null;

    public TapeContext ContextOf(TapeFile file)
    {
        var launch = Launches().LastOrDefault(l => l.Covers(file.LastWriteUtc));
        if (launch is null)
            return new TapeContext(null, Recorders.Guess(null, file.Vehicle));

        var vehicle = launch.Find(file.Vehicle);
        string scenery = string.IsNullOrWhiteSpace(launch.SceneryName) ? launch.Scenery : launch.SceneryName;
        return new TapeContext(launch.StartClock, Recorders.Guess(vehicle, file.Vehicle),
                               string.IsNullOrWhiteSpace(scenery) ? null : scenery,
                               launch.RoleOf(file.Vehicle) == TapeRole.Other ? null : launch.Train);
    }

    /// <summary>Every tape, session by session.</summary>
    public IReadOnlyList<TapeFile> List() => Sessions().SelectMany(session => session.Tapes).ToList();

    /// <summary>
    /// The runs the tapes came from, the running or latest one first. A tape belongs
    /// to the last launch before it was written; one written before any known launch
    /// goes with those written about the same time, as a run writes them all at its end.
    /// </summary>
    public IReadOnlyList<TapeSession> Sessions()
    {
        try
        {
            var launches = Launches();
            var tapes = _files.DirectoryExists(Folder)
                ? _files.GetFiles(Folder, "*.dat").Select(ReadTape).ToList()
                : new List<TapeFile>();
            var behind = tapes.GroupBy(tape => launches.LastOrDefault(l => l.Covers(tape.LastWriteUtc)))
                              .Where(group => group.Key is not null)
                              .ToDictionary(group => group.Key!, group => group.ToList());

            var sessions = new List<TapeSession>();
            foreach (var launch in launches)
            {
                bool last = ReferenceEquals(launch, launches[^1]);
                bool running = last && _simulatorRunning();
                if (!running && !behind.ContainsKey(launch))
                    continue;

                var player = last ? PlayerIn(launch) : launch.Player ?? PlayerVehicles.Unknown;
                sessions.Add(Session(launch, running, behind.GetValueOrDefault(launch, new List<TapeFile>())
                                                            .Select(t => t with { Role = launch.RoleOf(t.Vehicle, player) })));
            }

            var run = new List<TapeFile>();
            foreach (var tape in tapes.Where(t => !launches.Any(l => l.Covers(t.LastWriteUtc))).OrderBy(t => t.LastWriteUtc))
            {
                if (run.Count > 0 && tape.LastWriteUtc - run[^1].LastWriteUtc > QuietBetweenRuns)
                {
                    sessions.Add(Session(null, running: false, run));
                    run = new List<TapeFile>();
                }
                run.Add(tape);
            }
            if (run.Count > 0)
                sessions.Add(Session(null, running: false, run));

            return sessions.OrderByDescending(session => session.Running)
                           .ThenByDescending(session => session.LastWrittenUtc)
                           .ToList();
        }
        catch (Exception ex)
        {
            _log.Log($"listing {Folder}", ex);
            return Array.Empty<TapeSession>();
        }
    }

    private TapeFile ReadTape(string path) =>
        new(path, Path.GetFileNameWithoutExtension(path), _files.GetLastWriteTimeUtc(path), _files.GetFileSize(path));

    /// <summary>The player's own tapes first: every manned vehicle leaves one.</summary>
    private static TapeSession Session(TapeLaunch? launch, bool running, IEnumerable<TapeFile> tapes)
    {
        var listed = tapes.OrderByDescending(tape => tape.Role).ThenByDescending(tape => tape.LastWriteUtc).ToList();
        var started = launch?.LaunchedUtc ?? listed.Min(tape => tape.LastWriteUtc);
        return new TapeSession(launch, started, listed.Count > 0 ? listed.Max(tape => tape.LastWriteUtc) : started,
                               running, listed);
    }

    public HaslerTape? Load(TapeFile file)
    {
        try
        {
            // Through OpenRead, which does not mind the simulator still writing the file.
            using var stream = _files.OpenRead(file.Path);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            return HaslerTape.Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            _log.Log($"reading {file.Path}", ex);
            return null;
        }
    }
}
