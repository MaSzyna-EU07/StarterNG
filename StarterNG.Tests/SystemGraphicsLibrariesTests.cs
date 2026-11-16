using StarterNG.Infrastructure;

namespace StarterNG.Tests;

public class SystemGraphicsLibrariesTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string Game = @"E:\SteamLibrary\steamapps\common\MaSzyna";

    private static readonly IntPtr Dxgi = new(0x1000);
    private static readonly IntPtr D3d11 = new(0x2000);

    private static SystemGraphicsLibraries Take(Func<string, bool> beside, Dictionary<string, IntPtr> system)
    {
        return SystemGraphicsLibraries.Take(System32, Game, beside, Load);

        bool Load(string path, out IntPtr handle)
        {
            handle = system.GetValueOrDefault(Path.GetFileName(path));
            return handle != IntPtr.Zero;
        }
    }

    private static SystemGraphicsLibraries WithDxgiAndD3d11(Func<string, bool> beside) =>
        Take(beside, new Dictionary<string, IntPtr> { ["dxgi.dll"] = Dxgi, ["d3d11.dll"] = D3d11 });

    [Fact]
    public void An_import_by_name_gets_the_system_library_with_or_without_its_extension()
    {
        var taken = WithDxgiAndD3d11(_ => false);

        Assert.Equal(Dxgi, taken.Resolve("dxgi"));
        Assert.Equal(Dxgi, taken.Resolve("dxgi.dll"));
        Assert.Equal(D3d11, taken.Resolve("D3D11.DLL"));
    }

    [Fact]
    public void Other_libraries_and_ones_the_system_lacks_are_left_to_the_usual_search()
    {
        var taken = WithDxgiAndD3d11(_ => false);

        Assert.Equal(IntPtr.Zero, taken.Resolve("user32.dll"));
        Assert.Equal(IntPtr.Zero, taken.Resolve("opengl32"));
    }

    [Fact]
    public void They_are_loaded_from_the_system_folder_dxgi_first()
    {
        var asked = new List<string>();
        SystemGraphicsLibraries.Take(System32, Game, _ => false, Load);

        Assert.Equal(SystemGraphicsLibraries.Names.Select(n => Path.Combine(System32, n + ".dll")), asked);
        Assert.Equal("dxgi", SystemGraphicsLibraries.Names[0]);

        bool Load(string path, out IntPtr handle)
        {
            asked.Add(path);
            handle = IntPtr.Zero;
            return false;
        }
    }

    [Fact]
    public void A_wrapper_beside_the_starter_is_noted()
    {
        var taken = WithDxgiAndD3d11(path => path == Path.Combine(Game, "dxgi.dll"));

        Assert.Equal(new[] { "dxgi.dll" }, taken.Shadowed);
        Assert.Contains("dxgi.dll", taken.Note);
    }

    [Fact]
    public void With_nothing_beside_the_starter_there_is_nothing_to_note()
    {
        var taken = WithDxgiAndD3d11(_ => false);

        Assert.Empty(taken.Shadowed);
        Assert.Null(taken.Note);
    }

    [Fact]
    public void Only_windows_has_system_libraries_to_take()
    {
        var taken = SystemGraphicsLibraries.TakeOnWindows();

        Assert.Equal(OperatingSystem.IsWindows(), taken is not null);
    }

    [Fact]
    public void An_assembly_with_a_resolver_of_its_own_keeps_it()
    {
        var taken = WithDxgiAndD3d11(_ => false);
        var assembly = typeof(SystemGraphicsLibrariesTests).Assembly;

        Assert.True(taken.ServeTo(assembly));
        Assert.False(taken.ServeTo(assembly));
    }
}
