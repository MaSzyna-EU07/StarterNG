using System;
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
        Vehicles = new TexturesTxtVehicleRepository(files, paths, log, new TexturesTxtParser());
        Physics = new FizPhysicsRepository(files, paths, log);
        MissingAssets = new MissingAssetScanner(files, paths);
        SceneryTexts = new SceneryTranslations(files, log);
        Timetables = new TimetableLocator(files);
        LoadWeights = new LoadWeightsRepository(files, paths, log);
        Library = new GameLibrary(Vehicles, Sceneries, MiniTextures, Physics, log);
        State = new AppState();

        var settingsPaths = new SettingsPaths(environment, paths);
        Executables = new ExecutableLocator(files, paths, environment, log);
        SettingsStore = new SettingsStore(files, clock, log, settingsPaths, new SettingsSerializer(), Executables);
        SettingsPaths = settingsPaths;
        MissingVehicleLog = new MissingVehicleLog(SettingsStore.Settings, MissingAssets, Library, log);
        StartSimulation = new StartSimulation(State, SettingsStore, files, Processes, Random, log);
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

    public StartSimulation StartSimulation { get; }

    public LocalizationService Localization { get; }
}
