using StarterNG.Application;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Infrastructure.Settings;
using StarterNG.Infrastructure.Vehicles;

namespace StarterNG.Tests.Fakes;

/// <summary>
/// The production object graph wired over an in-memory installation: the same
/// composition as <c>AppServices</c>, with disk, clock and randomness replaced.
/// </summary>
public sealed class TestInstallation
{
    public const string Root = "game";

    public TestInstallation(InMemoryFileSystem? files = null, DateTime? now = null)
    {
        Files = files ?? new InMemoryFileSystem();
        Paths = new GamePaths(Root);
        Clock = new FixedClock(now ?? new DateTime(2026, 1, 1, 12, 0, 0));
        Random = new StubRandom();
        Log = new FileDiagnosticsLog(Files, Clock, Paths);
        Strings = new FakeStrings();

        MiniTextures = new MiniTextureIndex(Files, Paths);
        Physics = new FizPhysicsRepository(Files, Paths, Log);
        Vehicles = new TexturesTxtVehicleRepository(Files, Paths, Log, new TexturesTxtParser());
        Sceneries = new SceneryRepository(Files, Paths, Log, new SceneryParser(Clock, Random));
        MissingAssets = new MissingAssetScanner(Files, Paths);
        Library = new GameLibrary(Vehicles, Sceneries, MiniTextures, Physics, Log);

        Environment = new FakeEnvironment();
        SettingsPaths = new SettingsPaths(Environment, Paths);
        Executables = new ExecutableLocator(Files, Paths, Environment, Log);
        SettingsStore = new SettingsStore(Files, Clock, Log, SettingsPaths, new SettingsSerializer(), Executables);
    }

    public InMemoryFileSystem Files { get; }
    public GamePaths Paths { get; }
    public FixedClock Clock { get; }
    public StubRandom Random { get; }
    public FileDiagnosticsLog Log { get; }
    public FakeStrings Strings { get; }
    public MiniTextureIndex MiniTextures { get; }
    public FizPhysicsRepository Physics { get; }
    public TexturesTxtVehicleRepository Vehicles { get; }
    public SceneryRepository Sceneries { get; }
    public MissingAssetScanner MissingAssets { get; }
    public GameLibrary Library { get; }
    public FakeEnvironment Environment { get; }
    public SettingsPaths SettingsPaths { get; }
    public ExecutableLocator Executables { get; }
    public SettingsStore SettingsStore { get; }

    /// <summary>Path of a file inside the fake installation.</summary>
    public static string At(params string[] segments) => Path.Combine(new[] { Root }.Concat(segments).ToArray());
}
