using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tachograph.Core;

namespace Tachograph.App;

/// <summary>Tuning options. Changes apply immediately and are saved when the window closes.</summary>
public sealed class SettingsWindow : Window
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF2));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x7C, 0x87, 0x91));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x3F, 0xA9, 0xF5));

    public SettingsWindow(AppSettings s, Action applyVoice, Action testVoice, string version)
    {
        Title = "Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1B));
        FontFamily = new FontFamily("Bahnschrift, Segoe UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(Heading("Activity detection"));
        AddSlider(panel, "Traffic-light hold", 0, 60, s.Mapper.DrivingHoldMinutes,
            v => s.Mapper.DrivingHoldMinutes = v,
            v => $"{v} game min  (about {v * 60 / 19} s real time at 19×)",
            "A stop with the engine running keeps counting as Driving for this long, then becomes Work.");
        AddCheck(panel, "Ferry / train crossings count as Availability (instead of Rest)",
            s.Mapper.TransportActivity == Activity.Availability,
            on => s.Mapper.TransportActivity = on ? Activity.Availability : Activity.Rest);

        panel.Children.Add(Heading("Voice and alerts"));
        AddSlider(panel, "Volume", 0, 100, s.SpeechVolume, v => { s.SpeechVolume = v; applyVoice(); }, v => $"{v}%");
        AddSlider(panel, "Speaking speed", -5, 5, s.SpeechRate, v => { s.SpeechRate = v; applyVoice(); },
            v => v == 0 ? "normal" : v > 0 ? $"+{v} faster" : $"{v} slower");
        AddCheck(panel, "Play a chime before spoken warnings", s.ChimeEnabled, on => s.ChimeEnabled = on);

        panel.Children.Add(Heading("Updates"));
        AddCheck(panel, "Check GitHub for a newer version when the app starts", s.CheckUpdatesAtStartup, on => s.CheckUpdatesAtStartup = on);

        var test = new Button
        {
            Content = "Test voice and chime", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x2D)),
            Foreground = Ink, BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x35, 0x3B))
        };
        test.Click += (_, _) => testVoice();
        panel.Children.Add(test);

        panel.Children.Add(new TextBlock
        {
            Text = $"ETS2 Tachograph  v{version}", Foreground = Dim, FontSize = 12, Margin = new Thickness(0, 18, 0, 0)
        });

        Content = panel;
        Closed += (_, _) => s.Save();
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(), Foreground = Accent, FontSize = 11, Margin = new Thickness(0, 14, 0, 6)
    };

    private static void AddSlider(Panel parent, string label, int min, int max, int value,
        Action<int> set, Func<int, string> format, string? hint = null)
    {
        var header = new Grid();
        header.Children.Add(new TextBlock { Text = label, Foreground = Ink, FontSize = 14 });
        var shown = new TextBlock { Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
        header.Children.Add(shown);

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, SmallChange = 1, LargeChange = 5,
            TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0)
        };
        shown.Text = format(value);
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            shown.Text = format(v);
            set(v);
        };

        parent.Children.Add(header);
        parent.Children.Add(slider);
        if (hint is not null)
            parent.Children.Add(new TextBlock { Text = hint, Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        parent.Children.Add(new Border { Height = 8 });
    }

    private static void AddCheck(Panel parent, string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Foreground = Ink, FontSize = 14, Margin = new Thickness(0, 4, 0, 4) };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        parent.Children.Add(box);
    }
}
