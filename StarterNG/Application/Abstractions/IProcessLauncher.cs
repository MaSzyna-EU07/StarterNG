using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarterNG.Application.Abstractions;

public interface IProcessHandle
{
    bool HasExited { get; }

    /// <summary>Once it has ended; null while running, or for a process the starter did not start.</summary>
    int? ExitCode { get; }

    /// <summary>The last lines of the error stream, when it was captured.</summary>
    IReadOnlyList<string> ErrorTail { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}

public interface IProcessLauncher
{
    /// <summary>
    /// Starts a process. With captureErrors its error stream is kept for the
    /// handle - only for a process the starter outlives: a write to a pipe nobody
    /// reads any more can kill the writer.
    /// </summary>
    IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                          bool captureErrors, out string? error);

    bool OpenInShell(string pathOrUrl);

    IProcessHandle? FindRunning(string processName);
}
