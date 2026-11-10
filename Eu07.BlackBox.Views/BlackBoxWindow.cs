using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Eu07.BlackBox.Views;

/// <summary>
/// Replays a black box. Above: the train and the scenery, playing and how late the train
/// was. In the middle the map at the moment played, the timeline of the user's train under
/// it; beside them the events, the timetable or the figures of the driving, and the cab. A file
/// the simulator still writes can be followed: what it adds is read every two seconds, and
/// playing at its end keeps up with it.
/// </summary>
public sealed class BlackBoxWindow : Window
{
    private static readonly double[] Speeds = { 1, 2, 5, 10, 30, 60 };

    private readonly IBlackBoxStrings _strings;
    private readonly MapView _map;
    private readonly TimelineView _timeline;
    private readonly EventList _events;
    private readonly TimetablePanel _timetable;
    private readonly ScrollViewer _timetableScroll;
    private readonly ScrollViewer _summaryScroll;
    private readonly CabPanel _cab;
    private readonly UiKit.Segmented _tabs;
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = new() { FontSize = 11, Foreground = Palette.DimText, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, MinWidth = 86, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _delayText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
    private readonly TextBlock _readout = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Palette.DimText, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileState = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Palette.FaintText };
    private readonly TextBlock _message = new() { FontSize = 14, Foreground = Palette.DimText, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(40) };
    private readonly StackPanel _tiles = new();
    private readonly UiKit.Tile _distance, _maxSpeed, _driven, _overAi, _signals, _stops, _delay, _platform, _lights, _horn, _acceleration, _security, _state;
    private readonly Button _open;
    private readonly Button _play;
    private readonly ComboBox _speed = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, FontSize = 12, Padding = new Thickness(8, 3) };
    private readonly Avalonia.Controls.Primitives.ToggleButton _follow;
    private readonly Avalonia.Controls.Primitives.ToggleButton _live;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Stopwatch _played = new();

    private BlackBoxReader? _reader;
    private string? _path;
    private double _time;
    private bool _reading;

    public BlackBoxWindow() : this(null)
    {
    }

    public BlackBoxWindow(IBlackBoxStrings? strings)
    {
        _strings = strings ?? new DefaultBlackBoxStrings();
        Title = _strings["Title"];
        Width = 1440;
        Height = 900;
        MinWidth = 960;
        MinHeight = 600;
        Background = Palette.Background;
        UiKit.Accent(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _map = new MapView { Strings = _strings };
        _timeline = new TimelineView { Strings = _strings };
        _events = new EventList { Strings = _strings, Background = Brushes.Transparent };
        _timetable = new TimetablePanel { Strings = _strings };
        _timetableScroll = new ScrollViewer { Content = _timetable, IsVisible = false };
        _summaryScroll = new ScrollViewer { Content = _tiles, IsVisible = false };
        _cab = new CabPanel { Strings = _strings };

        // ----- the bar on top: the train, playing, the switches -----
        _open = UiKit.Button(_strings["Open"]);
        _open.Click += async (_, _) => await PickAsync();
        var reload = UiKit.Button(_strings["Reload"]);
        reload.Click += (_, _) => ReadMore();
        var fit = UiKit.Button(_strings["Fit"]);
        fit.Click += (_, _) => ShowWhole();
        _follow = UiKit.Toggle(_strings["Follow"]);
        _follow.IsCheckedChanged += (_, _) => _map.Follow = _follow.IsChecked == true;
        _map.FollowStopped += () => _follow.IsChecked = false;
        _live = UiKit.Toggle(_strings["Live"], _strings["LiveTip"]);
        _live.IsCheckedChanged += (_, _) =>
        {
            if (_live.IsChecked == true)
                _reload.Start();
            else
                _reload.Stop();
        };

        _play = UiKit.PlayButton();
        _play.Content = UiKit.PlayGlyph(false);
        ToolTip.SetTip(_play, _strings["Play"]);
        _play.Click += (_, _) => Playing = !Playing;
        _speed.ItemsSource = Speeds.Select(s => $"×{s.ToString(CultureInfo.InvariantCulture)}").ToArray();
        _speed.SelectedIndex = 3;

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _subtitle } };
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { _play, _clock, _speed, _delayText } };
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _follow, _live, fit, reload, _open } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        Place(header, titles, 0, 0);
        Place(header, transport, 0, 1);
        switches.HorizontalAlignment = HorizontalAlignment.Right;
        Place(header, switches, 0, 2);
        titles.Margin = new Thickness(0, 0, 16, 0);
        var headerBar = new Border
        {
            Background = Palette.Panel,
            BorderBrush = Palette.CardBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 0),
            Height = 44,
            Child = header
        };

        // ----- the figures, a table in a tab of their own -----
        _distance = Tile("StatDistance");
        _maxSpeed = Tile("StatMaxSpeed");
        _driven = Tile("StatDriven");
        _overAi = Tile("StatOverAi");
        _signals = Tile("StatSignals");
        _stops = Tile("StatStops");
        _delay = Tile("StatDelay");
        _platform = Tile("StatPlatform");
        _lights = Tile("StatLights");
        _horn = Tile("StatHorn");
        _acceleration = Tile("StatAcceleration");
        _security = Tile("StatSecurity");
        _state = Tile("StatRecording");

        _timeline.Seek += t => SetTime(t);
        _timeline.Hovered += ShowReadout;
        _events.Seek += t => SetTime(t);
        _timetable.Seek += t => SetTime(t);
        _tick.Tick += (_, _) => Advance();
        _reload.Tick += (_, _) => ReadMore();

        // ----- the map with its zoom, the timeline, the side -----
        var zoomIn = MapButton("+", _strings["ZoomIn"], () => _map.ZoomBy(1.6));
        var zoomOut = MapButton("−", _strings["ZoomOut"], () => _map.ZoomBy(1 / 1.6));
        var zoomFit = MapButton("⤢", _strings["Fit"], ShowWhole);
        var zoom = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { zoomIn, zoomOut, zoomFit }
        };
        var mapArea = new Panel { Children = { _map, zoom, _message } };
        _map.ClipToBounds = true;

        _tabs = new UiKit.Segmented(_strings["TabEvents"], _strings["TabTimetable"], _strings["TabSummary"]);
        _tabs.SelectedChanged += index =>
        {
            _events.IsVisible = index == 0;
            _timetableScroll.IsVisible = index == 1;
            _summaryScroll.IsVisible = index == 2;
        };
        var side = new DockPanel();
        DockPanel.SetDock(_tabs, Dock.Top);
        side.Children.Add(_tabs);
        side.Children.Add(new Panel { Children = { _events, _timetableScroll, _summaryScroll } });
        var cabCard = UiKit.Group(_strings["Cab"], _cab);
        cabCard.Margin = new Thickness(0, UiKit.Gap, 0, 0);
        var sideColumn = new DockPanel();
        DockPanel.SetDock(cabCard, Dock.Bottom);
        sideColumn.Children.Add(cabCard);
        sideColumn.Children.Add(UiKit.Frame(side));

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"*,{UiKit.Gap.ToString(CultureInfo.InvariantCulture)},400"),
            RowDefinitions = new RowDefinitions($"*,{UiKit.Gap.ToString(CultureInfo.InvariantCulture)},230")
        };
        Place(body, UiKit.Group(_strings["PanelMap"], mapArea), 0, 0);
        Place(body, new GridSplitter { Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows, MinHeight = UiKit.Gap }, 1, 0);
        Place(body, UiKit.Group(_strings["PanelTimeline"], _timeline), 2, 0);
        var columns = new GridSplitter { Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns, MinWidth = UiKit.Gap };
        Place(body, columns, 0, 1);
        Grid.SetRowSpan(columns, 3);
        Place(body, sideColumn, 0, 2);
        Grid.SetRowSpan(sideColumn, 3);

        // ----- the line at the bottom: the moment under the pointer, the state of the file -----
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Place(status, _readout, 0, 0);
        Place(status, _fileState, 0, 1);
        var statusBar = new Border
        {
            Background = Palette.Panel,
            BorderBrush = Palette.CardBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 3),
            Child = status
        };

        body.Margin = new Thickness(UiKit.Gap);
        var root = new DockPanel { Background = Palette.Background };
        DockPanel.SetDock(headerBar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(headerBar);
        root.Children.Add(statusBar);
        root.Children.Add(body);
        Content = root;

        _title.Text = _strings["Title"];
        _subtitle.Text = _strings["NoFile"];
        _message.Text = _strings["NoFile"];
        Closed += (_, _) =>
        {
            _tick.Stop();
            _reload.Stop();
        };
    }

    // When and at which end the signals were wrong: "10:55:12 head Tb 1 instead of Pc 1".
    private string LightsFaultText(Recording recording, LightsFault fault, CultureInfo culture)
    {
        var ends = new List<string>();
        if (fault.HeadWrong)
            ends.Add(string.Format(culture, _strings["StatLightsHead"], HeadCodes.Name(fault.Head), HeadCodes.Name(fault.HeadRequired)));
        if (fault.TailWrong)
            ends.Add(string.Format(culture, _strings["StatLightsTail"], HeadCodes.Name(fault.Tail), HeadCodes.Name(fault.TailRequired)));
        return $"{Clock(recording.ClockAt(fault.Start).TotalSeconds)} {string.Join(", ", ends)}";
    }

    private UiKit.Tile Tile(string key)
    {
        var tile = new UiKit.Tile(_strings[key]);
        _tiles.Children.Add(tile);
        return tile;
    }

    private static Button MapButton(string text, string tip, Action action)
    {
        var button = UiKit.Button(text, tip);
        button.Width = 28;
        button.Height = 28;
        button.Padding = new Thickness(0);
        button.FontSize = 15;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Background = Palette.Panel;
        button.Click += (_, _) => action();
        return button;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    /// <summary>The button for opening another file: the starter opens the one picked in its list.</summary>
    public bool CanOpenFiles
    {
        get => _open.IsVisible;
        set => _open.IsVisible = value;
    }

    /// <summary>Reads what the simulator adds to the file every two seconds.</summary>
    public bool Live
    {
        get => _live.IsChecked == true;
        set => _live.IsChecked = value;
    }

    public string? FilePath => _path;

    public Recording? Recording => _reader?.Recording;

    public double CurrentTime => _time;

    /// <summary>Goes to a moment of the recording, in seconds from its start.</summary>
    public void SeekTo(double t) => SetTime(t);

    /// <summary>Keeps the user's train in the middle of the map.</summary>
    public bool FollowTrain
    {
        get => _follow.IsChecked == true;
        set => _follow.IsChecked = value;
    }

    /// <summary>The timetable beside the map instead of the events.</summary>
    public bool ShowTimetable
    {
        get => _tabs.Selected == 1;
        set => _tabs.Selected = value ? 1 : 0;
    }

    /// <summary>The summary of the driving beside the map instead of the events.</summary>
    public bool SummaryShown
    {
        get => _tabs.Selected == 2;
        set => _tabs.Selected = value ? 2 : 0;
    }

    private bool Playing
    {
        get => _tick.IsEnabled;
        set
        {
            if (value == _tick.IsEnabled)
                return;
            if (value)
            {
                if (Recording is { } r && _time >= r.Duration - 0.05 && !Live)
                    SetTime(0);
                _played.Restart();
                _tick.Start();
            }
            else
                _tick.Stop();
            _play.Content = UiKit.PlayGlyph(value);
            ToolTip.SetTip(_play, _strings[value ? "Pause" : "Play"]);
        }
    }

    private void ShowWhole()
    {
        _follow.IsChecked = false;
        _map.Fit();
        _timeline.Fit();
    }

    /// <summary>Opens a black box, read whole on another thread.</summary>
    public async Task OpenAsync(string path)
    {
        Playing = false;
        ShowMessage(string.Format(CultureInfo.CurrentCulture, _strings["Reading"], Path.GetFileName(path)));
        try
        {
            var reader = await Task.Run(() =>
            {
                var r = new BlackBoxReader();
                r.ReadMore(path);
                return r;
            });
            _reader = reader;
            _path = path;
            Title = string.Format(CultureInfo.CurrentCulture, _strings["TitleOf"], Path.GetFileName(path));
            _map.Recording = reader.Recording;
            _timeline.Recording = reader.Recording;
            _events.Recording = reader.Recording;
            _timetable.Recording = reader.Recording;
            _cab.Recording = reader.Recording;
            _message.IsVisible = false;
            // A file still written opens at its end, where the train is now.
            SetTime(Live ? reader.Recording.Duration : 0);
            ShowSummary();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowMessage(string.Format(CultureInfo.CurrentCulture, _strings["Unreadable"], path, ex.Message));
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
        _subtitle.Text = text;
        _fileState.Text = "";
    }

    /// <summary>What the simulator added since the last read. Small, so read here, between frames.</summary>
    public void ReadMore()
    {
        if (_reader is not { } reader || _path is null || _reading)
            return;
        _reading = true;
        try
        {
            bool atEnd = _time >= reader.Recording.Duration - 0.5;
            if (reader.ReadMore(_path) == 0)
                return;
            _events.Refresh();
            if (atEnd && !Playing)
                SetTime(reader.Recording.Duration);
            else
                SetTime(_time);
            ShowSummary();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowMessage(string.Format(CultureInfo.CurrentCulture, _strings["Unreadable"], _path, ex.Message));
        }
        finally
        {
            _reading = false;
        }
    }

    private async Task PickAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _strings["Open"],
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(_strings["FileFilter"]) { Patterns = new[] { "*.blackbox" } } }
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await OpenAsync(path);
    }

    private void Advance()
    {
        if (Recording is not { } recording)
            return;
        double real = _played.Elapsed.TotalSeconds;
        _played.Restart();
        double speed = Speeds[Math.Clamp(_speed.SelectedIndex, 0, Speeds.Length - 1)];
        double next = _time + real * speed;
        if (next >= recording.Duration)
        {
            next = recording.Duration;
            // At the end of a file still written it waits for more.
            if (!Live)
                Playing = false;
        }
        SetTime(next);
    }

    private void SetTime(double t)
    {
        var recording = Recording;
        double duration = recording?.Duration ?? 0;
        _time = Math.Clamp(t, 0, duration);
        _map.Time = _time;
        _timeline.Time = _time;
        _cab.Time = _time;
        _timetable.Time = _time;
        if (recording is null)
            return;
        _clock.Text = recording.ClockAt(_time).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        _events.ShowTime(_time);
        ShowReadout(null);
        ShowDelay(recording);
    }

    /// <summary>How late the user's train was at the moment played, by the clock.</summary>
    private void ShowDelay(Recording recording)
    {
        if (StationProgress.DelayAt(recording, _time) is not { } delay)
        {
            _delayText.Text = "";
            return;
        }
        var culture = CultureInfo.CurrentCulture;
        var brush = Palette.Delay(delay.Minutes);
        _delayText.Text = Math.Abs(delay.Minutes) < 0.5
            ? _strings["OnTime"]
            : string.Format(culture, _strings["DelayNow"], string.Format(culture, _strings["Minutes"], delay.Minutes));
        _delayText.Foreground = brush;
        ToolTip.SetTip(_delayText, string.Format(culture, _strings[delay.Standing ? "DelayStanding" : "DelayAfter"], delay.Train,
                                                 StationNames.Display(delay.Station)));
    }

    /// <summary>The user's train at the moment under the pointer of the timeline, or at the moment played.</summary>
    private void ShowReadout(double? hovered)
    {
        if (Recording is not { } recording)
            return;
        double t = hovered ?? _time;
        string clock = recording.ClockAt(t).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        var culture = CultureInfo.CurrentCulture;
        if (recording.CabAt(t) is not { } s || t - s.Time > 1.0)
        {
            _readout.Text = string.Format(culture, _strings["CursorOut"], clock);
            return;
        }
        if (!s.AiValid)
        {
            _readout.Text = string.Format(culture, _strings["CursorNoAi"], clock, s.AbsSpeedKmh);
            return;
        }
        string limit = s.Ai.HasNextLimit
            ? string.Format(culture, _strings["LimitAt"], s.Ai.NextLimitKmh, s.Ai.NextLimitDistance)
            : _strings["None"];
        _readout.Text = string.Format(culture, _strings["Cursor"], clock, s.AbsSpeedKmh,
                                      s.Ai.TargetKmh.ToString("0", culture) + " km/h", limit);
    }

    private void ShowSummary()
    {
        if (Recording is not { } recording)
            return;
        var summary = recording.Summarize();
        var culture = CultureInfo.CurrentCulture;

        // The scenery, the train and the vehicle the user started in.
        string scenery = recording.Info?.Scenery ?? Path.GetFileName(_path) ?? "";
        _title.Text = Path.GetFileNameWithoutExtension(scenery.TrimStart('$'));
        var timetable = recording.TimetableAt(_time);
        string vehicle = recording.Cab.Count > 0 ? recording.Vehicle(recording.Cab[^1].Vehicle)?.Name ?? "" : recording.Info?.StartVehicle ?? "";
        var parts = new[]
        {
            timetable is null ? null : $"{timetable.Category} {timetable.Train}".Trim(),
            timetable is null || timetable.From.Length + timetable.To.Length == 0 ? null : string.Format(culture, _strings["Relation"], timetable.From, timetable.To),
            vehicle,
            recording.Info?.Started.Replace('T', ' ')
        };
        _subtitle.Text = string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        _distance.Show($"{summary.DistanceKm:0.0} km");
        _maxSpeed.Show($"{summary.MaxSpeedKmh:0} km/h");
        _driven.Show(Clock(summary.DrivenSeconds), string.Format(culture, _strings["StatAutopilotSub"], Clock(summary.AutopilotSeconds)));
        _overAi.Show(Clock(summary.OverAiSeconds),
                     string.Format(culture, _strings["StatOverAiSub"], Clock(summary.WellOverAiSeconds), DrivingSummary.DefaultTolerance, summary.MaxOverAiKmh),
                     summary.WellOverAiSeconds > 0 ? Palette.Bad : summary.OverAiSeconds > 0 ? Palette.Warn : Palette.Good);
        _signals.Show(summary.SignalsPassed.ToString(culture), string.Format(culture, _strings["StatSignalsSub"], summary.PassedAtDanger.Count),
                      summary.PassedAtDanger.Count > 0 ? Palette.Bad : null);
        _stops.IsVisible = recording.Timetables.Count > 0;
        _stops.Show(summary.Stops.ToString(culture), string.Format(culture, _strings["StatStopsSub"], summary.StopsMissed),
                    summary.StopsMissed > 0 ? Palette.Bad : null);
        _delay.IsVisible = summary.MaxDelayMinutes is not null;
        if (summary.MaxDelayMinutes is { } delay)
            _delay.Show(string.Format(culture, _strings["Minutes"], delay),
                        summary.MaxDepartureDelayMinutes is { } departure
                            ? string.Format(culture, _strings["StatDelaySub"], string.Format(culture, _strings["Minutes"], departure))
                            : null,
                        Palette.Delay(delay));
        _platform.IsVisible = summary.Stops > 0;
        _platform.Show(summary.OffPlatform.Count.ToString(culture),
                       summary.OffPlatform.Count > 0
                           ? string.Join(" · ", summary.OffPlatform.Select(p => string.Format(culture, _strings["StatPlatformAt"], p.Station, p.Metres)))
                           : _strings["StatPlatformNone"],
                       summary.OffPlatform.Count > 0 ? Palette.Bad : Palette.Good);
        _lights.IsVisible = summary.LightsJudgedSeconds > 0;
        _lights.Show(summary.WrongLightsSeconds > 0 ? Clock(summary.WrongLightsSeconds) : _strings["StatLightsRight"],
                     summary.LightsFaults.Count > 0
                         ? string.Join(" · ", summary.LightsFaults.Take(3).Select(f => LightsFaultText(recording, f, culture)))
                           + (summary.LightsFaults.Count > 3 ? " …" : "")
                         : string.Format(culture, _strings["StatLightsSub"], Clock(summary.LightsJudgedSeconds)),
                     summary.WrongLightsSeconds > 0 ? Palette.Bad : Palette.Good);
        _horn.IsVisible = summary.HornPoints > 0;
        _horn.Show($"{summary.HornPoints - summary.HornsMissed}/{summary.HornPoints}", string.Format(culture, _strings["StatHornSub"], summary.HornsMissed),
                   summary.HornsMissed > 0 ? Palette.Bad : Palette.Good);
        _acceleration.Show($"+{summary.MaxAcceleration:0.00} / −{summary.MaxDeceleration:0.00}",
                           string.Format(culture, _strings["StatAccelerationSub"], summary.MaxLateralAcceleration));
        _security.Show(summary.SecurityBrakings.ToString(culture), null, summary.SecurityBrakings > 0 ? Palette.Bad : Palette.Good);
        _state.Show(Clock(recording.Duration), _strings[recording.Complete ? "StatComplete" : "StatIncomplete"],
                    recording.Complete ? null : Palette.Warn);
        _fileState.Text = $"{Path.GetFileName(_path)}  ·  {Clock(recording.Duration)}  ·  {_strings[recording.Complete ? "StatComplete" : "StatIncomplete"]}";
    }

    private static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Round(seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
