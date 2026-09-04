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

    public ExecutableLocator(IFileSystem files, IGamePaths paths, IEnvironment environment, IDiagnosticsLog log)
    {
        _files = files;
        _paths = paths;
        _environment = environment;
        _log = log;
    }

    public string CanonicalName => _environment.IsWindows ? "eu07.exe" : "eu07";

    public string Resolve(SimulatorSettings settings, out ExeProblem problem)
    {
        if (!settings.SelectExeAutomatically && !string.IsNullOrWhiteSpace(settings.ExecutablePath))
        {
            problem = Validate(FullPath(settings.ExecutablePath));
            return settings.ExecutablePath;
        }

        string? fallback = null;
        try
        {
            var candidates = _files.GetFiles(_paths.Root, "eu07*")
                .Where(IsCandidate)
                .OrderBy(path => string.Equals(Path.GetFileName(path), CanonicalName,
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

            if (!_environment.IsWindows && !_files.IsExecutable(path))
                return ExeProblem.NotExecutable;

            return MatchesPlatform(path) ? ExeProblem.None : ExeProblem.WrongPlatform;
        }
        catch (Exception)
        {
            return ExeProblem.NotFound;
        }
    }

    private bool MatchesPlatform(string path)
    {
        byte[] head = new byte[4];
        using var stream = _files.OpenRead(path);
        if (stream.Read(head, 0, 4) != 4)
            return true;

        bool portableExecutable = head[0] == 0x4D && head[1] == 0x5A;
        bool elf = head[0] == 0x7F && head[1] == 0x45 && head[2] == 0x4C && head[3] == 0x46;

        if (_environment.IsWindows && elf)
            return false;
        if (_environment.IsLinux && portableExecutable)
            return false;

        return true;
    }

    private bool IsCandidate(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (NeverExecutable.Contains(extension))
            return false;

        if (_environment.IsWindows)
            return extension == ".exe";

        if (_environment.IsLinux)
            return extension.Length == 0 || extension is ".x86_64" or ".run" or ".appimage";

        return extension.Length == 0 || extension is ".exe" or ".x86_64" or ".run" or ".appimage";
    }

    private string FullPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(_paths.Root, path);
}
