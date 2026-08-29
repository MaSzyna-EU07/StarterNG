using Avalonia.Controls;
using StarterNG.Classes;

namespace StarterNG;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void Report(LoadStatus status)
    {
        progressBar.Value = status.Fraction;
        statusText.Text = Format(status);
    }

    private static string Format(LoadStatus s)
    {
        string detail = string.IsNullOrEmpty(s.Detail) ? "" : $": {s.Detail}";
        return s.Phase switch
        {
            LoadPhase.Vehicles => App.Loc["LoadingVehicles"] + detail,
            LoadPhase.Sceneries => App.Loc["LoadingSceneries"] + detail,
            _ => App.Loc["LoadingDone"]
        };
    }
}
