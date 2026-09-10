using Avalonia;
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithDiagnosticsLogging();
}
