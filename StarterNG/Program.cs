using Avalonia;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading.Tasks;
using StarterNG.Application;
using StarterNG.Infrastructure;

namespace StarterNG;

static class Program
{

    /// <summary>
    /// Drawn by the processor instead of the graphics card, for a machine on which the
    /// window comes up empty. Started with <c>--software-render</c>.
    /// </summary>
    private static bool _softwareRender;

    [STAThread]
    public static void Main(string[] args)
    {
        // Ahead of everything that could draw: what lies beside the starter is the simulator's.
        SystemGraphicsLibraries.LoadFirst(typeof(Win32PlatformOptions).Assembly);

        _softwareRender = args.Any(a => string.Equals(a, "--software-render", StringComparison.OrdinalIgnoreCase));

        // Composition root first: everything below, including the crash handlers,
        // resolves its collaborators from this graph.
        AppServices.Initialize();
        SystemGraphicsLibraries.LogShadowed();

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

    // Each platform reads only its own options, so both can be given everywhere.
    private static AppBuilder WithSoftwareRenderIfAsked(this AppBuilder builder) =>
        _softwareRender
            ? builder
                .With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.Software },
                    CompositionMode = new[] { Win32CompositionMode.RedirectionSurface }
                })
                .With(new X11PlatformOptions
                {
                    RenderingMode = new[] { X11RenderingMode.Software }
                })
            : builder;

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithSoftwareRenderIfAsked()
            .WithInterFont()
            .WithDiagnosticsLogging()
            .AfterSetup(_ => LogUiExceptions());
}
