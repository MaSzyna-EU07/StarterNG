using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using StarterNG.Infrastructure;
using StarterNG.Application;

namespace StarterNG.Views;

public sealed class AdvancedSettingsWindow : Window
{
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = MonoFont.Family,
        FontSize = 12
    };

    private readonly CheckBox _compressTex = new();
    private readonly CheckBox _mipmaps = new();
    private readonly CheckBox _scaleSpeculars = new();
    private readonly CheckBox _shaderGamma = new();
    private readonly CheckBox _useGles = new();
    private readonly CheckBox _convertModels = new();
    private readonly CheckBox _resourceMove = new();
    private readonly CheckBox _resourceSweep = new();
    private readonly CheckBox _ignoreIrrelevant = new();
    private readonly Slider _threads = new() { Minimum = 0, Maximum = 16, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _threadsValue = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 24 };

    public event Action<string>? ToolRequested;

    public AdvancedSettingsWindow()
    {
        Title = App.Loc["AdvancedSettings"];
        Width = 620;
        Height = 470;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _compressTex.Content = App.Loc["CompressTextures"];
        _mipmaps.Content = App.Loc["ScreenMipmaps"];
        _scaleSpeculars.Content = App.Loc["ScaleSpeculars"];
        _shaderGamma.Content = App.Loc["ShaderGamma"];
        _useGles.Content = App.Loc["UseGLES"];
        _convertModels.Content = App.Loc["ExtendedModelConversion"];
        _resourceMove.Content = App.Loc["GfxResourceMove"];
        _resourceSweep.Content = App.Loc["GfxResourceSweep"];
        _ignoreIrrelevant.Content = App.Loc["IgnoreIrrelevantTrains"];
        _threads.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                _threadsValue.Text = ((int)_threads.Value).ToString();
        };

        Load();

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = App.Loc["AdvLogPage"], Content = BuildLogPage() });
        tabs.Items.Add(new TabItem { Header = App.Loc["Advanced"], Content = BuildOptionsPage() });
        tabs.Items.Add(new TabItem { Header = App.Loc["Tools"], Content = BuildToolsPage() });

        var close = new Button
        {
            Content = App.Loc["Close"],
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        close.Classes.Add("Accent");
        close.Click += (_, _) => { Store(); Close(); };

        var root = new DockPanel { Margin = new Avalonia.Thickness(10) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;
    }

    private Control BuildLogPage()
    {
        _log.Text = Diagnostics.Text;

        var clear = new Button
        {
            Content = App.Loc["AdvClearLog"],
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        clear.Classes.Add("Flat");
        clear.Click += (_, _) => { Diagnostics.Clear(); _log.Text = ""; };

        ScrollViewer.SetHorizontalScrollBarVisibility(_log, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_log, ScrollBarVisibility.Auto);

        var panel = new DockPanel { Margin = new Avalonia.Thickness(8) };
        DockPanel.SetDock(clear, Dock.Bottom);
        panel.Children.Add(clear);
        panel.Children.Add(_log);
        return panel;
    }

    private Control BuildOptionsPage()
    {
        var sp = new StackPanel { Margin = new Avalonia.Thickness(10), Spacing = 8 };
        foreach (var c in new Control[]
                 {
                     _compressTex, _scaleSpeculars, _useGles, _shaderGamma, _mipmaps,
                     _convertModels, _resourceMove, _resourceSweep, _ignoreIrrelevant
                 })
            sp.Children.Add(c);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = App.Loc["TrainPhysicsThreads"],
            VerticalAlignment = VerticalAlignment.Center
        });
        _threads.Width = 220;
        row.Children.Add(_threads);
        row.Children.Add(_threadsValue);
        sp.Children.Add(row);

        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private Control BuildToolsPage()
    {
        var sp = new StackPanel { Margin = new Avalonia.Thickness(10), Spacing = 8 };

        Button Tool(string key, string tag)
        {
            var b = new Button
            {
                Content = App.Loc[key],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            b.Classes.Add("Flat");
            b.Click += (_, _) => ToolRequested?.Invoke(tag);
            return b;
        }

        sp.Children.Add(Tool("AddAllCategory", "category"));
        sp.Children.Add(Tool("AddUniqueMmd", "mmd"));
        sp.Children.Add(Tool("RemoveAllTrains", "removeall"));
        return sp;
    }

    private void Load()
    {
        var s = AppServices.Current.Settings;
        _compressTex.IsChecked = s.CompressTextures;
        _mipmaps.IsChecked = s.ScreenMipmaps;
        _scaleSpeculars.IsChecked = s.ScaleSpeculars;
        _shaderGamma.IsChecked = s.ShaderGamma;
        _useGles.IsChecked = s.UseGLES;
        _convertModels.IsChecked = s.ExtendedModelConversion;
        _resourceMove.IsChecked = s.GfxResourceMove;
        _resourceSweep.IsChecked = s.GfxResourceSweep;
        _ignoreIrrelevant.IsChecked = s.IgnoreIrrelevantTrains;
        _threads.Value = s.TrainPhysicsThreads;
        _threadsValue.Text = s.TrainPhysicsThreads.ToString();
    }

    private void Store()
    {
        var s = AppServices.Current.Settings;
        s.CompressTextures = _compressTex.IsChecked == true;
        s.ScreenMipmaps = _mipmaps.IsChecked == true;
        s.ScaleSpeculars = _scaleSpeculars.IsChecked == true;
        s.ShaderGamma = _shaderGamma.IsChecked == true;
        s.UseGLES = _useGles.IsChecked == true;
        s.ExtendedModelConversion = _convertModels.IsChecked == true;
        s.GfxResourceMove = _resourceMove.IsChecked == true;
        s.GfxResourceSweep = _resourceSweep.IsChecked == true;
        s.IgnoreIrrelevantTrains = _ignoreIrrelevant.IsChecked == true;
        s.TrainPhysicsThreads = (int)_threads.Value;
    }
}
