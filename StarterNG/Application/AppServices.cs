using System;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Adapters;
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

    private AppServices(IGamePaths paths, IFileSystem files, IClock clock)
    {
        Paths = paths;
        Files = files;
        Clock = clock;

        var log = new FileDiagnosticsLog(files, clock, paths);
        Log = log;
        Processes = new SystemProcessLauncher(log);
        Localization = new LocalizationService(log);
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
    public static AppServices Initialize(IGamePaths? paths = null, IFileSystem? files = null, IClock? clock = null)
    {
        _current = new AppServices(
            paths ?? GamePaths.ForCurrentDirectory(),
            files ?? new PhysicalFileSystem(),
            clock ?? new SystemClock());
        return _current;
    }

    public IGamePaths Paths { get; }

    public IFileSystem Files { get; }

    public IClock Clock { get; }

    public IDiagnosticsLog Log { get; }

    public IProcessLauncher Processes { get; }

    /// <summary>
    /// Exposed as the concrete service rather than <see cref="ILocalizedStrings"/>
    /// because the XAML binds to it as a change-notifying source; code should take
    /// the port.
    /// </summary>
    public LocalizationService Localization { get; }
}
