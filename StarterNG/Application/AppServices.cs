using System;
using System.IO;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure;
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
        var random = new SystemRandomSource();
        Processes = new SystemProcessLauncher(log);
        var settingsPaths = new SettingsPaths(environment, paths);
        SettingsPaths = settingsPaths;
        // The player's own first: the game folder may need rights to write to, the user folder never.
        Localization = new LocalizationService(log, folders: new LanguageFolders(
            new[]
            {
                Path.Combine(settingsPaths.UserConfigDirectory(), "starter", "lang"),
                paths.FromRoot("starter", "lang"),
                LocalizationService.LangDirectory,
                paths.FromRoot("startercfg", "lang")
            },
            paths.FromRoot("starter"),
            paths.FromRoot("lang")));
        Sceneries = new SceneryRepository(files, paths, log, new SceneryParser(clock, random));
        SceneryImages = new SceneryImageLocator(files);
        MiniTextures = new MiniTextureIndex(files, paths);

        // textures.txt stays the truth; the starter reads its own JSON copy of it
        // and rebuilds that copy whenever the installation has moved on.
        var texturesTxt = new TexturesTxtVehicleRepository(files, paths, log, new TexturesTxtParser());
        var vehicleJson = new VehicleJsonSerializer();
        Vehicles = new VehicleCacheRepository(files, paths, log, texturesTxt, vehicleJson,
                                              settingsPaths.VehicleCacheDirectory());
        Physics = new FizPhysicsRepository(files, paths, log);
        var missingAssets = new MissingAssetScanner(files, paths);
        SceneryTexts = new SceneryTranslations(files, log);
        Timetables = new TimetableLocator(files);
        LoadWeights = new LoadWeightsRepository(files, paths, log);
        Library = new GameLibrary(Vehicles, Sceneries, MiniTextures, Physics, log);
        State = new AppState();
        SceneryFiles = new SceneryFileWatch(files, Sceneries, Library, State);

        Executables = new ExecutableLocator(files, paths, environment, log);
        SettingsStore = new SettingsStore(files, clock, log, settingsPaths, new SettingsSerializer(dlssPlatform: !environment.IsMacOS), Executables);
        MissingVehicleLog = new MissingVehicleLog(SettingsStore.Settings, missingAssets, Library, log);
        Favorites = new FavoriteSceneries(files, log, settingsPaths.FavoritesPath());
        Tapes = new HaslerTapes(files, paths, log, clock, Timetables,
                                () => Processes.IsRunning(Path.GetFileNameWithoutExtension(SettingsStore.ResolveExecutable())));
        Gpus = new LinuxGpus(files, environment);
        Displays = new Displays(files, environment);
        StartSimulation = new StartSimulation(State, SettingsStore, Library.Vehicles, Executables,
                                              files, paths, Processes, random, log, Gpus, environment,
                                              new WineConfigLink(files, environment, settingsPaths, log), Tapes);
        Aftermath = new SimulatorAftermath(files, paths, log);
        SaveTrainset = new SaveSceneryTrainset(files, log);
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

    public ISceneryRepository Sceneries { get; }

    public SceneryFileWatch SceneryFiles { get; }

    public SceneryImageLocator SceneryImages { get; }

    public IMiniTextureIndex MiniTextures { get; }

    public IVehicleRepository Vehicles { get; }

    public IPhysicsRepository Physics { get; }

    public ISceneryTranslations SceneryTexts { get; }

    public TimetableLocator Timetables { get; }

    public LoadWeightsRepository LoadWeights { get; }

    public GameLibrary Library { get; }

    public AppState State { get; }

    public SettingsStore SettingsStore { get; }

    public SimulatorSettings Settings => SettingsStore.Settings;

    public ExecutableLocator Executables { get; }

    public LinuxGpus Gpus { get; }

    public Displays Displays { get; }

    public SettingsPaths SettingsPaths { get; }

    public MissingVehicleLog MissingVehicleLog { get; }

    public FavoriteSceneries Favorites { get; }

    public StartSimulation StartSimulation { get; }

    public SimulatorAftermath Aftermath { get; }

    public HaslerTapes Tapes { get; }

    public SaveSceneryTrainset SaveTrainset { get; }

    public LocalizationService Localization { get; }
}
