using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// A process the starter has launched or adopted. Exposes only what the starter
/// needs — whether the simulator is still up, and a way to await its exit — so
/// the presentation layer never handles <see cref="System.Diagnostics.Process"/>
/// directly.
/// </summary>
public interface IProcessHandle
{
    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Port over process creation: starting the simulator, opening a file, folder or
/// URL in the desktop shell, and adopting an already running simulator.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Starts an executable with the given arguments. Returns null when the
    /// process could not be created; <paramref name="error"/> then carries the
    /// reason for display.
    /// </summary>
    IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                          out string? error);

    /// <summary>
    /// Hands a path or URL to the desktop shell (explorer, xdg-open, ...).
    /// Returns false and logs when the shell refused it.
    /// </summary>
    bool OpenInShell(string pathOrUrl);

    /// <summary>The first running process with that name, or null.</summary>
    IProcessHandle? FindRunning(string processName);
}
