using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Application;

/// <summary>What a simulator that went down left behind: its log and its crash dumps.</summary>
public sealed class SimulatorAftermath
{
    private readonly IFileSystem _files;
    private readonly IGamePaths _paths;
    private readonly IDiagnosticsLog _log;

    public SimulatorAftermath(IFileSystem files, IGamePaths paths, IDiagnosticsLog log)
    {
        _files = files;
        _paths = paths;
        _log = log;
    }

    /// <summary>
    /// Minidumps written since the simulator was started, newest first. The
    /// simulator names them crash_*.dmp relative to its working directory - the
    /// installation - and crashpad, where it is set up, keeps its own under
    /// crashdumps; the folder of the binary is looked at too, for a simulator that
    /// was once run from there.
    /// </summary>
    public IReadOnlyList<string> CrashDumpsSince(DateTime startedUtc, string executable)
    {
        var dumps = new List<string>();
        try
        {
            foreach (string directory in new[] { _paths.Root, Path.GetDirectoryName(executable) ?? _paths.Root }
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(_files.DirectoryExists))
                dumps.AddRange(_files.GetFiles(directory, "*.dmp"));

            string crashpad = _paths.FromRoot("crashdumps");
            if (_files.DirectoryExists(crashpad))
                dumps.AddRange(_files.GetFilesRecursive(crashpad, "*.dmp"));

            // A little slack: the start is taken just after the launch, not before it.
            var since = startedUtc - TimeSpan.FromSeconds(5);
            return dumps.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(dump => _files.GetLastWriteTimeUtc(dump) >= since)
                        .OrderByDescending(_files.GetLastWriteTimeUtc)
                        .ToList();
        }
        catch (Exception ex)
        {
            _log.Log("looking for crash dumps", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>The end of the simulator's own log, for when it died without a word on its error stream.</summary>
    public IReadOnlyList<string> SimulatorLogTail(int count)
    {
        string path = _paths.FromRoot("log.txt");
        try
        {
            if (!_files.FileExists(path))
                return Array.Empty<string>();

            var lines = LegacyText.Decode(_files.ReadAllBytes(path))
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Trim().Length > 0)
                .ToList();
            return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
        }
        catch (Exception ex)
        {
            _log.Log($"reading {path}", ex);
            return Array.Empty<string>();
        }
    }
}
