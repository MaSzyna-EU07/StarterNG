using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Settings;

public sealed class ExecutableLocator
{
    private static readonly string[] NeverExecutable =
    {
        ".ini", ".log", ".txt", ".cfg", ".config", ".json", ".dll", ".so", ".dat", ".bak", ".csv", ".xml"
    };

    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IEnvironment _environment;
    private readonly IDiagnosticsLog _log;

    private string? _wine;
    private bool _wineLookedUp;
    private string? _missingPickLogged;

    public ExecutableLocator(IFileSystem files, IGamePaths paths, IEnvironment environment, IDiagnosticsLog log)
    {
        _files = files;
        _paths = paths;
        _environment = environment;
        _log = log;
    }

    public string CanonicalName => _environment.IsWindows ? "eu07.exe" : "eu07";

    /// <summary>
    /// The simulator to start: the one picked in the settings, or the best one in
    /// the installation. A pick that is not there - the settings live per user and
    /// are shared by every installation, so it may come from another one - gives
    /// way to the search rather than stopping the start.
    /// </summary>
    public string Resolve(SimulatorSettings settings, out ExeProblem problem)
    {
        if (!settings.SelectExeAutomatically && !string.IsNullOrWhiteSpace(settings.ExecutablePath))
        {
            problem = Validate(FullPath(settings.ExecutablePath));
            if (problem != ExeProblem.NotFound)
                return settings.ExecutablePath;

            string found = Search(out var searched);
            if (searched == ExeProblem.NotFound)
                return settings.ExecutablePath;

            // Resolved on every start and for the title bar, so said once.
            if (_missingPickLogged != settings.ExecutablePath)
            {
                _log.Log($"The chosen simulator {settings.ExecutablePath} is not in {_paths.Root}, using {found}");
                _missingPickLogged = settings.ExecutablePath;
            }
            problem = searched;
            return found;
        }

        return Search(out problem);
    }

    private string Search(out ExeProblem problem)
    {
        string? fallback = null;
        try
        {
            var candidates = _files.GetFiles(_paths.Root, "eu07*")
                .Where(IsCandidate)
                .OrderBy(path => IsExeOnLinux(path) ? 1 : 0)
                .ThenBy(path => string.Equals(Path.GetFileName(path), CanonicalName,
                                              StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => Path.GetFileName(path).Length);

            foreach (string path in candidates)
            {
                if (Validate(path) == ExeProblem.None)
                {
                    problem = ExeProblem.None;
                    return path;
                }
                fallback ??= path;
            }
        }
        catch (Exception ex)
        {
            _log.Log("looking for the simulator executable", ex);
        }

        if (fallback is not null)
        {
            problem = Validate(fallback);
            return fallback;
        }

        problem = ExeProblem.NotFound;
        return CanonicalName;
    }

    public List<string> ListCandidates(string? directory = null)
    {
        try
        {
            return _files.GetFiles(directory ?? _paths.Root, "eu07*")
                .Where(IsCandidate)
                .OrderByDescending(_files.GetLastWriteTimeUtc)
                .Select(Path.GetFileName)
                .ToList()!;
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    public ExeProblem Validate(string path)
    {
        try
        {
            if (!_files.FileExists(path))
                return ExeProblem.NotFound;

            bool foreign = !MatchesPlatform(path);

            // Started through Wine, a Windows build needs no execute bit of its own.
            if (foreign && RunsThroughWine(path))
                return ExeProblem.WrongPlatform;

            if (!_environment.IsWindows && !_files.IsExecutable(path))
                return ExeProblem.NotExecutable;

            return foreign ? ExeProblem.WrongPlatform : ExeProblem.None;
        }
        catch (Exception)
        {
            return ExeProblem.NotFound;
        }
    }

    /// <summary>
    /// What to start for a simulator binary: the binary itself, or Wine with the
    /// binary as its first argument when it is a Windows build on Linux.
    /// </summary>
    public (string FileName, IReadOnlyList<string> Arguments) LaunchCommand(string executable,
                                                                            IReadOnlyList<string> arguments)
    {
        if (!RunsThroughWine(executable))
            return (executable, arguments);

        return (Wine()!, new[] { executable }.Concat(arguments).ToList());
    }

    /// <summary>Wine from the PATH, or null where there is none - and always on Windows.</summary>
    public string? Wine()
    {
        if (_environment.IsWindows)
            return null;

        if (!_wineLookedUp)
        {
            _wine = FindOnPath("wine") ?? FindOnPath("wine64");
            _wineLookedUp = true;
        }

        return _wine;
    }

    private string? FindOnPath(string name)
    {
        // The PATH of Linux and macOS, whatever the host the tests run on.
        string[] directories = (_environment.GetVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (string directory in directories)
        {
            string candidate = Path.Combine(directory, name);
            if (_files.FileExists(candidate) && _files.IsExecutable(candidate))
                return candidate;
        }

        return null;
    }

    private bool RunsThroughWine(string path)
    {
        try
        {
            return !_environment.IsWindows && Wine() is not null && Format(path) == BinaryFormat.PortableExecutable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool MatchesPlatform(string path) => Format(path) switch
    {
        BinaryFormat.Elf => !_environment.IsWindows,
        BinaryFormat.PortableExecutable => !_environment.IsLinux,
        _ => true
    };

    private enum BinaryFormat
    {
        Unknown,
        Elf,
        PortableExecutable
    }

    private BinaryFormat Format(string path)
    {
        byte[] head = new byte[4];
        using var stream = _files.OpenRead(path);
        if (stream.Read(head, 0, 4) != 4)
            return BinaryFormat.Unknown;

        if (head[0] == 0x4D && head[1] == 0x5A)
            return BinaryFormat.PortableExecutable;
        if (head[0] == 0x7F && head[1] == 0x45 && head[2] == 0x4C && head[3] == 0x46)
            return BinaryFormat.Elf;

        return BinaryFormat.Unknown;
    }

    private bool IsExeOnLinux(string path) =>
        _environment.IsLinux && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private bool IsCandidate(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (NeverExecutable.Contains(extension))
            return false;

        if (_environment.IsWindows)
            return extension == ".exe";

        // A Windows build is offered only where Wine can start it, and after any
        // native one - see the ordering in Resolve.
        if (_environment.IsLinux)
            return extension.Length == 0 || extension is ".x86_64" or ".run" or ".appimage" ||
                   (IsExeOnLinux(path) && Wine() is not null);

        return extension.Length == 0 || extension is ".exe" or ".x86_64" or ".run" or ".appimage";
    }

    private string FullPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(_paths.Root, path);
}
