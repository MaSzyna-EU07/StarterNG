using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
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
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}