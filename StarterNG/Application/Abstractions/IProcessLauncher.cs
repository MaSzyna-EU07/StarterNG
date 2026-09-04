using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarterNG.Application.Abstractions;

public interface IProcessHandle
{
    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}

public interface IProcessLauncher
{
    IProcessHandle? Start(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
                          out string? error);

    bool OpenInShell(string pathOrUrl);

    IProcessHandle? FindRunning(string processName);
}
