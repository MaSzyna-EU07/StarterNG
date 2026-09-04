using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SettingsModel = StarterNG.Classes.Settings;

namespace StarterNG.Views;

public sealed class UartWindow : Window
{
    private readonly CheckBox _enabled = new();
    private readonly TextBox _port = new() { PlaceholderText = "COM3", MinWidth = 170 };
    private readonly TextBox _tune = new() { MinWidth = 170 };
    private readonly CheckBox _debug = new();
    private readonly CheckBox _main = new();
    private readonly CheckBox _scnd = new();
    private readonly CheckBox _train = new();
    private readonly CheckBox _local = new();
    private readonly CheckBox _radioVolume = new();
    private readonly CheckBox _radioChannel = new();

    public UartWindow()
    {
        Title = App.Loc["UartSection"];
        Width = 430;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _enabled.Content = App.Loc["UartEnable"];
        _debug.Content = App.Loc["UartDebug"];
        _main.Content = App.Loc["UartMain"];
        _scnd.Content = App.Loc["UartScnd"];
        _train.Content = App.Loc["UartTrain"];
        _local.Content = App.Loc["UartLocal"];
        _radioVolume.Content = App.Loc["UartRadioVolume"];
        _radioChannel.Content = App.Loc["UartRadioChannel"];

        Load();

        var root = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 8 };
        root.Children.Add(_enabled);
        root.Children.Add(Row(App.Loc["UartPort"], _port));
        root.Children.Add(Row(App.Loc["UartTune"], _tune));
        root.Children.Add(new TextBlock
        {
            Text = App.Loc["UartHint"],
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(_debug);

        root.Children.Add(new TextBlock
        {
            Text = App.Loc["UartFeature"],
            FontWeight = FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        });
        var top = new UniformGrid { Columns = 2, Margin = new Avalonia.Thickness(0, 0, 0, 2) };
        top.Children.Add(_main);
        top.Children.Add(_scnd);
        top.Children.Add(_train);
        top.Children.Add(_local);
        root.Children.Add(top);

        var mid = new UniformGrid { Columns = 2 };
        mid.Children.Add(_radioVolume);
        mid.Children.Add(_radioChannel);
        root.Children.Add(mid);

        var save = new Button
        {
            Content = App.Loc["UartSaveClose"],
            MinWidth = 140,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        save.Classes.Add("Accent");
        save.Click += (_, _) => { Store(); Close(); };
        root.Children.Add(save);

        Content = root;
    }

    private static Control Row(string label, Control input)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 110
        });
        sp.Children.Add(input);
        return sp;
    }

    private void Load()
    {
        var s = SettingsModel.Instance;
        _enabled.IsChecked = s.UartEnabled;
        _port.Text = s.UartPort;
        _tune.Text = s.UartTune;
        _debug.IsChecked = s.UartDebug;
        _main.IsChecked = s.UartMain;
        _scnd.IsChecked = s.UartScnd;
        _train.IsChecked = s.UartTrain;
        _local.IsChecked = s.UartLocal;
        _radioVolume.IsChecked = s.UartRadioVolume;
        _radioChannel.IsChecked = s.UartRadioChannel;
    }

    private void Store()
    {
        var s = SettingsModel.Instance;
        s.UartEnabled = _enabled.IsChecked == true;
        s.UartPort = _port.Text ?? "";
        s.UartTune = _tune.Text ?? "";
        s.UartDebug = _debug.IsChecked == true;
        s.UartMain = _main.IsChecked == true;
        s.UartScnd = _scnd.IsChecked == true;
        s.UartTrain = _train.IsChecked == true;
        s.UartLocal = _local.IsChecked == true;
        s.UartRadioVolume = _radioVolume.IsChecked == true;
        s.UartRadioChannel = _radioChannel.IsChecked == true;
    }
}
