using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Eu07.BlackBox.Views;
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
    private readonly Dictionary<string, BlackBoxWindow> _blackBoxes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _liveBlackBoxes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TapeSession> _sessions = Array.Empty<TapeSession>();
    private readonly Dictionary<string, double> _wantedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<TapeSample> _picked;

    private TapeFile? _shown;
    /// <summary>The session picked that has no tape, so that a refresh of the list goes back to it and not to a tape of another.</summary>
    private DateTime? _tapeless;
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
            if (e.Property != RangeBase.ValueProperty)
                return;
            _view.Scale = _scale.Value;
            ShowMark(scrollTo: false);
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
        _list.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is TreeViewItem { Tag: BlackBoxRecording box })
            {
                e.Handled = true;
                await OpenBlackBoxAsync(box);
            }
        };
        _list.SelectionChanged += async (_, _) =>
        {
            // A session stands for its first tape, the player's own if it has one.
            if (_list.SelectedItem is TreeViewItem { Tag: TapeSession } node)
                node.IsExpanded = true;
            // A black box opens in a window of its own on a click or Enter, not when the arrows go over it; the
            // tape beside it is one of its session's, the one shown if it is.
            var session = _list.SelectedItem switch
            {
                TreeViewItem { Tag: TapeSession run } => run,
                TreeViewItem { Tag: BlackBoxRecording box } =>
                    _sessions.FirstOrDefault(s => s.BlackBoxes.Any(b => b.Path == box.Path)),
                _ => null
            };
            if (_list.SelectedItem is TreeViewItem { Tag: BlackBoxRecording } && session?.Tapes.Any(t => t.Path == _shown?.Path) == true)
                return;
            var file = _list.SelectedItem is TreeViewItem { Tag: TapeFile tape } ? tape : session?.Tapes.FirstOrDefault();
            if (file is null)
            {
                if (session is not null)
                    ShowNoTape(session);
                return;
            }
            if (file == _shown)
                return;

            ApplyContext(_tapes.ContextOf(file));
            await ShowAsync(file, scrollToEnd: false);
        };

        _view = NewView(RecorderKind.Hasler);
        _view.Scale = DefaultKmScale;
        _view.Hovered += ShowCursor;
        _picked = async sample => await PickOnTapeAsync(sample);
        _view.Picked += _picked;
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
        old.Picked -= _picked;
        _view = NewView(Recorder);
        _view.CopySettingsFrom(old);
        _view.Hovered += ShowCursor;
        _view.Picked += _picked;
        _scroll.Content = _view;
        // The other view is not as tall; an offset kept from it would hide its top rows.
        _scroll.Offset = new Vector(_scroll.Offset.X, 0);
        _cursor.Text = "";
        ShowMark(scrollTo: false);
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
            string.Join(' ', session.Tapes.Select(tape => $"{tape.Vehicle}:{tape.Role}")) + " " +
            string.Join(' ', session.BlackBoxes.Select(box => box.Path))));

    /// <summary>Sessions newest first, the newest one open until the reader has said otherwise.</summary>
    private void ShowSessions(IReadOnlyList<TapeSession> sessions, bool toDriven)
    {
        _running = sessions.Any(session => session.Running);
        _sessions = sessions;
        _listed = Fingerprint(sessions);
        ShowWhatIsKnown();

        string? driven = DrivenTape(sessions)?.Path;
        if (toDriven)
            _followed = driven;
        string? selected = toDriven && driven is not null ? driven : _shown?.Path;
        DateTime? tapeless = toDriven && driven is not null ? null : _tapeless;
        FollowBlackBoxes(sessions);

        foreach (var node in _list.Items.OfType<TreeViewItem>())
            _expanded[((TapeSession)node.Tag!).StartedUtc] = node.IsExpanded;
        _list.Items.Clear();

        (TreeViewItem Node, TreeViewItem Tape)? first = null, pick = null;
        TreeViewItem? again = null;
        for (int i = 0; i < sessions.Count; i++)
        {
            var node = SessionItem(sessions[i], _expanded.GetValueOrDefault(sessions[i].StartedUtc, i == 0));
            _list.Items.Add(node);
            if (sessions[i].StartedUtc == tapeless)
                again = node;
            foreach (var tape in node.Items.OfType<TreeViewItem>().Where(item => item.Tag is TapeFile))
            {
                first ??= (node, tape);
                if (((TapeFile)tape.Tag!).Path == selected)
                    pick = (node, tape);
            }
        }

        _empty.Text = string.Format(App.Loc["HaslerNoTapes"], _tapes.Folder);
        _empty.IsVisible = first is null;
        _scroll.IsVisible = first is not null;
        if (pick is null && again is not null)
        {
            // The session picked had no tape; picked again, it shows the tape it has now, or still none.
            _list.SelectedItem = again;
            return;
        }
        if ((pick ?? first) is not { } chosen)
        {
            _shown = null;
            _view.Tape = null;
            _view.Mark = null;
            _summary.Text = "";
            return;
        }

        chosen.Node.IsExpanded = true;
        _list.SelectedItem = chosen.Tape;
    }

    private TreeViewItem SessionItem(TapeSession session, bool open)
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
        foreach (var box in session.BlackBoxes)
            item.Items.Add(BlackBoxItem(box));
        foreach (var tape in session.Tapes)
            item.Items.Add(TapeItem(tape));
        return item;
    }

    private TreeViewItem BlackBoxItem(BlackBoxRecording box)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = App.Loc["HaslerBlackBox"], FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = $"{box.LastWriteUtc.ToLocalTime():HH:mm} · {box.Size / (1024.0 * 1024.0):0.0} MB",
            FontSize = 11,
            Opacity = 0.7
        });
        ToolTip.SetTip(text, App.Loc["HaslerBlackBoxTip"]);
        var item = new TreeViewItem { Header = text, Tag = box };
        // Also when it is the one picked already, and its window was closed since.
        item.Tapped += async (_, _) => await OpenBlackBoxAsync(box);
        return item;
    }

    /// <summary>
    /// The black box in its window, read on while the session runs; one window a file. Given a moment, it opens
    /// there. Where it plays is marked on a tape of the same session.
    /// </summary>
    private async Task OpenBlackBoxAsync(BlackBoxRecording box, double? at = null)
    {
        if (_blackBoxes.TryGetValue(box.Path, out var open))
        {
            if (at is { } moment)
                SeekBlackBox(box, open, moment);
            open.Activate();
            return;
        }
        var window = new BlackBoxWindow(new StarterBlackBoxStrings())
        {
            CanOpenFiles = false,
            Live = _liveBlackBoxes.Contains(box.Path)
        };
        _blackBoxes[box.Path] = window;
        if (at is { } first)
            _wantedAt[box.Path] = first;
        void Follow(double t) => FollowBlackBox(box, window, t);
        window.TimeChanged += Follow;
        window.Closed += (_, _) =>
        {
            window.TimeChanged -= Follow;
            _blackBoxes.Remove(box.Path);
            _wantedAt.Remove(box.Path);
            ShowMark(scrollTo: false);
        };
        window.Show(this);
        await window.OpenAsync(box.Path);
        // The last moment asked for while the file was read; not a window closed meanwhile, whose file another reads.
        if (_blackBoxes.TryGetValue(box.Path, out var current) && current == window
            && _wantedAt.Remove(box.Path, out double wanted))
            window.SeekTo(wanted);
    }

    /// <summary>A window still reading its file has no moment to go to yet: it goes there once it has read it.</summary>
    private void SeekBlackBox(BlackBoxRecording box, BlackBoxWindow window, double t)
    {
        if (window.Recording is null)
            _wantedAt[box.Path] = t;
        else
            window.SeekTo(t);
    }

    /// <summary>The session of the tape shown, with its black boxes; null for a tape no list has any more.</summary>
    private TapeSession? ShownSession =>
        _shown is { } shown
            ? _sessions.FirstOrDefault(session => session.Tapes.Any(tape => tape.Path == shown.Path))
            : null;

    /// <summary>
    /// A click on the tape takes the black box of its session to the same moment, opening it if no window has it.
    /// Both count seconds of the simulation from its start, so a tape's own time is the black box's. Also the click
    /// that brings this window back from the black box's: going back to the tape is to look at another moment.
    /// </summary>
    private async Task PickOnTapeAsync(TapeSample sample)
    {
        if (ShownSession is not { BlackBoxes.Count: > 0 } session)
            return;
        var open = session.BlackBoxes.Where(box => _blackBoxes.ContainsKey(box.Path)).ToList();
        if (open.Count == 0)
        {
            await OpenBlackBoxAsync(session.BlackBoxes[0], sample.Time);
            return;
        }
        foreach (var box in open)
            SeekBlackBox(box, _blackBoxes[box.Path], sample.Time);
    }

    /// <summary>Where a black box of the session shown plays, on its tape, brought into view as it moves on.</summary>
    private void FollowBlackBox(BlackBoxRecording box, BlackBoxWindow window, double t)
    {
        if (!_blackBoxes.TryGetValue(box.Path, out var open) || open != window
            || ShownSession?.BlackBoxes.Any(b => b.Path == box.Path) != true)
            return;
        // Played, it moves every frame: the tape is drawn again only when the mark moves on it.
        double? x = _view.XAt(t), was = _view.MarkX;
        if (x == was || (x is { } now && was is { } before && Math.Abs(now - before) < 0.5))
            return;
        _view.Mark = t;
        BringMarkIntoView();
    }

    /// <summary>
    /// The mark of an open black box of the session shown, or none: on a tape just shown, and on a view changed,
    /// where a mark held back while it stood still on the old one would stand wrong.
    /// </summary>
    private void ShowMark(bool scrollTo)
    {
        var window = ShownSession?.BlackBoxes.Select(box => _blackBoxes.GetValueOrDefault(box.Path))
                                  .FirstOrDefault(w => w?.Recording is not null);
        _view.Mark = window?.CurrentTime;
        if (scrollTo)
            BringMarkIntoView();
    }

    private void BringMarkIntoView()
    {
        if (_view.MarkX is not { } x)
            return;
        double left = _scroll.Offset.X, width = _scroll.Viewport.Width;
        if (width <= 0 || (x >= left + 16 && x <= left + width - 16))
            return;
        _scroll.Offset = new Vector(Math.Max(0, x - width / 2), _scroll.Offset.Y);
    }

    /// <summary>The newest black box of a running session is still written; once the session ends, its last part is read and that is all.</summary>
    private void FollowBlackBoxes(IReadOnlyList<TapeSession> sessions)
    {
        _liveBlackBoxes = sessions.Where(session => session.Running)
                                  .SelectMany(session => session.BlackBoxes.Take(1))
                                  .Select(box => box.Path)
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, window) in _blackBoxes)
        {
            if (!window.Live || _liveBlackBoxes.Contains(path))
                continue;
            window.ReadMore();
            window.Live = false;
        }
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

    /// <summary>A session with no tape, a black box at most: no tape of another session left beside it.</summary>
    private void ShowNoTape(TapeSession session)
    {
        _shown = null;
        _tapeless = session.StartedUtc;
        _view.Tape = null;
        _view.Mark = null;
        _cursor.Text = "";
        _summary.Text = App.Loc[session.BlackBoxes.Count > 0 ? "HaslerSessionNoTape" : "HaslerSessionEmpty"];
    }

    private async Task ShowAsync(TapeFile file, bool scrollToEnd)
    {
        if (_loading)
            return;
        _loading = true;
        try
        {
            var tape = await Task.Run(() => _tapes.Load(file));
            bool another = file.Path != _shown?.Path;
            _shown = file;
            _tapeless = null;
            if (tape is null)
            {
                _view.Tape = null;
                _view.Mark = null;
                _summary.Text = string.Format(App.Loc["HaslerUnreadable"], file.Path);
                return;
            }

            _view.Tape = tape;
            ShowMark(scrollTo: another && !scrollToEnd);
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
            if (ShownSession is { BlackBoxes.Count: > 0 })
                _summary.Text += " · " + App.Loc["HaslerClickBlackBox"];
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
        ShowMark(scrollTo: false);
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
        // The paper alone, not where a black box happens to be.
        copy.Mark = null;
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
