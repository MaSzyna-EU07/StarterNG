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
