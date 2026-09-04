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
using StarterNG.Application;
using StarterNG.Classes;
using StarterNG.Infrastructure;
using StarterNG.Services;

namespace StarterNG;

public partial class App : Avalonia.Application
{

    /// <summary>
    /// The translation table, as a static source for the XAML bindings. Code
    /// should take <see cref="Application.Abstractions.ILocalizedStrings"/>
    /// through its constructor rather than reading this.
    /// </summary>
    public static LocalizationService Loc => AppServices.Current.Localization;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppServices.Current.SettingsStore.Load();

        KeyboardConfig.Instance.Load();

        CultureInfo ci = CultureInfo.InstalledUICulture ;

        var langName = AppServices.Current.Settings.LanguageWasSet
            ? AppServices.Current.Settings.Language
            : ci.Name switch
            {
                "pl-PL" => "Polski",
                _ => "English"
            };
        AppServices.Current.Settings.Language = langName;

        // What eu07.ini asked for, before the starter picks a language it can
        // actually display.
        string requestedCode = AppServices.Current.Settings.LanguageCode;

        ApplyLanguage(langName);

        // The starter ships no translation for this code, so its own interface
        // falls back - but the simulator has one and keeps it. Overwriting the key
        // here would silently move eu07.exe to English on the next save.
        if (!string.IsNullOrWhiteSpace(requestedCode) &&
            !string.Equals(requestedCode, Loc.CurrentLangCode, StringComparison.OrdinalIgnoreCase))
            AppServices.Current.Settings.LanguageCode = requestedCode;
    }

    public static void ApplyLanguage(string langNameOrCode)
    {

        var resolvedName = Loc.Load(langNameOrCode);

        // The service is the authority on which language actually loaded: the code
        // goes to eu07.ini for the simulator, the name to the picker.
        AppServices.Current.Settings.Language = resolvedName;
        AppServices.Current.Settings.LanguageCode = Loc.CurrentLangCode;

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
                try { AppServices.Current.SettingsStore.CaptureAndSave(); }
                catch (Exception ex) { Diagnostics.Log("Settings save", ex); }

                AppServices.Current.MissingVehicleLog.Dump();

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

                AppServices.Current.Library.Load(progress);
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
