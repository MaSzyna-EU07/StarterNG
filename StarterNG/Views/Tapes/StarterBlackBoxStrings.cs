using System;
using System.Globalization;
using Eu07.BlackBox.Views;

namespace StarterNG.Views.Tapes;

/// <summary>
/// The texts of the black box window in the starter's language: a translation may give any of
/// them as BlackBox + its key; the rest are the window's own, in Polish or else in English.
/// </summary>
internal sealed class StarterBlackBoxStrings : IBlackBoxStrings
{
    private readonly DefaultBlackBoxStrings _own = new(App.Loc.CurrentLangCode);

    public string this[string key]
    {
        get
        {
            string text = App.Loc.Opt("BlackBox" + key);
            if (text.Length > 0)
                return text;
            // The commands in a language of the old starter's key descriptions, as the keys tab names them; in Polish
            // and English the window's own.
            if (key.StartsWith(DefaultBlackBoxStrings.CommandPrefix, StringComparison.Ordinal) && App.Loc.CurrentLangCode is not ("pl" or "en")
                && App.Loc.Opt(key) is { Length: > 0 } described)
                return char.ToUpper(described[0], CultureInfo.CurrentCulture) + described[1..];
            return _own[key];
        }
    }
}
