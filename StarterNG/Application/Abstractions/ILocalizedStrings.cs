namespace StarterNG.Application.Abstractions;

/// <summary>
/// Port over the translation table. Anything that needs a user-facing string
/// takes this, rather than reaching for the concrete localization service or a
/// static on <c>App</c>.
/// </summary>
public interface ILocalizedStrings
{
    /// <summary>
    /// Translation for a key. Missing keys return the key itself (and are logged
    /// once), so an untranslated screen stays usable.
    /// </summary>
    string this[string key] { get; }

    /// <summary>Lookup for a key that may legitimately be absent; "" when missing.</summary>
    string Opt(string key);

    string CurrentLanguage { get; }

    string CurrentLangCode { get; }
}
