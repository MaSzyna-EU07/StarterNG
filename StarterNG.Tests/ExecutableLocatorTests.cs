using StarterNG.Domain.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class ExecutableLocatorTests
{
    private const string Elf = "\u007fELF binary";
    private const string PortableExecutable = "MZ binary";

    [Fact]
    public void The_canonical_binary_is_preferred_over_other_candidates()
    {
        var files = new InMemoryFileSystem()
            .WithExecutable(TestInstallation.At("eu07-dev"), Elf)
            .WithExecutable(TestInstallation.At("eu07"), Elf);
        var installation = new TestInstallation(files);

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.Equal(ExeProblem.None, problem);
        Assert.EndsWith("eu07", resolved);
    }

    [Fact]
    public void A_binary_for_the_other_platform_is_reported_as_such()
    {
        var files = new InMemoryFileSystem().WithExecutable(TestInstallation.At("eu07"), PortableExecutable);
        var installation = new TestInstallation(files);

        installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.Equal(ExeProblem.WrongPlatform, problem);
    }

    [Fact]
    public void A_binary_without_the_execute_bit_is_reported_as_such()
    {
        var files = new InMemoryFileSystem().WithFile(TestInstallation.At("eu07"), Elf);
        var installation = new TestInstallation(files);

        installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.Equal(ExeProblem.NotExecutable, problem);
    }

    [Fact]
    public void An_empty_installation_reports_the_canonical_name_as_missing()
    {
        var installation = new TestInstallation();

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.Equal(ExeProblem.NotFound, problem);
        Assert.Equal("eu07", resolved);
    }

    [Fact]
    public void A_manually_chosen_path_is_used_as_given()
    {
        var files = new InMemoryFileSystem().WithExecutable(TestInstallation.At("eu07-custom"), Elf);
        var installation = new TestInstallation(files);
        var settings = new SimulatorSettings { SelectExeAutomatically = false, ExecutablePath = "eu07-custom" };

        string resolved = installation.Executables.Resolve(settings, out var problem);

        Assert.Equal("eu07-custom", resolved);
        Assert.Equal(ExeProblem.None, problem);
    }

    [Fact]
    public void Data_files_beside_the_binary_are_never_offered_as_candidates()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("eu07.ini"), "width 1280")
            .WithFile(TestInstallation.At("eu07.log"), "log")
            .WithExecutable(TestInstallation.At("eu07"), Elf);
        var installation = new TestInstallation(files);

        Assert.Equal(new[] { "eu07" }, installation.Executables.ListCandidates());
    }
}
