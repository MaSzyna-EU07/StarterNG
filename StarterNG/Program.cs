using Avalonia;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using StarterNG.Application;
using StarterNG.Infrastructure;

namespace StarterNG;

static class Program
{

    [STAThread]
    public static void Main(string[] args)
    {
        // Composition root first: everything below, including the crash handlers,
        // resolves its collaborators from this graph.
        AppServices.Initialize();

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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithDiagnosticsLogging()
            .AfterSetup(_ => LogUiExceptions());
}
