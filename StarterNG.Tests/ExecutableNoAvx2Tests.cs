using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class ExecutableNoAvx2Tests
{
    private const string Elf = "\u007fELF binary";
    private static readonly DateTime Older = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

    private static InMemoryFileSystem With(params string[] names)
    {
        var files = new InMemoryFileSystem();
        foreach (string name in names)
            files.WithExecutable(TestInstallation.At(name), Elf);
        return files;
    }

    private static string Automatic(InMemoryFileSystem files, bool lacksAvx2) =>
        Path.GetFileName(new TestInstallation(files, lacksAvx2: lacksAvx2).Executables
                             .Resolve(new SimulatorSettings(), out _));

    [Theory]
    [InlineData("eu07_noavx2", true)]
    [InlineData("eu07-x64_2026-11-15_abc1234_noavx2.exe", true)]
    [InlineData("EU07_NOAVX2.EXE", true)]
    [InlineData("eu07", false)]
    [InlineData("eu07-x64_2026-11-15_abc1234.exe", false)]
    [InlineData("eu07_noavx2_old.exe", false)]
    public void A_build_without_avx2_is_known_by_the_end_of_its_name(string name, bool noAvx2) =>
        Assert.Equal(noAvx2, ExecutableLocator.IsNoAvx2(name));

    [Fact]
    public void Without_avx2_the_build_without_it_is_chosen_over_the_canonical_one() =>
        Assert.Equal("eu07_noavx2", Automatic(With("eu07", "eu07_noavx2"), lacksAvx2: true));

    [Fact]
    public void With_avx2_the_build_made_for_it_stays_first() =>
        Assert.Equal("eu07", Automatic(With("eu07", "eu07_noavx2"), lacksAvx2: false));

    [Fact]
    public void With_avx2_a_build_without_it_is_used_when_it_is_the_only_one() =>
        Assert.Equal("eu07_noavx2", Automatic(With("eu07_noavx2"), lacksAvx2: false));

    [Fact]
    public void Without_avx2_the_newest_build_without_it_is_chosen()
    {
        var files = With("eu07_2026-11-01_aaa_noavx2", "eu07_2026-11-15_bbb_noavx2")
            .WrittenAt(TestInstallation.At("eu07_2026-11-01_aaa_noavx2"), Older)
            .WrittenAt(TestInstallation.At("eu07_2026-11-15_bbb_noavx2"), Newer);

        Assert.Equal("eu07_2026-11-15_bbb_noavx2", Automatic(files, lacksAvx2: true));
    }

    [Fact]
    public void Without_avx2_and_no_build_without_it_the_usual_choice_stands() =>
        Assert.Equal("eu07", Automatic(With("eu07-dev", "eu07"), lacksAvx2: true));

    [Fact]
    public void Only_a_build_with_avx2_on_a_processor_without_it_will_not_run()
    {
        var lacking = new TestInstallation(lacksAvx2: true).Executables;
        var able = new TestInstallation(lacksAvx2: false).Executables;

        Assert.True(lacking.WillNotRunHere("eu07.exe"));
        Assert.False(lacking.WillNotRunHere("eu07-x64_noavx2.exe"));
        Assert.False(able.WillNotRunHere("eu07.exe"));
    }

    [Fact]
    public void The_newest_build_without_avx2_is_offered_by_name()
    {
        var files = With("eu07", "eu07_2026-11-01_aaa_noavx2", "eu07_2026-11-15_bbb_noavx2")
            .WrittenAt(TestInstallation.At("eu07_2026-11-01_aaa_noavx2"), Older)
            .WrittenAt(TestInstallation.At("eu07_2026-11-15_bbb_noavx2"), Newer);

        Assert.Equal("eu07_2026-11-15_bbb_noavx2", new TestInstallation(files).Executables.NewestNoAvx2());
        Assert.Null(new TestInstallation(With("eu07")).Executables.NewestNoAvx2());
    }
}
