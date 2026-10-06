using System.Runtime.ExceptionServices;
using System.Text;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class LegacyDecodingTests
{
    private const string Polish = "Zakład_Świętokrzyski_w_Kielcach";

    [Fact]
    public void A_code_page_1250_file_decodes_to_its_letters()
    {
        Assert.Equal(Polish, LegacyText.Decode(LegacyText.CodePage1250.GetBytes(Polish)));
    }

    [Fact]
    public void A_file_re_saved_as_utf8_decodes_too()
    {
        // A handful of textures.txt in the wild are UTF-8; read as 1250 they turn
        // "Zakład" into "ZakĹ‚ad".
        Assert.Equal(Polish, LegacyText.Decode(Encoding.UTF8.GetBytes(Polish)));
    }

    [Fact]
    public void A_byte_order_mark_does_not_stick_to_the_text()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("$a\n" + Polish)).ToArray();

        Assert.Equal("$a\n" + Polish, LegacyText.Decode(bytes));
    }

    [Fact]
    public void Telling_the_two_apart_does_not_throw()
    {
        // Decoding used to try strict UTF-8 and catch the failure: a first-chance
        // exception for every 1250 file, hundreds of them on a load.
        int thread = Environment.CurrentManagedThreadId;
        int thrown = 0;
        EventHandler<FirstChanceExceptionEventArgs> count = (_, e) =>
        {
            if (e.Exception is DecoderFallbackException && Environment.CurrentManagedThreadId == thread)
                thrown++;
        };

        AppDomain.CurrentDomain.FirstChanceException += count;
        try
        {
            LegacyText.Decode(LegacyText.CodePage1250.GetBytes(Polish));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }

        Assert.Equal(0, thrown);
    }

    [Fact]
    public void Plain_ascii_reads_the_same_either_way()
    {
        Assert.Equal("PESA_Bydgoszcz", LegacyText.Decode(Encoding.ASCII.GetBytes("PESA_Bydgoszcz")));
    }

    [Fact]
    public void A_utf8_textures_file_reaches_the_catalogue_intact()
    {
        var files = new InMemoryFileSystem().WithFile(
            TestInstallation.At("dynamic", "pkp", "elf", "textures.txt"),
            "en96-001.mat=34we,EN96,EN96-001 // v2,EN96-001,PREG,Zakład_Świętokrzyski,29.09.2011");
        var installation = new TestInstallation(files);

        installation.Vehicles.Load(installation.Library.Vehicles);

        var meta = Assert.Single(installation.Library.Vehicles.Textures).Meta;
        Assert.Equal("Zakład_Świętokrzyski".Replace('_', ' '), meta!.Depot);
    }

    [Fact]
    public void A_code_page_1250_scenery_reaches_the_list_intact()
    {
        var files = new InMemoryFileSystem().WithLegacyFile(
            TestInstallation.At("scenery", "td.scn"), "//$n Strzebiń - Woźniki Śl.");

        var scenery = Assert.Single(new TestInstallation(files).Sceneries.LoadAll());

        Assert.Equal("Strzebiń - Woźniki Śl.", scenery.Name);
    }
}
