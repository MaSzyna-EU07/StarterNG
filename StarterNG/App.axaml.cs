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

        // Key bindings live in eu07_input-keyboard.ini next to eu07.ini.
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

            // Persist settings when the launcher is closing. Key bindings are only
            // rewritten when actually edited, so an untouched file keeps its layout.
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
                        // Show splash only if loading takes more than 300ms
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