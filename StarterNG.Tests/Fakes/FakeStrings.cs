using StarterNG.Application.Abstractions;

namespace StarterNG.Tests.Fakes;

/// <summary>
/// Translation table that echoes keys back, so assertions can name the key that
/// a screen or check would have shown.
/// </summary>
public sealed class FakeStrings : ILocalizedStrings
{
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);

    public string this[string key] => _overrides.TryGetValue(key, out var value) ? value : key;

    public string Opt(string key) => _overrides.TryGetValue(key, out var value) ? value : string.Empty;

    public string CurrentLanguage => "English";

    public string CurrentLangCode => "en";

    public FakeStrings With(string key, string value)
    {
        _overrides[key] = value;
        return this;
    }
}
