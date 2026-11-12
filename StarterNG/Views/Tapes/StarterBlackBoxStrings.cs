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
            return text.Length > 0 ? text : _own[key];
        }
    }
}
