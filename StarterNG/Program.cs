using Avalonia;
using System;
using System.Threading.Tasks;
using StarterNG.Infrastructure;

namespace StarterNG;

class Program
{

    [STAThread]
    public static void Main(string[] args)
    {
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
