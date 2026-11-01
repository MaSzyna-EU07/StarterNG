using StarterNG.Domain.Settings;
using StarterNG.Infrastructure;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class GraphicsCardsTests
{
    private const long GiB = 1L << 30;

    private const string PciIds =
        "# List of PCI ID's\n" +
        "\n" +
        "10de  NVIDIA Corporation\n" +
        "\t1000  GK110 [Nothing Here]\n" +
        "\t2d19  GB206M [GeForce RTX 5060 Max-Q / Mobile]\n" +
        "\t\t1043 1e0c  A subsystem\n" +
        "1002  Advanced Micro Devices, Inc. [AMD/ATI]\n" +
        "\t1900  Phoenix1\n" +
        "\t2d19  Not this vendor's card\n" +
        "8086  Intel Corporation\n" +
        "\t46a6  Alder Lake-P GT2 [Iris Xe Graphics]\n" +
        "C 00  Unclassified device\n" +
        "\t2d19  Not a card at all\n";

    private static GraphicsCards Reader(InMemoryFileSystem files, FakeEnvironment environment,
                                        params string[] nvidiaSmi) =>
        new(files, environment, new LinuxGpus(files, environment), () => nvidiaSmi);

    private static InMemoryFileSystem Card(InMemoryFileSystem files, string card, string driver, string id, string slot,
                                           long memory = 0)
    {
        string device = $"/sys/class/drm/{card}/device";
        files.WithDirectory($"/sys/class/drm/{card}")
             .WithFile($"{device}/uevent", $"DRIVER={driver}\nPCI_ID={id}\nPCI_SLOT_NAME={slot}\n");
        if (memory > 0)
            files.WithFile($"{device}/mem_info_vram_total", memory + "\n");
        return files;
    }

    [Theory]
    [InlineData("10DE:2D19", "GeForce RTX 5060 Max-Q / Mobile")]
    [InlineData("1002:1900", "Phoenix1")]
    [InlineData("8086:46A6", "Iris Xe Graphics")]
    [InlineData("10DE:FFFF", null)]
    [InlineData("1234:2D19", null)]
    [InlineData("nonsense", null)]
    public void The_pci_list_names_a_card_by_its_vendor_and_device(string id, string? expected)
    {
        Assert.Equal(expected, GraphicsCards.ModelInPciList(PciIds.Split('\n'), id));
    }

    [Fact]
    public void A_linux_card_takes_its_name_from_the_pci_list_and_its_memory_from_the_driver()
    {
        var files = Card(new InMemoryFileSystem(), "card0", "amdgpu", "1002:73DF", "0000:03:00.0", 12 * GiB)
            .WithFile("/usr/share/hwdata/pci.ids", "1002  AMD\n\t73df  Navi 22 [Radeon RX 6700 XT]\n");

        var card = Assert.Single(Reader(files, new FakeEnvironment()).List());

        Assert.Equal("AMD Radeon RX 6700 XT", card.Name);
        Assert.Equal(12, card.MemoryGb);
    }

    [Fact]
    public void The_proprietary_driver_names_its_card_and_nvidia_smi_tells_its_memory()
    {
        var files = Card(new InMemoryFileSystem(), "card0", "nvidia", "10DE:2D19", "0000:01:00.0")
            .WithFile("/proc/driver/nvidia/gpus/0000:01:00.0/information",
                      "Model: \t\t NVIDIA GeForce RTX 5060 Laptop GPU\nIRQ:   \t\t 182\n");

        var card = Assert.Single(Reader(files, new FakeEnvironment(), "00000000:01:00.0, 8188").List());

        Assert.Equal("NVIDIA GeForce RTX 5060 Laptop GPU", card.Name);
        Assert.Equal(8, card.MemoryGb);
    }

    [Fact]
    public void A_card_keeps_its_name_when_nothing_tells_its_memory()
    {
        var files = Card(new InMemoryFileSystem(), "card0", "nouveau", "10DE:2D19", "0000:01:00.0");

        var card = Assert.Single(Reader(files, new FakeEnvironment()).List());

        Assert.Equal("NVIDIA 10DE:2D19", card.Name);
        Assert.Equal(0, card.MemoryBytes);
    }

    [Fact]
    public void The_discrete_card_comes_first_whatever_order_the_system_lists_them()
    {
        var files = new InMemoryFileSystem().WithFile(
            "/usr/share/misc/pci.ids", "1002  AMD\n\t1900  Rembrandt [Radeon 780M]\n\t73df  Navi 22 [Radeon RX 6700 XT]\n");
        Card(files, "card0", "amdgpu", "1002:1900", "0000:01:00.0", GiB / 2);
        Card(files, "card1", "amdgpu", "1002:73DF", "0000:65:00.0", 12 * GiB);

        var cards = Reader(files, new FakeEnvironment()).List();

        Assert.Equal(new[] { "AMD Radeon RX 6700 XT", "AMD Radeon 780M" }, cards.Select(card => card.Name));
    }

    [Fact]
    public void There_are_no_cards_on_a_mac()
    {
        var files = Card(new InMemoryFileSystem(), "card0", "amdgpu", "1002:73DF", "0000:03:00.0", 12 * GiB);

        Assert.Empty(Reader(files, new FakeEnvironment { IsLinux = false, IsMacOS = true }).List());
    }

    [Theory]
    [InlineData("00000000:01:00.0, 8188", "0000:01:00.0", 8188L << 20)]
    [InlineData("0000:65:00.0, 16384", "0000:65:00.0", 16384L << 20)]
    public void Nvidia_smi_is_read_by_the_address_sys_gives(string line, string slot, long bytes)
    {
        Assert.Equal(bytes, GraphicsCards.ParseNvidiaMemory(new[] { line })[slot]);
    }

    [Theory]
    [InlineData("Failed to initialize NVML")]
    [InlineData("00000000:01:00.0, [N/A]")]
    public void Nvidia_smi_saying_something_else_tells_nothing(string line)
    {
        Assert.Empty(GraphicsCards.ParseNvidiaMemory(new[] { line }));
    }

    [Theory]
    [InlineData("AMD Radeon 780M Graphics", true)]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("AMD Radeon(TM) Vega 8 Graphics", true)]
    [InlineData("AMD Radeon RX Vega 10 Graphics", true)]
    [InlineData("AMD Radeon Vega Series / Radeon Vega Mobile Series", true)]
    [InlineData("Intel(R) UHD Graphics 770", true)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", true)]
    [InlineData("Intel(R) Arc(TM) Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("Intel Arc B580", false)]
    [InlineData("AMD Radeon RX 6800M", false)]
    [InlineData("AMD Radeon RX Vega 56", false)]
    [InlineData("NVIDIA GeForce RTX 5060 Laptop GPU", false)]
    public void A_card_in_a_processor_is_told_by_its_name(string name, bool shared)
    {
        Assert.Equal(shared, new GraphicsCard(name, 8 * GiB).SharedMemory);
    }

    [Fact]
    public void A_card_reporting_under_a_gigabyte_counts_as_one_in_a_processor()
    {
        Assert.True(new GraphicsCard("AMD Phoenix1", GiB / 2).SharedMemory);
    }

    [Fact]
    public void The_reported_memory_is_rounded_the_way_a_box_says_it()
    {
        Assert.Equal(8, new GraphicsCard("NVIDIA GeForce RTX 5060 Laptop GPU", 8546942976).MemoryGb);
    }
}
