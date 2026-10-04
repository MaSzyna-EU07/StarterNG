using System;
using System.Text;
using System.Text.Unicode;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// The code page every file the simulator owns is written in: sceneries,
/// textures.txt, .fiz, timetables, eu07.ini.
/// </summary>
/// <remarks>
/// Code page 1250 is not built into .NET; it arrives with
/// <see cref="CodePagesEncodingProvider"/>, which has to be registered before the
/// first <c>GetEncoding</c> call. Doing that registration at each call site is how
/// Polish characters end up as Latin-1 mojibake ("Skład" as "Sk³ad"): whichever
/// site runs first wins, and the composition root builds its repositories before
/// any of them. One property, resolved once, removes the ordering question.
/// </remarks>
public static class LegacyText
{
    public static Encoding CodePage1250 { get; } = Resolve();

    public static bool IsFallback { get; private set; }

    /// <summary>
    /// Decodes a game file. Most are code page 1250, but a few have been re-saved
    /// as UTF-8 over the years, and reading those as 1250 turns "Zakład" into
    /// "ZakĹ‚ad". Valid UTF-8 is taken at its word; anything else is 1250, which
    /// cannot fail. Checked up front rather than by catching a failed strict decode:
    /// most files are 1250, and that was an exception per file on every load.
    /// A byte order mark is dropped, or it would stick to the first token.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> text = bytes;
        if (text.StartsWith(Utf8Bom))
            text = text[Utf8Bom.Length..];

        return Utf8.IsValid(text) ? Encoding.UTF8.GetString(text) : CodePage1250.GetString(text);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static Encoding Resolve()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250);
        }
        catch (Exception)
        {
            IsFallback = true;
            return Encoding.Latin1;
        }
    }
}
