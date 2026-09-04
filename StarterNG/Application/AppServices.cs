using System;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Infrastructure.Sceneries;
using StarterNG.Infrastructure.Settings;
using StarterNG.Infrastructure.Vehicles;
using StarterNG.Services;

namespace StarterNG.Application;

/// <summary>
/// The composition root: the single place where concrete adapters are chosen and
/// wired to the ports the rest of the application depends on.
/// </summary>
/// <remarks>
/// Hand-written on purpose. The starter publishes with <c>PublishAot</c> and
/// <c>PublishTrimmed</c>, which rules out reflection-based containers; the object
/// graph is small enough that explicit construction is also the clearest
/// documentation of what depends on what.
///
/// <see cref="Current"/> is a service locator and is used in exactly one kind of
/// place — the entry point and the view constructors Avalonia creates for us,
/// which cannot take arguments. Everything below that boundary receives its
/// dependencies through its constructor; new code must not reach for
/// <see cref="Current"/>.
/// </remarks>
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
        LoadWeights = new LoadWeightsRepository(files, paths, log);
        Library = new GameLibrary(Vehicles, Sceneries, MiniTextures, Physics, log);
        State = new AppState();

        var settingsPaths = new SettingsPaths(environment, paths);
        Executables = new ExecutableLocator(files, paths, environment, log);
        SettingsStore = new SettingsStore(files, clock, log, settingsPaths, new SettingsSerializer(), Executables);
        SettingsPaths = settingsPaths;
        MissingVehicleLog = new MissingVehicleLog(SettingsStore.Settings, MissingAssets, Library, log);
    }

    /// <summary>
    /// The graph built by the entry point. Throws when read before
    /// <see cref="Initialize"/>, which would mean a static initializer somewhere
    /// ran ahead of the composition root.
    /// </summary>
    public static AppServices Current =>
        _current ?? throw new InvalidOperationException(
            "AppServices.Current read before Initialize(); the composition root must be built first.");

    /// <summary>Builds the production graph. Called once, from the entry point.</summary>
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

    public LoadWeightsRepository LoadWeights { get; }

    /// <summary>Everything read out of the installation: rolling stock and scenarios.</summary>
    public GameLibrary Library { get; }

    /// <summary>What the user currently has selected.</summary>
    public AppState State { get; }

    /// <summary>The settings the starter runs with, and their load/save lifecycle.</summary>
    public SettingsStore SettingsStore { get; }

    /// <summary>Shorthand for the settings themselves.</summary>
    public SimulatorSettings Settings => SettingsStore.Settings;

    public ExecutableLocator Executables { get; }

    public SettingsPaths SettingsPaths { get; }

    public MissingVehicleLog MissingVehicleLog { get; }

    /// <summary>
    /// Exposed as the concrete service rather than <see cref="ILocalizedStrings"/>
    /// because the XAML binds to it as a change-notifying source; code should take
    /// the port.
    /// </summary>
    public LocalizationService Localization { get; }
}
