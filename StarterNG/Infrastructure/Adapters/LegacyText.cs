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
    /// <summary>The .scn / .fiz / .ini code page, or Latin-1 if it is unavailable.</summary>
    public static Encoding CodePage1250 { get; } = Resolve();

    /// <summary>
    /// True when the code page could not be loaded and text is being read as
    /// Latin-1 instead. Polish characters will be wrong; the startup check
    /// reports it rather than leaving it to be discovered on screen.
    /// </summary>
    public static bool IsFallback { get; private set; }

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
