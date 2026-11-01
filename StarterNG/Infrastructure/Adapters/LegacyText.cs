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
        var (encoding, bom) = Detect(bytes);
        return encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);
    }

    /// <summary>
    /// What a game file is written in, so it is written back the way it was read:
    /// UTF-8 only for a file that is UTF-8 and says so with its mark or a letter
    /// outside ASCII, code page 1250 - the game's own - else. That one gives every
    /// byte back as it was, also behind a mark that lied; UTF-8 would turn what is
    /// not UTF-8 into replacement characters.
    /// </summary>
    public static (Encoding Encoding, byte[] Bom) Detect(byte[] bytes)
    {
        byte[] bom = bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom : Array.Empty<byte>();
        var body = bytes.AsSpan(bom.Length);
        bool utf8 = Utf8.IsValid(body) && (bom.Length > 0 || body.ContainsAnyInRange((byte)0x80, (byte)0xFF));
        return (utf8 ? new UTF8Encoding(false) : CodePage1250, bom);
    }

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

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
