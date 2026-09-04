using System;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class SystemEnvironment : IEnvironment
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string BaseDirectory => AppContext.BaseDirectory;
}
