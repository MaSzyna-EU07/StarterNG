using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StarterNG.Classes;
using StarterNG.Services;

namespace StarterNG;

public partial class App : Application
{

    public static LocalizationService Loc { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        Settings.Instance.Load();

        KeyboardConfig.Instance.Load();

        CultureInfo ci = CultureInfo.InstalledUICulture ;

        var langName = Settings.Instance.LanguageWasSet
            ? Settings.Instance.Language
            : ci.Name switch
            {
                "pl-PL" => "Polski",
                _ => "English"
            };
        Settings.Instance.Language = langName;

        ApplyLanguage(langName);
    }

    public static void ApplyLanguage(string langNameOrCode)
    {

        var resolvedName = Loc.Load(langNameOrCode);
        Settings.Instance.Language = resolvedName;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            desktop.ShutdownRequested += (_, _) =>
            {
                Settings.Instance.CaptureAndSave();
                Settings.Instance.DumpMissingVehicleLog();
                if (KeyboardConfig.Instance.Dirty)
                    KeyboardConfig.Instance.Save();
            };

            SplashWindow? splash = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Task.Run(async () =>
            {
                var progress = new Progress<LoadStatus>(status =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {

                        if (sw.ElapsedMilliseconds > 300 && splash == null)
                        {
                            splash = new SplashWindow();
                            desktop.MainWindow = splash;
                            splash.Show();
                        }
                        splash?.Report(status);
                    });
                });

                GameData.Instance.Load(progress);
            }).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                    splash?.Close();
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
