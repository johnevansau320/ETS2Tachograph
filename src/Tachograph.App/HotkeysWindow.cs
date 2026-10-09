using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tachograph.App;

/// <summary>Lets the user assign a global hotkey to each action. Built in code to keep it small.</summary>
public sealed class HotkeysWindow : Window
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF2));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x7C, 0x87, 0x91));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x3F, 0xA9, 0xF5));

    private readonly AppSettings _settings;
    private readonly HotkeyManager _manager;
    private readonly Dictionary<HotkeyAction, (TextBlock Label, Button Change)> _rows = new();
    private readonly TextBlock _message = new() { Foreground = Dim, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    private HotkeyAction? _capturing;

    public HotkeysWindow(AppSettings settings, HotkeyManager manager)
    {
        _settings = settings;
        _manager = manager;

        Title = "Hotkeys";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1B));
        FontFamily = new FontFamily("Bahnschrift, Segoe UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int row = 0;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = HotkeyInfo.Title(action), Foreground = Ink, FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
            var keys = new TextBlock { Foreground = Accent, FontSize = 14, Width = 150,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var change = Small("Change");
            var clear = Small("Clear");
            var a = action;
            change.Click += (_, _) => BeginCapture(a);
            clear.Click += (_, _) => Assign(a, "");

            Grid.SetRow(title, row); Grid.SetColumn(title, 0);
            Grid.SetRow(keys, row); Grid.SetColumn(keys, 1);
            Grid.SetRow(change, row); Grid.SetColumn(change, 2);
            Grid.SetRow(clear, row); Grid.SetColumn(clear, 3);
            grid.Children.Add(title); grid.Children.Add(keys); grid.Children.Add(change); grid.Children.Add(clear);
            _rows[action] = (keys, change);
            row++;
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _message.Text = "Click Change, then press the key combination. Esc cancels. Hotkeys work while the game has focus.";
        Grid.SetRow(_message, row); Grid.SetColumnSpan(_message, 4);
        grid.Children.Add(_message);

        Content = grid;
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => { _manager.UnregisterAll(); RefreshLabels(); }; // so existing combos can be re-captured
        Closed += (_, _) => RegisterAll(_manager, _settings);
    }

    public static List<string> RegisterAll(HotkeyManager manager, AppSettings settings)
    {
        var failed = new List<string>();
        foreach (var a in Enum.GetValues<HotkeyAction>())
        {
            var spec = settings.GetHotkey(a);
            if (!manager.Register(a, spec)) failed.Add($"{HotkeyInfo.Title(a)} ({spec})");
        }
        return failed;
    }

    private static Button Small(string text) => new()
    {
        Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0),
        Background = new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x2D)), Foreground = Ink,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x35, 0x3B))
    };

    private void RefreshLabels()
    {
        foreach (var (action, (label, change)) in _rows)
        {
            var spec = _settings.GetHotkey(action);
            bool capturing = _capturing == action;
            label.Text = capturing ? "Press keys…" : string.IsNullOrEmpty(spec) ? "not assigned" : spec;
            label.Foreground = capturing ? Ink : string.IsNullOrEmpty(spec) ? Dim : Accent;
            change.IsEnabled = !capturing;
        }
    }

    private void BeginCapture(HotkeyAction action)
    {
        _capturing = action;
        _message.Text = $"Press the new combination for “{HotkeyInfo.Title(action)}”. Esc cancels.";
        RefreshLabels();
        Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_capturing is not { } action) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None) return;

        _capturing = null;
        if (key == Key.Escape) { _message.Text = "Cancelled."; RefreshLabels(); return; }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            _message.Text = "Use at least one of Ctrl, Alt or Shift, so the key still works normally in the game.";
            RefreshLabels();
            return;
        }
        Assign(action, HotkeyManager.Format(mods, key));
    }

    private void Assign(HotkeyAction action, string spec)
    {
        _capturing = null;
        if (spec != "")
        {
            HotkeyAction? clash = Enum.GetValues<HotkeyAction>()
                .Where(a => a != action && string.Equals(_settings.GetHotkey(a), spec, StringComparison.OrdinalIgnoreCase))
                .Select(a => (HotkeyAction?)a)
                .FirstOrDefault();
            if (clash is { } other)
            {
                _message.Text = $"{spec} is already used for “{HotkeyInfo.Title(other)}”. Clear that one first.";
                RefreshLabels();
                return;
            }
            // Probe that Windows will accept it, then release it again until the window closes.
            if (!_manager.Register(action, spec))
            {
                _message.Text = $"{spec} is already taken by another program. Try a different combination.";
                RefreshLabels();
                return;
            }
            _manager.Unregister(action);
        }
        _settings.SetHotkey(action, spec);
        _settings.Save();
        _message.Text = spec == "" ? $"“{HotkeyInfo.Title(action)}” is now unassigned." : $"“{HotkeyInfo.Title(action)}” set to {spec}.";
        RefreshLabels();
    }
}
