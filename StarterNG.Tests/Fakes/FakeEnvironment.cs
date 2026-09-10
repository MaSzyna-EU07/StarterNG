using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>A host machine described inline: platform, home folder, env vars.</summary>
public sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public bool IsWindows { get; set; }

    public bool IsLinux { get; set; } = true;

    public bool IsMacOS { get; set; }

    public string BaseDirectory { get; set; } = Path.Combine("game");

    public string? GetVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public FakeEnvironment With(string name, string value)
    {
        _variables[name] = value;
        return this;
    }

    /// <summary>Switches the fake to Windows, with an APPDATA folder.</summary>
    public FakeEnvironment AsWindows(string appData = "C:/Users/kolejarz/AppData/Roaming")
    {
        IsWindows = true;
        IsLinux = false;
        IsMacOS = false;
        return With("APPDATA", appData);
    }
}
