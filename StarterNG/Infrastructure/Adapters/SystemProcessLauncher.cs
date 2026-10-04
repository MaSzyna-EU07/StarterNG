using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class SystemProcessLauncher : IProcessLauncher
{
    private readonly IDiagnosticsLog _log;

    public SystemProcessLauncher(IDiagnosticsLog log)
    {
        _log = log;
    }

    public IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                                 bool captureErrors, out string? error)
    {
        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardError = captureErrors
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            var process = new Process { StartInfo = info };
            var handle = new ProcessHandle(process, started: true);
            if (captureErrors)
                process.ErrorDataReceived += (_, e) => handle.AddError(e.Data);

            if (!process.Start())
            {
                error = executablePath;
                _log.Log($"start {executablePath}: process not created");
                return null;
            }

            // Drained as it comes: a full pipe would stall the simulator.
            if (captureErrors)
                process.BeginErrorReadLine();

            error = null;
            return handle;
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
            return process is null ? null : new ProcessHandle(process, started: false);
        }
        catch (Exception ex)
        {
            _log.Log($"lookup {processName}", ex);
            return null;
        }
    }

    private sealed class ProcessHandle : IProcessHandle
    {
        private const int KeptErrorLines = 20;

        private readonly Process _process;
        private readonly bool _started;
        private readonly Queue<string> _errors = new();

        public ProcessHandle(Process process, bool started)
        {
            _process = process;
            _started = started;
        }

        public bool HasExited
        {
            get
            {
                try { return _process.HasExited; }
                catch { return true; }
            }
        }

        // Only a process started here has an exit code to read; one merely found
        // running throws on it.
        public int? ExitCode
        {
            get
            {
                try { return _started && _process.HasExited ? _process.ExitCode : null; }
                catch { return null; }
            }
        }

        public IReadOnlyList<string> ErrorTail
        {
            get
            {
                lock (_errors)
                    return _errors.ToList();
            }
        }

        public void AddError(string? line)
        {
            if (line is null)
                return;

            lock (_errors)
            {
                _errors.Enqueue(line);
                if (_errors.Count > KeptErrorLines)
                    _errors.Dequeue();
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            _process.WaitForExitAsync(cancellationToken);
    }
}
