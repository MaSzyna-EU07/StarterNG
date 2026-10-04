using System;
using System.Reflection;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Infrastructure.Settings;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Services;

namespace StarterNG.Application;

// Skladany recznie: PublishAot i PublishTrimmed wykluczaja kontener oparty na
// refleksji. Current jest service locatorem i wolno go czytac tylko z entry
// pointu i z konstruktorow widokow, ktorych Avalonia nie potrafi parametryzowac.
public sealed class AppServices
{
    private static AppServices? _current;

    private AppServices(IGamePaths paths, IFileSystem files, IClock clock, IEnvironment environment)
    {
        Paths = paths;
        Files = files;
        Clock = clock;
        Environment = environment;

        var log = new FileDiagnosticsLog(files, clock, paths);
        Log = log;
        Random = new SystemRandomSource();
        Processes = new SystemProcessLauncher(log);
        Localization = new LocalizationService(log);
        Sceneries = new SceneryRepository(files, paths, log, new SceneryParser(clock, Random));
        SceneryImages = new SceneryImageLocator(files);
        MiniTextures = new MiniTextureIndex(files, paths);
        var settingsPaths = new SettingsPaths(environment, paths);
        SettingsPaths = settingsPaths;

        // textures.txt stays the truth; the starter reads its own JSON copy of it
        // and rebuilds that copy whenever the installation has moved on.
        TexturesTxt = new TexturesTxtVehicleRepository(files, paths, log, new TexturesTxtParser());
        VehicleJson = new VehicleJsonSerializer();
        VehicleCache = new VehicleCacheRepository(files, paths, log, TexturesTxt, VehicleJson,
                                                  settingsPaths.VehicleCacheDirectory());
        Vehicles = VehicleCache;
        Physics = new FizPhysicsRepository(files, paths, log);
        MissingAssets = new MissingAssetScanner(files, paths);
        SceneryTexts = new SceneryTranslations(files, log);
        Timetables = new TimetableLocator(files);
        LoadWeights = new LoadWeightsRepository(files, paths, log);
        Library = new GameLibrary(Vehicles, Sceneries, MiniTextures, Physics, log);
        State = new AppState();

        Executables = new ExecutableLocator(files, paths, environment, log);
        SettingsStore = new SettingsStore(files, clock, log, settingsPaths, new SettingsSerializer(), Executables);
        MissingVehicleLog = new MissingVehicleLog(SettingsStore.Settings, MissingAssets, Library, log);
        var assembly = typeof(AppServices).Assembly;
        var version = assembly.GetName().Version ?? new Version(0, 0);
        string? commit = UpdateCheck.CommitOf(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        Updates = new UpdateCheck(SettingsStore.Settings, new GitHubReleaseFeed(log, version), version, commit,
                                  files, clock, log, settingsPaths.UpdateCheckPath());
        StartSimulation = new StartSimulation(State, SettingsStore, Library.Vehicles, Executables, files, paths,
                                              Processes, Random, log);
    }

    public static AppServices Current =>
        _current ?? throw new InvalidOperationException(
            "AppServices.Current read before Initialize(); the composition root must be built first.");

    public static AppServices Initialize(IGamePaths? paths = null, IFileSystem? files = null, IClock? clock = null,
                                         IEnvironment? environment = null)
    {
        _current = new AppServices(
            paths ?? GamePaths.ForCurrentDirectory(),
            files ?? new PhysicalFileSystem(),
            clock ?? new SystemClock(),
            environment ?? new SystemEnvironment());
        return _current;
    }

    public IGamePaths Paths { get; }

    public IFileSystem Files { get; }

    public IClock Clock { get; }

    public IEnvironment Environment { get; }

    public IDiagnosticsLog Log { get; }

    public IProcessLauncher Processes { get; }

    public IRandomSource Random { get; }

    public ISceneryRepository Sceneries { get; }

    public SceneryImageLocator SceneryImages { get; }

    public IMiniTextureIndex MiniTextures { get; }

    public IVehicleRepository Vehicles { get; }

    /// <summary>The same object as <see cref="Vehicles"/>, typed for the developer tab.</summary>
    public VehicleCacheRepository VehicleCache { get; }

    /// <summary>The legacy reader behind the cache, for tools that go to the source.</summary>
    public TexturesTxtVehicleRepository TexturesTxt { get; }

    public VehicleJsonSerializer VehicleJson { get; }

    public IPhysicsRepository Physics { get; }

    public MissingAssetScanner MissingAssets { get; }

    public ISceneryTranslations SceneryTexts { get; }

    public TimetableLocator Timetables { get; }

    public LoadWeightsRepository LoadWeights { get; }

    public GameLibrary Library { get; }

    public AppState State { get; }

    public SettingsStore SettingsStore { get; }

    public SimulatorSettings Settings => SettingsStore.Settings;

    public ExecutableLocator Executables { get; }

    public SettingsPaths SettingsPaths { get; }

    public MissingVehicleLog MissingVehicleLog { get; }

    public UpdateCheck Updates { get; }

    public StartSimulation StartSimulation { get; }

    public LocalizationService Localization { get; }
}
