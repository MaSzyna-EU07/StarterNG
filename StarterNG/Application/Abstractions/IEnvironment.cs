namespace StarterNG.Application.Abstractions;

public interface IEnvironment
{
    bool IsWindows { get; }

    bool IsLinux { get; }

    bool IsMacOS { get; }

    string? GetVariable(string name);

    string BaseDirectory { get; }
}
