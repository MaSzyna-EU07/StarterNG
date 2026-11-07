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
    public void A_pick_from_another_installation_gives_way_to_the_search()
    {
        var files = new InMemoryFileSystem().WithExecutable(TestInstallation.At("eu07"), Elf);
        var installation = new TestInstallation(files);
        var settings = new SimulatorSettings { SelectExeAutomatically = false, ExecutablePath = "eu07-elsewhere" };

        string resolved = installation.Executables.Resolve(settings, out var problem);

        Assert.EndsWith("eu07", resolved);
        Assert.Equal(ExeProblem.None, problem);
    }

    [Fact]
    public void A_missing_pick_is_reported_when_the_installation_has_none_either()
    {
        var installation = new TestInstallation();
        var settings = new SimulatorSettings { SelectExeAutomatically = false, ExecutablePath = "eu07-elsewhere" };

        string resolved = installation.Executables.Resolve(settings, out var problem);

        Assert.Equal("eu07-elsewhere", resolved);
        Assert.Equal(ExeProblem.NotFound, problem);
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

    private static readonly string Wine = Path.Combine("/usr/bin", "wine");

    private static TestInstallation WithWine(InMemoryFileSystem files)
    {
        var installation = new TestInstallation(files.WithExecutable(Wine));
        installation.Environment.With("PATH", "/usr/bin");
        return installation;
    }

    [Fact]
    public void Without_wine_a_windows_build_on_linux_is_not_a_candidate()
    {
        var installation = new TestInstallation(
            new InMemoryFileSystem().WithFile(TestInstallation.At("eu07.exe"), PortableExecutable));

        installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.Equal(ExeProblem.NotFound, problem);
        Assert.Null(installation.Executables.Wine());
    }

    [Fact]
    public void With_wine_a_windows_build_on_linux_is_found_even_without_the_execute_bit()
    {
        var installation = WithWine(
            new InMemoryFileSystem().WithFile(TestInstallation.At("eu07.exe"), PortableExecutable));

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.EndsWith("eu07.exe", resolved);
        Assert.Equal(ExeProblem.WrongPlatform, problem);
        Assert.Equal(new[] { "eu07.exe" }, installation.Executables.ListCandidates());
    }

    [Fact]
    public void A_native_build_wins_over_the_windows_one()
    {
        var installation = WithWine(new InMemoryFileSystem()
            .WithFile(TestInstallation.At("eu07.exe"), PortableExecutable)
            .WithExecutable(TestInstallation.At("eu07-dev"), Elf));

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.EndsWith("eu07-dev", resolved);
        Assert.Equal(ExeProblem.None, problem);
    }

    [Fact]
    public void Wine_starts_the_windows_build_and_a_native_one_starts_itself()
    {
        string exe = TestInstallation.At("eu07.exe");
        string native = TestInstallation.At("eu07");
        var installation = WithWine(new InMemoryFileSystem()
            .WithFile(exe, PortableExecutable)
            .WithExecutable(native, Elf));
        string[] arguments = { "-s", "$td.scn" };

        var (wine, wineArguments) = installation.Executables.LaunchCommand(exe, arguments);
        var (program, programArguments) = installation.Executables.LaunchCommand(native, arguments);

        Assert.Equal(Wine, wine);
        Assert.Equal(new[] { exe, "-s", "$td.scn" }, wineArguments);
        Assert.Equal(native, program);
        Assert.Equal(arguments, programArguments);
    }

    [Fact]
    public void What_is_inside_says_what_the_file_is_not_its_name()
    {
        string windowsBuild = TestInstallation.At("eu07-win");
        string nativeBuild = TestInstallation.At("eu07.exe");
        var installation = WithWine(new InMemoryFileSystem()
            .WithFile(windowsBuild, PortableExecutable)
            .WithExecutable(nativeBuild, Elf));
        var executables = installation.Executables;

        Assert.True(executables.IsWindowsBuild("eu07-win"));
        Assert.Equal(Wine, executables.LaunchCommand(windowsBuild, Array.Empty<string>()).FileName);
        Assert.False(executables.IsWindowsBuild("eu07.exe"));
        Assert.Equal(nativeBuild, executables.LaunchCommand(nativeBuild, Array.Empty<string>()).FileName);
    }

    [Fact]
    public void A_script_on_linux_starts_itself()
    {
        string script = TestInstallation.At("eu07-start");
        var installation = WithWine(new InMemoryFileSystem().WithExecutable(script, "#!/bin/sh\nwine eu07.exe\n"));

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.EndsWith("eu07-start", resolved);
        Assert.Equal(ExeProblem.None, problem);
        Assert.Equal(script, installation.Executables.LaunchCommand(script, Array.Empty<string>()).FileName);
    }

    [Theory]
    [InlineData("notatki do eu07\n")]
    [InlineData("")]
    public void On_linux_the_execute_bit_alone_does_not_make_a_program(string inside)
    {
        // On a Windows partition every file has the bit.
        string note = TestInstallation.At("eu07_opis");
        var installation = WithWine(new InMemoryFileSystem()
            .WithExecutable(note, inside)
            .WithFile(TestInstallation.At("eu07.exe"), PortableExecutable));

        string resolved = installation.Executables.Resolve(new SimulatorSettings(), out var problem);

        Assert.EndsWith("eu07.exe", resolved);
        Assert.Equal(ExeProblem.WrongPlatform, problem);
        Assert.Equal(new[] { "eu07.exe" }, installation.Executables.ListCandidates());
        Assert.Equal(ExeProblem.NotAProgram, installation.Executables.Validate(note));
    }

    [Fact]
    public void A_mac_binary_is_not_taken_for_a_note()
    {
        string build = TestInstallation.At("eu07");
        var installation = new TestInstallation(new InMemoryFileSystem().WithExecutable(build, "Ïúíþ"));
        installation.Environment.IsLinux = false;
        installation.Environment.IsMacOS = true;

        Assert.Equal(ExeProblem.None, installation.Executables.Validate(build));
    }

    [Fact]
    public void Wine_is_never_looked_for_on_windows()
    {
        var installation = WithWine(new InMemoryFileSystem());
        installation.Environment.AsWindows();

        Assert.Null(installation.Executables.Wine());
    }
}
