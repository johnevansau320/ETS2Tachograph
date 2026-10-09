using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tachograph.Core;

namespace Tachograph.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly TachographEngine _engine = new(SessionStore.Load());
    private readonly TachographSession _session;
    private readonly TelemetryClient _client;
    private readonly SpeechSynthesizer _voice = new();
    private readonly ChimePlayer _chime = new();
    public static string Version { get; } =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?")
        .Split('+')[0];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private HotkeyManager? _hotkeys;
    private HistoryWindow? _history;
    private bool _polling;
    private int _ticks;

    private static readonly Brush Blue = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xA9, 0xF5)));
    private static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public MainWindow()
    {
        InitializeComponent();
        _session = new TachographSession(_engine, new ActivityMapper(_settings.Mapper));
        _client = new TelemetryClient(_settings.ServerUrl);
        ApplyVoice();

        SpeechItem.IsChecked = _settings.SpeechEnabled;
        TopItem.IsChecked = Topmost = _settings.AlwaysOnTop;
        VersionItem.Header = $"ETS2 Tachograph v{Version}";
        SetWidth(_settings.WindowWidth);
        RestorePosition();
        UpdateHotkeyHint();

        _timer.Tick += async (_, _) => await PollAsync();
        _bannerTimer.Tick += (_, _) => { Banner.Visibility = Visibility.Collapsed; _bannerTimer.Stop(); };
        Loaded += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(this);
            _hotkeys = new HotkeyManager(src);
            _hotkeys.Pressed += OnHotkey;
            var failed = HotkeysWindow.RegisterAll(_hotkeys, _settings);
            if (failed.Count > 0)
                StateText.Text = $"Hotkey unavailable: {failed[0]}" + (failed.Count > 1 ? $" (+{failed.Count - 1} more)" : "");
            _timer.Start();
            if (_settings.CheckUpdatesAtStartup) _ = CheckForUpdateAsync(manual: false);
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            _history?.Close();
            _hotkeys?.Dispose();
            SessionStore.Save(_engine.State);
            if (WindowState == WindowState.Normal) { _settings.WindowLeft = Left; _settings.WindowTop = Top; }
            _settings.Save();
            _client.Dispose();
            _chime.Dispose();
            _voice.Dispose();
        };
        Refresh(connected: false);
    }

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var snap = await _client.PollAsync();
            if (snap is null || !snap.Connected)
            {
                Refresh(false, snap is null ? "Telemetry server not found" : "Waiting for game");
                return;
            }
            foreach (var w in _session.Update(snap)) Announce(w);
            Refresh(true, snap.Paused ? "Game paused" : "Recording");
            if (++_ticks % 20 == 0) SessionStore.Save(_engine.State);
        }
        finally { _polling = false; }
    }

    private void Announce(TachographWarning w) =>
        Notify(w.Message, w.Severity switch { Severity.Violation => Red, Severity.Warning => Amber, _ => Blue },
            speak: true, chime: w.Severity);

    private void ApplyVoice()
    {
        _voice.Volume = Math.Clamp(_settings.SpeechVolume, 0, 100);
        _voice.Rate = Math.Clamp(_settings.SpeechRate, -10, 10);
    }

    /// <summary>Shows the banner for ten seconds, optionally with a chime and then speech.</summary>
    private void Notify(string text, Brush background, bool speak = false, bool interrupt = false, Severity? chime = null)
    {
        Banner.Background = background;
        BannerText.Text = text;
        Banner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Start();
        _ = SoundAsync(text, speak, interrupt, chime);
    }

    private async Task SoundAsync(string text, bool speak, bool interrupt, Severity? chime)
    {
        int delay = 0;
        if (chime is { } sev && _settings.ChimeEnabled && _settings.SpeechVolume > 0)
            delay = _chime.Play(sev, _settings.SpeechVolume / 100.0);
        if (!speak || !_settings.SpeechEnabled) return;
        if (interrupt) _voice.SpeakAsyncCancelAll();
        if (delay > 0) await Task.Delay(delay);
        _voice.SpeakAsync(text);
    }

    private void Refresh(bool connected, string? state = null)
    {
        var s = _engine.GetStatus();
        if (state is not null) StateText.Text = state;

        PicDriving.Visibility = Show(s.Activity == Activity.Driving);
        PicWork.Visibility = Show(s.Activity == Activity.Work);
        PicAvailability.Visibility = Show(s.Activity == Activity.Availability);
        PicRest.Visibility = Show(s.Activity == Activity.Rest);
        ActivityText.Text = s.Activity.ToString().ToUpperInvariant();

        ClockText.Text = _engine.State.Started ? StatusSpeech.DayAndTime(s.GameMinute) : "--- --:--";
        BreakBar.Value = Math.Clamp(s.DriveSinceBreak, 0, 270);
        BreakBar.Foreground = Gauge(s.BreakRemaining);
        BreakText.Text = s.BreakRemaining >= 0
            ? $"break in {StatusSpeech.Clock(s.BreakRemaining)}"
            : $"overdue {StatusSpeech.Clock(-s.BreakRemaining)}";

        Tile(DailyValue, s.DailyRemaining);
        Tile(WeekValue, s.WeekRemaining);
        Tile(FortValue, s.FortnightRemaining);

        RestDueText.Text = s.Activity == Activity.Rest ? $"resting {StatusSpeech.Clock(s.CurrentRest)}"
            : $"daily rest due in {StatusSpeech.Clock(s.DailyRestMustStartIn)}";
        ReducedText.Text = $"reduced rests {s.ReducedDailyRestsUsed}/3" + (s.ExtensionAvailable ? "  ·  10h ext." : "");
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void Tile(System.Windows.Controls.TextBlock t, int remaining)
    {
        t.Text = StatusSpeech.Clock(remaining);
        t.Foreground = remaining <= 0 ? Red : remaining <= 30 ? Amber : (Brush)FindResource("Ink");
    }

    private static Brush Gauge(int remaining) => remaining <= 5 ? Red : remaining <= 30 ? Amber : Blue;

    private void UpdateHotkeyHint()
    {
        var s = _settings.GetHotkey(HotkeyAction.SpeakStatus);
        HotkeyText.Text = string.IsNullOrEmpty(s) ? "" : $"Status: {s}";
    }

    // --- hotkey actions (usable in game, where the mouse isn't) ---
    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.SpeakStatus:
                StateText.Text = "Status requested";
                Notify(StatusSpeech.Spoken(_engine.GetStatus()), Blue, speak: true, interrupt: true);
                break;
            case HotkeyAction.SpeakToday:
                var day = HistoryReport.ForDay(_engine.State, HistoryReport.LastDay(_engine.State));
                Notify(StatusSpeech.SpokenDay(day), Blue, speak: true, interrupt: true);
                break;
            case HotkeyAction.ToggleHistory:
                ToggleHistory();
                break;
            case HotkeyAction.HistoryPrevDay:
                _history?.Step(-1);
                break;
            case HotkeyAction.HistoryNextDay:
                _history?.Step(+1);
                break;
            case HotkeyAction.ToggleSpeech:
                _settings.SpeechEnabled = !_settings.SpeechEnabled;
                SpeechItem.IsChecked = _settings.SpeechEnabled;
                _voice.SpeakAsyncCancelAll();
                Notify(_settings.SpeechEnabled ? "Spoken warnings on" : "Spoken warnings off", Blue,
                    speak: _settings.SpeechEnabled);
                break;
            case HotkeyAction.ToggleWindow:
                if (IsVisible) Hide(); else Show();
                break;
            case HotkeyAction.Silence:
                _voice.SpeakAsyncCancelAll();
                _chime.Stop();
                Banner.Visibility = Visibility.Collapsed;
                _bannerTimer.Stop();
                break;
        }
    }

    private void ToggleHistory()
    {
        if (_history is not null) { _history.Close(); return; }
        _history = new HistoryWindow(_engine.State) { Topmost = Topmost };
        _history.Closed += (_, _) => _history = null;
        _history.Show(); // ShowActivated=false in XAML, so the game keeps keyboard focus
    }

    // --- UI events ---
    // The instrument is designed at 460 x 300 and scales uniformly, so resizing keeps that aspect ratio.
    private const double DesignWidth = 460, DesignHeight = 300, MinInstrumentWidth = 300, MaxInstrumentWidth = 1400;

    private void SetWidth(double width)
    {
        width = Math.Clamp(double.IsFinite(width) ? width : DesignWidth, MinInstrumentWidth, MaxInstrumentWidth);
        Width = width;
        Height = width * DesignHeight / DesignWidth;
        _settings.WindowWidth = width;
    }

    /// <summary>Puts the window back where it was, pulled onto the desktop if that screen is gone.</summary>
    private void RestorePosition()
    {
        if (_settings.WindowLeft is not double left || _settings.WindowTop is not double top
            || !double.IsFinite(left) || !double.IsFinite(top)) return;

        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        const double stayVisible = 120; // at least this much of the window must remain on the desktop
        left = Math.Clamp(left, vl - Width + stayVisible, vl + vw - stayVisible);
        top = Math.Clamp(top, vt, vt + vh - stayVisible);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void Grip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // use whichever drag direction moved more, so diagonal drags feel natural
        double delta = Math.Abs(e.HorizontalChange) >= Math.Abs(e.VerticalChange * DesignWidth / DesignHeight)
            ? e.HorizontalChange : e.VerticalChange * DesignWidth / DesignHeight;
        SetWidth(Width + delta);
    }

    private void Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void History_Click(object sender, RoutedEventArgs e) => ToggleHistory();

    private void Speech_Click(object sender, RoutedEventArgs e) => _settings.SpeechEnabled = SpeechItem.IsChecked;

    private void Top_Click(object sender, RoutedEventArgs e) => Topmost = _settings.AlwaysOnTop = TopItem.IsChecked;

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_settings, ApplyVoice,
            () => Notify("Voice and chime test. Break required in 30 minutes.", Amber, speak: true, interrupt: true,
                chime: Severity.Warning), Version).Show();

    private void Update_Click(object sender, RoutedEventArgs e) => _ = CheckForUpdateAsync(manual: true);

    private async Task CheckForUpdateAsync(bool manual)
    {
        var r = await UpdateService.CheckAsync(Version);
        switch (r.Status)
        {
            case UpdateStatus.Available:
                if (!manual)
                {
                    Notify($"Version {r.Info!.Latest} is available. Right-click, then Check for updates.", Blue);
                    break;
                }
                if (MessageBox.Show(this, $"Version {r.Info!.Latest} is available (you have {Version}).\n\nOpen the download page in your browser?",
                        "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        string.IsNullOrEmpty(r.Info.Url) ? UpdateService.ReleasesPage : r.Info.Url) { UseShellExecute = true });
                break;
            case UpdateStatus.UpToDate when manual:
                MessageBox.Show(this, $"You have the latest version ({Version}).", "No update needed", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case UpdateStatus.NoReleases when manual:
                MessageBox.Show(this, "No releases have been published yet.", "No update found", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case UpdateStatus.Failed when manual:
                MessageBox.Show(this, r.Error ?? "The update check failed.", "Update check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => HistoryExporter.Save(this, _engine.State);

    private void Hotkeys_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeys is null) return;
        var w = new HotkeysWindow(_settings, _hotkeys) { Owner = this };
        w.Closed += (_, _) => UpdateHotkeyHint();
        w.Show();
    }
}
