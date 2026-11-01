using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StarterNG.Application;
using StarterNG.Domain.Settings;
using StarterNG.Domain.Tapes;

namespace StarterNG.Views.Tapes;

/// <summary>
/// Reads the speed recorder tapes the simulator writes to physicslog/ - one per
/// driven vehicle - grouped by the run they came from, and lays them out as Hasler
/// paper, fed by distance, or as the screen of an electronic recorder for vehicles
/// that have one, by time or by distance. The clock and the recorder come from
/// what the starter noted at the launch; both can be set by hand. While a session
/// runs "Live" goes to the tape of the vehicle the player is in and keeps
/// re-reading it.
/// </summary>
public sealed class HaslerTapeWindow : Window
{
    private const double DefaultKmScale = 40;
    private const double DefaultMinuteScale = 30;
    private const double MaxExportWidth = 32000;

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#41c400"));

    private readonly HaslerTapes _tapes = AppServices.Current.Tapes;
    private readonly TreeView _list = new() { Width = 260 };
    private readonly ComboBox _feed = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _scale = new() { Width = 140, Minimum = 4, Maximum = 600, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _recorder = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _live = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _startClock = new() { Width = 64, PlaceholderText = "hh:mm", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { Margin = new Thickness(8, 4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _cursor = new() { Margin = new Thickness(8, 4), FontSize = 12, Opacity = 0.85 };
    private readonly TextBlock _empty = new()
    {
        Margin = new Thickness(24),
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.75,
        IsVisible = false
    };
    private readonly ScrollViewer _scroll;
    private TapeView _view;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private TapeContext? _context;
    private readonly Control _recordingOff;
    private readonly Control _logOff;
    private readonly Dictionary<DateTime, bool> _expanded = new();

    private TapeFile? _shown;
    private string? _followed;
    private string _listed = "";
    private bool _running;
    private bool _loading;
    private bool _looking;

    /// <summary>A setting went on from here; the settings page's boxes have to follow it.</summary>
    public event Action? LogEnabled;

    public HaslerTapeWindow()
    {
        Title = App.Loc["HaslerTapes"];
        Width = 1100;
        Height = 560;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _feed.ItemsSource = new[] { App.Loc["HaslerByDistance"], App.Loc["HaslerByTime"] };
        _feed.SelectedIndex = 0;
        _feed.IsEnabled = false;
        _feed.SelectionChanged += (_, _) => FeedChanged();
        _scale.Value = DefaultKmScale;
        _scale.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                _view.Scale = _scale.Value;
        };
        _live.Content = App.Loc["HaslerLive"];
        ToolTip.SetShowOnDisabled(_live, true);
        _live.IsCheckedChanged += async (_, _) =>
        {
            if (_live.IsChecked == true)
                await RefreshListAsync(toDriven: true);
        };
        _timer.Tick += async (_, _) => await LookAtSessionsAsync();
        _startClock.TextChanged += (_, _) => _view.StartClock = TapeFigures.ParseClock(_startClock.Text);
        _recorder.ItemsSource = new[] { App.Loc["HaslerRecorderHasler"], App.Loc["HaslerRecorderElectronic"] };
        _recorder.SelectedIndex = 0;
        _recorder.SelectionChanged += (_, _) => RecorderChanged();
        ToolTip.SetTip(_startClock, App.Loc["HaslerStartClockTip"]);

        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += async (_, _) =>
        {
            // A session stands for its first tape, the player's own if it has one.
            if (_list.SelectedItem is TreeViewItem { Tag: TapeSession } session)
                session.IsExpanded = true;
            var file = _list.SelectedItem switch
            {
                TreeViewItem { Tag: TapeFile tape } => tape,
                TreeViewItem { Tag: TapeSession { Tapes.Count: > 0 } run } => run.Tapes[0],
                _ => null
            };
            if (file is null || file == _shown)
                return;

            ApplyContext(_tapes.ContextOf(file));
            await ShowAsync(file, scrollToEnd: false);
        };

        _view = NewView(RecorderKind.Hasler);
        _view.Scale = DefaultKmScale;
        _view.Hovered += ShowCursor;
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _view
        };

        _recordingOff = Strip("HaslerRecordingOff", "HaslerRecordingEnable", settings => settings.PhysicsLog = true);
        _logOff = Strip("HaslerLogOff", "HaslerLogEnable", settings => settings.DebugLog = true);

        var folder = ToolButton(App.Loc["HaslerOpenFolder"]);
        folder.Click += (_, _) =>
        {
            AppServices.Current.Files.CreateDirectory(_tapes.Folder);
            AppServices.Current.Processes.OpenInShell(_tapes.Folder);
        };

        var export = ToolButton(App.Loc["HaslerExportPng"]);
        export.Click += async (_, _) => await ExportPngAsync();

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
        foreach (var control in new Control[]
                 {
                     Label(App.Loc["HaslerRecorder"]), _recorder, Label(App.Loc["HaslerFeed"]), _feed,
                     Label(App.Loc["HaslerScale"]), _scale,
                     Label(App.Loc["HaslerStartClock"]), _startClock, _live, folder, export
                 })
        {
            control.Margin = new Thickness(4);
            toolbar.Children.Add(control);
        }

        var tapeArea = new DockPanel();
        DockPanel.SetDock(_recordingOff, Dock.Top);
        DockPanel.SetDock(_logOff, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(_cursor, Dock.Bottom);
        tapeArea.Children.Add(_recordingOff);
        tapeArea.Children.Add(_logOff);
        tapeArea.Children.Add(toolbar);
        tapeArea.Children.Add(_summary);
        tapeArea.Children.Add(_cursor);
        tapeArea.Children.Add(new Panel { Children = { _scroll, _empty } });

        var root = new DockPanel();
        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#3A424A")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _list
        };
        DockPanel.SetDock(listBorder, Dock.Left);
        root.Children.Add(listBorder);
        root.Children.Add(tapeArea);
        Content = root;

        Opened += async (_, _) =>
        {
            _timer.Start();
            await RefreshListAsync();
        };
        Activated += (_, _) => ShowWhatIsKnown();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// What gets recorded, and whether there is a trip for "live" to follow: a session
    /// that runs, and the player in a vehicle.
    /// </summary>
    private void ShowWhatIsKnown()
    {
        var settings = AppServices.Current.Settings;
        _recordingOff.IsVisible = !settings.PhysicsLog;
        _logOff.IsVisible = settings.PhysicsLog && !settings.DebugLog;

        bool canFollow = _running && _tapes.DrivenVehicle is not null;
        _live.IsEnabled = canFollow;
        if (!canFollow)
            _live.IsChecked = false;
        ToolTip.SetTip(_live, App.Loc[canFollow ? "HaslerLiveTip" : "HaslerLiveIdle"]);
    }

    private DockPanel Strip(string textKey, string buttonKey, Action<SimulatorSettings> enable)
    {
        var button = ToolButton(App.Loc[buttonKey]);
        button.Classes.Add("Accent");
        button.Margin = new Thickness(8, 0, 0, 0);
        button.Click += (_, _) => Enable(enable);
        DockPanel.SetDock(button, Dock.Right);
        return new DockPanel
        {
            Margin = new Thickness(8, 4),
            IsVisible = false,
            Children =
            {
                button,
                new TextBlock
                {
                    Text = App.Loc[textKey],
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    /// <summary>Only this one setting: other unsaved edits on the settings page are not this button's to save.</summary>
    private void Enable(Action<SimulatorSettings> enable)
    {
        var store = AppServices.Current.SettingsStore;
        enable(store.Settings);
        store.Save();
        LogEnabled?.Invoke();
        ShowWhatIsKnown();
        if (store.LastSaveFailed)
            _summary.Text = App.Loc["SettingsNotSaved"];
    }

    private static Button ToolButton(string text) => new()
    {
        Content = text,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
    };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private RecorderKind Recorder => _recorder.SelectedIndex == 1 ? RecorderKind.Electronic : RecorderKind.Hasler;

    private static TapeView NewView(RecorderKind kind) => kind == RecorderKind.Electronic
        ? new ElectronicTapeView { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
        : new HaslerTapeView { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

    /// <summary>
    /// The same trip drawn the other way. Hasler paper is fed by distance and
    /// nothing else; the screen of an electronic recorder runs on the clock, and
    /// may be switched to distance.
    /// </summary>
    private void RecorderChanged()
    {
        bool electronic = Recorder == RecorderKind.Electronic;
        _feed.IsEnabled = electronic;
        _feed.SelectedIndex = electronic ? 1 : 0;
        if ((_view is ElectronicTapeView) == electronic)
            return;

        var old = _view;
        old.Hovered -= ShowCursor;
        _view = NewView(Recorder);
        _view.CopySettingsFrom(old);
        _view.Hovered += ShowCursor;
        _scroll.Content = _view;
        // The other view is not as tall; an offset kept from it would hide its top rows.
        _scroll.Offset = new Vector(_scroll.Offset.X, 0);
        _cursor.Text = "";
    }

    private async Task RefreshListAsync(bool toDriven = false) =>
        ShowSessions(await Task.Run(_tapes.Sessions), toDriven);

    /// <summary>The tape of the vehicle the player is in, in the session that runs.</summary>
    private static TapeFile? DrivenTape(IEnumerable<TapeSession> sessions) =>
        sessions.FirstOrDefault(session => session.Running)?.Tapes
                .FirstOrDefault(tape => tape.Role is TapeRole.Driven or TapeRole.StartedIn);

    private static string Fingerprint(IEnumerable<TapeSession> sessions) =>
        string.Join('\n', sessions.Select(session =>
            $"{session.StartedUtc:O} {session.LastWrittenUtc:yyyyMMddHHmm} {session.Running} " +
            string.Join(' ', session.Tapes.Select(tape => $"{tape.Vehicle}:{tape.Role}"))));

    /// <summary>Sessions newest first, the newest one open until the reader has said otherwise.</summary>
    private void ShowSessions(IReadOnlyList<TapeSession> sessions, bool toDriven)
    {
        _running = sessions.Any(session => session.Running);
        _listed = Fingerprint(sessions);
        ShowWhatIsKnown();

        string? driven = DrivenTape(sessions)?.Path;
        if (toDriven)
            _followed = driven;
        string? selected = toDriven && driven is not null ? driven : _shown?.Path;

        foreach (var node in _list.Items.OfType<TreeViewItem>())
            _expanded[((TapeSession)node.Tag!).StartedUtc] = node.IsExpanded;
        _list.Items.Clear();

        (TreeViewItem Node, TreeViewItem Tape)? first = null, pick = null;
        for (int i = 0; i < sessions.Count; i++)
        {
            var node = SessionItem(sessions[i], _expanded.GetValueOrDefault(sessions[i].StartedUtc, i == 0));
            _list.Items.Add(node);
            foreach (var tape in node.Items.OfType<TreeViewItem>())
            {
                first ??= (node, tape);
                if (((TapeFile)tape.Tag!).Path == selected)
                    pick = (node, tape);
            }
        }

        _empty.Text = string.Format(App.Loc["HaslerNoTapes"], _tapes.Folder);
        _empty.IsVisible = first is null;
        _scroll.IsVisible = first is not null;
        if ((pick ?? first) is not { } chosen)
        {
            _shown = null;
            _view.Tape = null;
            _summary.Text = "";
            return;
        }

        chosen.Node.IsExpanded = true;
        _list.SelectedItem = chosen.Tape;
    }

    private static TreeViewItem SessionItem(TapeSession session, bool open)
    {
        string title = session.Launch is { } launch
            ? string.Join(" · ", new[] { string.IsNullOrWhiteSpace(launch.SceneryName) ? launch.Scenery : launch.SceneryName, launch.Train }
                                     .Where(part => !string.IsNullOrWhiteSpace(part)))
            : App.Loc["HaslerSession"];

        var started = session.StartedUtc.ToLocalTime();
        var written = session.LastWrittenUtc.ToLocalTime();
        string when = $"{started:dd.MM HH:mm}";
        if (!session.Running && written - started >= TimeSpan.FromMinutes(1))
            when += $"–{written:HH:mm}";
        if (session.Tapes.Count > 0)
            when += " · " + string.Format(App.Loc["HaslerSessionTapes"], session.Tapes.Count);

        var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        details.Children.Add(new TextBlock { Text = when, FontSize = 11, Opacity = 0.7 });
        if (session.Running)
            details.Children.Add(new TextBlock
            {
                Text = App.Loc["HaslerSessionRunning"],
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = RunningBrush
            });

        var name = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, title);

        var item = new TreeViewItem
        {
            Header = new StackPanel { Spacing = 1, Children = { name, details } },
            Tag = session,
            IsExpanded = open
        };
        foreach (var tape in session.Tapes)
            item.Items.Add(TapeItem(tape));
        return item;
    }

    private static TreeViewItem TapeItem(TapeFile file)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = file.Vehicle,
            FontWeight = file.Role == TapeRole.Other ? FontWeight.Normal : FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        string? role = file.Role switch
        {
            TapeRole.Driven => App.Loc["HaslerRoleDriven"],
            TapeRole.StartedIn => App.Loc["HaslerRoleStartedIn"],
            TapeRole.DrivenEarlier => App.Loc["HaslerRoleDrivenEarlier"],
            TapeRole.OwnConsist => App.Loc["HaslerRoleConsist"],
            _ => null
        };
        if (role is not null)
            text.Children.Add(new TextBlock { Text = role, FontSize = 11, Opacity = 0.7 });
        return new TreeViewItem { Header = text, Tag = file };
    }

    /// <summary>
    /// What the starter noted at the launch the tape came from: the clock the
    /// scenery started at and the recorder its vehicle has. A tape from before
    /// the starter noted anything keeps the hour unknown and the recorder guessed.
    /// </summary>
    private void ApplyContext(TapeContext context)
    {
        _context = context;
        _startClock.Text = context.StartClock is { } clock
            ? clock.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : "";
        _recorder.SelectedIndex = context.Recorder == RecorderKind.Electronic ? 1 : 0;
    }

    private async Task ShowAsync(TapeFile file, bool scrollToEnd)
    {
        if (_loading)
            return;
        _loading = true;
        try
        {
            var tape = await Task.Run(() => _tapes.Load(file));
            _shown = file;
            if (tape is null)
            {
                _view.Tape = null;
                _summary.Text = string.Format(App.Loc["HaslerUnreadable"], file.Path);
                return;
            }

            _view.Tape = tape;
            _summary.Text = string.Format(App.Loc["HaslerSummary"], file.Vehicle, TapeFigures.Clock(tape.Duration),
                                          TapeFigures.Clock(tape.RunningTime), TapeFigures.Clock(tape.StandingTime),
                                          tape.DistanceM / 1000.0, tape.MaxSpeedKmh, tape.AverageSpeedKmh,
                                          tape.AverageWithStopsKmh);
            if (_context?.Scenery is { } scenery)
                _summary.Text += " · " + string.Format(App.Loc["HaslerScenario"], scenery);
            if (_context?.Train is { } train)
                _summary.Text += " · " + string.Format(App.Loc["HaslerTrain"], train);
            // The log counts seconds from the start: an hour comes only from what the starter noted.
            if (string.IsNullOrWhiteSpace(_startClock.Text))
                _summary.Text += " · " + App.Loc["HaslerClockUnknown"];
            if (scrollToEnd)
                Dispatcher.UIThread.Post(() => _scroll.Offset = new Vector(double.MaxValue, _scroll.Offset.Y),
                                         DispatcherPriority.Background);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Keeps the list up with the sessions, and for a trip followed live re-reads the
    /// tape when the simulator has added to it. A change of vehicle or a new launch's
    /// own tape takes over; one picked by hand stays until then.
    /// </summary>
    private async Task LookAtSessionsAsync()
    {
        if (_loading || _looking)
            return;

        _looking = true;
        try
        {
            var sessions = await Task.Run(_tapes.Sessions);
            bool live = _live.IsChecked == true;
            bool newDriven = live && DrivenTape(sessions) is { } driven && driven.Path != _followed;
            if (newDriven || Fingerprint(sessions) != _listed)
            {
                ShowSessions(sessions, toDriven: newDriven);
                return;
            }

            if (!live || _shown is not { } shown)
                return;
            var now = sessions.SelectMany(session => session.Tapes).FirstOrDefault(tape => tape.Path == shown.Path);
            if (now is null || (now.Size == shown.Size && now.LastWriteUtc == shown.LastWriteUtc))
                return;

            // Following the end only when the reader is already there, so scrolling back is not undone.
            bool atEnd = _scroll.Offset.X + _scroll.Viewport.Width >= _scroll.Extent.Width - 4;
            await ShowAsync(now, scrollToEnd: atEnd);
        }
        finally
        {
            _looking = false;
        }
    }

    private void FeedChanged()
    {
        bool byDistance = _feed.SelectedIndex != 1;
        if (_view.ByDistance == byDistance)
            return;
        _view.ByDistance = byDistance;
        _scale.Value = byDistance ? DefaultKmScale : DefaultMinuteScale;
        _scale.Maximum = byDistance ? 600 : 300;
    }

    /// <summary>The whole tape, not just what is scrolled into view, drawn afresh at the chosen scale.</summary>
    private async Task ExportPngAsync()
    {
        if (_view.Tape is not { IsEmpty: false } tape || _shown is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = App.Loc["HaslerExportPng"],
            SuggestedFileName = _shown.Vehicle + ".png",
            DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
        });
        if (file is null)
            return;

        var copy = NewView(Recorder);
        copy.CopySettingsFrom(_view);
        // The trip it was asked for: a reload may have come in behind the file picker.
        copy.Tape = tape;
        copy.Measure(Size.Infinity);
        if (copy.DesiredSize.Width > MaxExportWidth)
        {
            // A bitmap that wide is past what the renderer takes; the paper is squeezed to fit instead.
            copy.Scale *= MaxExportWidth / copy.DesiredSize.Width;
            copy.Measure(Size.Infinity);
        }
        copy.Arrange(new Rect(copy.DesiredSize));

        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(copy.DesiredSize.Width),
                                                                    (int)Math.Ceiling(copy.DesiredSize.Height)));
            bitmap.Render(copy);
            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream);
        }
        catch (Exception ex)
        {
            AppServices.Current.Log.Log($"exporting {_shown.Path} to {file.Name}", ex);
            _summary.Text = string.Format(App.Loc["HaslerExportFailed"], file.Name);
        }
    }

    private void ShowCursor(TapeSample? sample)
    {
        if (sample is not { } s || _view.Tape is null)
        {
            _cursor.Text = "";
            return;
        }
        // Only what the record in view shows: a Hasler tape has no brake pipe and
        // marks current as on or off, and the electronic screen draws the current
        // and the vigilance alarm as channels, not values.
        var clock = TimeSpan.FromSeconds(_view.ClockAt(s) % 86400);
        string time = clock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        _cursor.Text = Recorder == RecorderKind.Electronic
            ? string.Format(App.Loc["ElectronicCursor"], time, s.DistanceM / 1000.0, s.SpeedKmh, s.PipePressBar,
                            s.BrakePressBar, YesNo(s.Current), YesNo(s.SecurityAlarm))
            : string.Format(App.Loc["HaslerCursor"], time, s.DistanceM / 1000.0, s.SpeedKmh, s.BrakePressBar,
                            YesNo(s.Current));
    }

    private static string YesNo(bool on) => on ? App.Loc["HaslerYes"] : App.Loc["HaslerNo"];
}
