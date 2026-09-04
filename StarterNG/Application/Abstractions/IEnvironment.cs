namespace StarterNG.Application.Abstractions;

/// <summary>
/// The bits of the host machine the starter has to ask about: which operating
/// system it is on, and where that system keeps per-user configuration.
/// </summary>
public interface IEnvironment
{
    bool IsWindows { get; }

    bool IsLinux { get; }

    bool IsMacOS { get; }

    /// <summary>An environment variable, or null when it is not set.</summary>
    string? GetVariable(string name);

    /// <summary>The folder the starter's own executable lives in.</summary>
    string BaseDirectory { get; }
}
