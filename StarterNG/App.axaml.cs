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

        // Load the persisted configuration up front so the saved language wins;
        // fall back to the system culture only when the file has no lang entry.
        Settings.Instance.Load();

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

    /// <summary>
    /// Swaps the active language by loading the matching XML file from the
    /// <c>lang/</c> folder next to the executable (see <see cref="LocalizationService"/>).
    /// The translations are no longer compiled into the assembly, so they can be
    /// edited or added without rebuilding the launcher. Accepts either a display
    /// name ("English", "Polski") or a short code ("en", "pl").
    /// </summary>
    public static void ApplyLanguage(string langNameOrCode)
    {
        // Loc.Load resolves the file and returns the canonical display name,
        // which we mirror back into the settings so the stored value stays valid.
        var resolvedName = Loc.Load(langNameOrCode);
        Settings.Instance.Language = resolvedName;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Code page 1250 is needed to parse scenery files (done during load).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Persist settings when the launcher is closing.
            desktop.ShutdownRequested += (_, _) => Settings.Instance.CaptureAndSave();

            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();

            // Progress<T> marshals callbacks back to this (UI) thread.
            var progress = new Progress<LoadStatus>(splash.Report);

            Task.Run(async () =>
            {
                GameData.Instance.Load(progress);
                await Task.Delay(400); // let the completed bar be visible briefly
            }).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var main = new MainWindow();
                    desktop.MainWindow = main;   // keep a main window before closing the splash
                    main.Show();
                    splash.Close();
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}