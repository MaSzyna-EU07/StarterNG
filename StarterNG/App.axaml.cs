using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
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

        CultureInfo ci = CultureInfo.InstalledUICulture ;
 
        var langName = ci.Name switch
        {
            "pl-PL" => "Polski",
            _ => "English"
        };
 
        var langDict = new ResourceInclude(new Uri("avares://StarterNG/"))
        {
            Source = new Uri($"Assets/Langs/{langName}.axaml", UriKind.Relative)
        };

        var resources = Current!.Resources;

        var languageDictionaries = resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Where(d => d.Source != null && d.Source.OriginalString.Contains("Langs/"))
            .ToList();

        foreach (var dict in languageDictionaries)
        {
            resources.MergedDictionaries.Remove(dict);
        }

        resources.MergedDictionaries.Add(langDict);
        
         
        
        Loc.SetLanguage(langDict, langName);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Code page 1250 is needed to parse scenery files (done during load).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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