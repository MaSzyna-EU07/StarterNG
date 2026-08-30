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
using StarterNG.Infrastructure;
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
        ApplyCulture(Loc.CurrentLangCode);
    }

    private static void ApplyCulture(string langCode)
    {
        CultureInfo culture;
        try
        {
            culture = langCode switch
            {
                "" or "en" => CultureInfo.InvariantCulture,
                "cz" => CultureInfo.GetCultureInfo("cs"),
                _ => CultureInfo.GetCultureInfo(langCode)
            };
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            desktop.ShutdownRequested += (_, _) =>
            {
                try { Settings.Instance.CaptureAndSave(); }
                catch (Exception ex) { Diagnostics.Log("Settings save", ex); }

                Settings.Instance.DumpMissingVehicleLog();

                try
                {
                    if (KeyboardConfig.Instance.Dirty)
                        KeyboardConfig.Instance.Save();
                }
                catch (Exception ex) { Diagnostics.Log("Keyboard config save", ex); }

                Diagnostics.Flush();
            };

            SplashWindow? splash = null;
            bool mainReady = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Task.Run(async () =>
            {
                var progress = new Progress<LoadStatus>(status =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (mainReady)
                            return;

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
            }).ContinueWith(load =>
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    MainWindow main;
                    try
                    {
                        main = new MainWindow();
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log("Main window", ex);
                        Diagnostics.Flush();
                        Console.Error.WriteLine(ex);
                        mainReady = true;
                        splash?.Close();
                        desktop.Shutdown(1);
                        return;
                    }

                    mainReady = true;
                    desktop.MainWindow = main;
                    main.Show();
                    splash?.Close();
                    splash = null;

                    if (load.IsFaulted && load.Exception is { } error)
                    {
                        var inner = error.GetBaseException();
                        Diagnostics.Log("Game data load", inner);
                        await Diagnostics.ReportAsync(
                            $"{Loc["FaultLoadData"]}{Environment.NewLine}{Environment.NewLine}{Loc["FaultDetail"]} {inner.Message}");
                    }

                    var faults = Diagnostics.CheckInstallation();
                    if (faults.Count > 0)
                    {
                        await Diagnostics.ReportAsync(
                            string.Join(Environment.NewLine, faults) + Environment.NewLine + Environment.NewLine +
                            Loc["FaultBadInstall"]);
                    }
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
