using Avalonia;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using StarterNG.Application;
using StarterNG.Infrastructure;

namespace StarterNG;

static class Program
{

    private static bool _softwareRender;

    [STAThread]
    public static void Main(string[] args)
    {
        // Ahead of everything that could draw: what lies beside the starter is the simulator's.
        var graphics = SystemGraphicsLibraries.TakeOnWindows();
        graphics?.ServeTo(typeof(Win32PlatformOptions).Assembly);

        _softwareRender = SoftwareRender.AskedIn(args);

        // Composition root first: everything below, including the crash handlers,
        // resolves its collaborators from this graph.
        AppServices.Initialize();
        if (graphics?.Note is { } note)
            Diagnostics.Log(note);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Diagnostics.Log("Unhandled exception", ex);
            Diagnostics.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.Log("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Fatal", ex);
            throw;
        }
        finally
        {
            Diagnostics.Flush();
        }
    }

    private static AppBuilder WithDiagnosticsLogging(this AppBuilder builder)
    {
        DiagnosticsLogSink.Install();
        return builder;
    }

    // After setup: the dispatcher takes its platform's implementation when first asked
    // for. Not marked handled: a handler that failed half way leaves the state unknown,
    // and the exception goes on to end the program as before, only no longer unexplained.
    private static void LogUiExceptions()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Diagnostics.Log("Unhandled UI exception", e.Exception);
            Diagnostics.Flush();
        };
    }

    private static AppBuilder WithWindowSurfaces(this AppBuilder builder) =>
        WindowSurfaces.ApplyTo(builder);

    private static AppBuilder WithSoftwareRenderIfAsked(this AppBuilder builder) =>
        _softwareRender ? SoftwareRender.ApplyTo(builder) : builder;

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithWindowSurfaces()
            // After the surfaces: the later options of a platform are the ones it reads.
            .WithSoftwareRenderIfAsked()
            .WithInterFont()
            .WithDiagnosticsLogging()
            .AfterSetup(_ => LogUiExceptions());
}
