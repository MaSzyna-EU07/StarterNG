using System;
using StarterNG.Application;

namespace StarterNG.Controls;

/// <summary>
/// The starter's texts as the controls here read them: in its language once the
/// application is built, the key itself before that, as in a test.
/// </summary>
internal static class AppLanguage
{
    public static string Text(string key) => AppServices.CurrentOrNull?.Localization[key] ?? key;

    /// <summary>Raised when the language is changed; nothing is raised before the application is built.</summary>
    public static event Action? Changed
    {
        add
        {
            if (AppServices.CurrentOrNull?.Localization is { } loc)
                loc.LanguageChanged += value;
        }
        remove
        {
            if (AppServices.CurrentOrNull?.Localization is { } loc)
                loc.LanguageChanged -= value;
        }
    }
}
