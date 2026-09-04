using StarterNG.Domain.Sceneries;

namespace StarterNG.Application.Abstractions;

/// <summary>
/// The translations a scenery ships alongside itself, in scenery/i18n/*.json.
/// </summary>
/// <remarks>
/// Scenery text may be an "@key" reference rather than literal prose, resolved
/// against the currently loaded scenery's table. The table is per scenery, so
/// <see cref="LoadFor"/> replaces it rather than adding to it.
/// </remarks>
public interface ISceneryTranslations
{
    /// <summary>Loads the tables for one scenery, English first then the user's language.</summary>
    void LoadFor(Scenery scenery, string langCode);

    /// <summary>
    /// Resolves an "@key" reference against the loaded table. Text that is not a
    /// reference, or a key with no translation, comes back unchanged.
    /// </summary>
    string Translate(string? text);
}
