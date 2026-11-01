using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class DisplaysTests
{
    /// <summary>A 128-byte block with an 18-byte timing at 54, as a panel's base EDID has it.</summary>
    private static byte[] BaseBlock(int pixelClock10KHz, int hActive, int hBlank, int vActive, int vBlank,
                                    int extensions = 0)
    {
        var edid = new byte[128];
        new byte[] { 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0 }.CopyTo(edid, 0);
        edid[126] = (byte)extensions;
        int at = 54;
        edid[at] = (byte)pixelClock10KHz;
        edid[at + 1] = (byte)(pixelClock10KHz >> 8);
        edid[at + 2] = (byte)hActive;
        edid[at + 3] = (byte)hBlank;
        edid[at + 4] = (byte)((hActive >> 8) << 4 | hBlank >> 8);
        edid[at + 5] = (byte)vActive;
        edid[at + 6] = (byte)vBlank;
        edid[at + 7] = (byte)((vActive >> 8) << 4 | vBlank >> 8);
        return edid;
    }

    /// <summary>A DisplayID 2.0 block with one type VII timing (kHz, sizes less one).</summary>
    private static byte[] DisplayIdBlock(int pixelClockKHz, int hActive, int hBlank, int vActive, int vBlank)
    {
        var block = new byte[128];
        block[0] = 0x70;
        block[1] = 0x20;
        block[2] = 23;   // one data block: 3 bytes of header, 20 of timing
        block[3] = 2;
        int at = 5;
        block[at] = 0x22;
        block[at + 2] = 20;
        int t = at + 3, clock = pixelClockKHz - 1;
        block[t] = (byte)clock;
        block[t + 1] = (byte)(clock >> 8);
        block[t + 2] = (byte)(clock >> 16);
        void Put(int offset, int value)
        {
            block[t + offset] = (byte)(value - 1);
            block[t + offset + 1] = (byte)((value - 1) >> 8);
        }
        Put(4, hActive);
        Put(6, hBlank);
        Put(12, vActive);
        Put(14, vBlank);
        return block;
    }

    [Fact]
    public void A_plain_monitor_gives_its_preferred_timing()
    {
        // 1920x1080 at 148.5 MHz: the usual 60 Hz.
        Assert.Equal(60, Edid.HighestRefreshRate(BaseBlock(14850, 1920, 280, 1080, 45)));
    }

    [Fact]
    public void A_fast_panel_found_only_in_displayid_is_read_there()
    {
        // As the eDP panel of a gaming laptop: 60 Hz in the base block, 165 Hz in DisplayID.
        var edid = BaseBlock(15394, 1920, 160, 1200, 35, extensions: 1)
            .Concat(DisplayIdBlock(423250, 1920, 160, 1200, 33)).ToArray();

        Assert.Equal(165, Edid.HighestRefreshRate(edid));
    }

    [Fact]
    public void Something_too_short_for_an_edid_has_no_rate()
    {
        Assert.Equal(0, Edid.HighestRefreshRate(new byte[] { 0, 0xFF }));
        Assert.Equal(0, Edid.HighestRefreshRate(new byte[128]));
    }

    [Fact]
    public void Only_connected_screens_are_listed_by_their_connector()
    {
        var files = new InMemoryFileSystem(caseSensitive: true)
            .WithFile("/sys/class/drm/card1-eDP-1/status", "connected\n")
            .WithFile("/sys/class/drm/card1-HDMI-A-1/status", "disconnected\n")
            .WithFile("/sys/class/drm/card1/device/uevent", "DRIVER=amdgpu\n");
        files.WriteAllBytes("/sys/class/drm/card1-eDP-1/edid", BaseBlock(14850, 1920, 280, 1080, 45));
        files.WriteAllBytes("/sys/class/drm/card1-HDMI-A-1/edid", BaseBlock(14850, 1920, 280, 1080, 45));

        var displays = new Displays(files, new FakeEnvironment()).List();

        Assert.Equal(new[] { new Display("eDP-1", 60) }, displays);
    }

    [Fact]
    public void Off_linux_without_windows_nothing_is_read()
    {
        var environment = new FakeEnvironment { IsLinux = false, IsMacOS = true };

        Assert.Empty(new Displays(new InMemoryFileSystem(), environment).List());
    }
}
