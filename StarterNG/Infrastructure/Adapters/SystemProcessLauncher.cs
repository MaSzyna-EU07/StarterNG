using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// <see cref="IProcessLauncher"/> backed by <see cref="Process"/>. The only place
/// in the application that starts an operating-system process.
/// </summary>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    private readonly IDiagnosticsLog _log;

    public SystemProcessLauncher(IDiagnosticsLog log)
    {
        _log = log;
    }

    public IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                                 out string? error)
    {
        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(info);
            if (process is null)
            {
                error = executablePath;
                _log.Log($"start {executablePath}: process not created");
                return null;
            }

            error = null;
            return new ProcessHandle(process);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _log.Log($"start {executablePath}", ex);
            return null;
        }
    }

    public bool OpenInShell(string pathOrUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _log.Log($"open {pathOrUrl}", ex);
            return false;
        }
    }

    public IProcessHandle? FindRunning(string processName)
    {
        try
        {
            var process = Process.GetProcessesByName(processName).FirstOrDefault();
            return process is null ? null : new ProcessHandle(process);
        }
        catch (Exception ex)
        {
            _log.Log($"lookup {processName}", ex);
            return null;
        }
    }

    private sealed class ProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process)
        {
            _process = process;
        }

        public bool HasExited
        {
            get
            {
                try { return _process.HasExited; }
                catch { return true; }
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            _process.WaitForExitAsync(cancellationToken);
    }
}
