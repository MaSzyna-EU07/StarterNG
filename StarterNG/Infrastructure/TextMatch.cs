using System;
using System.Globalization;
using System.Text;

namespace StarterNG.Infrastructure;

/// <summary>
/// Case- and diacritic-insensitive substring matching, so that typing
/// "rozdzielczosc" in a search box still finds "Rozdzielczość".
/// </summary>
public static class TextMatch
{
    public static bool Contains(string? haystack, string? needle)
    {
        if (string.IsNullOrEmpty(needle))
            return true;
        if (string.IsNullOrEmpty(haystack))
            return false;

        return Fold(haystack).Contains(Fold(needle), StringComparison.Ordinal);
    }

    /// <summary>
    /// Lower-cases and strips combining marks, so "Ż" and "z" compare equal.
    /// Polish "ł" has no decomposed form, so it is mapped by hand.
    /// </summary>
    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c == 'ł' ? 'l' : c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
