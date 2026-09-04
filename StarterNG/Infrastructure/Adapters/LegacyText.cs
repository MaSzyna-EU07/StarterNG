using System;
using System.Text;

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
    /// "ZakĹ‚ad". Valid UTF-8 is taken at its word; anything that fails a strict
    /// decode is 1250, which cannot fail.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return CodePage1250.GetString(bytes);
        }
    }

    private static readonly Encoding Utf8Strict =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
