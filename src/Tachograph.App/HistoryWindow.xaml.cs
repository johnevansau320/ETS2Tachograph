using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Tachograph.Core;

namespace Tachograph.App;

public partial class HistoryWindow : Window
{
    private readonly TachographState _state;
    private int _day;

    // One lane per activity, top to bottom, like the lines on a tachograph chart.
    private static readonly Activity[] Lanes = { Activity.Driving, Activity.Work, Activity.Availability, Activity.Rest };
    private static readonly Dictionary<Activity, Brush> Colors = new()
    {
        [Activity.Driving] = Hex("#3FA9F5"),
        [Activity.Work] = Hex("#F5A623"),
        [Activity.Availability] = Hex("#A99BFF"),
        [Activity.Rest] = Hex("#4CC38A"),
    };

    private static Brush Hex(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public HistoryWindow(TachographState state)
    {
        InitializeComponent();
        _state = state;
        _day = HistoryReport.LastDay(state);
        Render();
    }

    public void Step(int delta)
    {
        int next = Math.Clamp(_day + delta, HistoryReport.FirstDay(_state), HistoryReport.LastDay(_state));
        if (next != _day) { _day = next; Render(); }
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Step(-1);


    private void Next_Click(object sender, RoutedEventArgs e) => Step(+1);


    private void Export_Click(object sender, RoutedEventArgs e) => HistoryExporter.Save(this, _state);

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void Render()
    {
        string[] names = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var d = HistoryReport.ForDay(_state, _day);
        DayTitle.Text = names[_day % 7];
        DaySub.Text = $"Week {_day / 7 + 1}  Â·  game day {_day + 1}"
            + (d.Recorded < 1440 ? $"  Â·  {StatusSpeech.Clock(d.Recorded)} recorded" : "");

        DrivingTotal.Text = StatusSpeech.Clock(d.Driving);
        WorkTotal.Text = StatusSpeech.Clock(d.Work);
        AvailTotal.Text = StatusSpeech.Clock(d.Availability);
        RestTotal.Text = StatusSpeech.Clock(d.Rest);

        var w = HistoryReport.ForWeek(_state, _day / 7);
        WeekLine.Text = $"Week {w.Week + 1}:  driving {StatusSpeech.Clock(w.Driving)} of 56:00   Â·   work {StatusSpeech.Clock(w.Work)}   Â·   rest {StatusSpeech.Clock(w.Rest)}";
        WeekBar.Text = w.Week > 0
            ? $"Fortnight driving: {StatusSpeech.Clock(w.Driving + HistoryReport.ForWeek(_state, w.Week - 1).Driving)} of 90:00"
            : "";

        ViolationsText.Text = d.Violations.Count == 0
            ? "None on this day."
            : string.Join(Environment.NewLine, d.Violations.Select(v =>
                $"{StatusSpeech.DayAndTime(v.Minute)}   {v.Message}"));
        ViolationsText.Foreground = d.Violations.Count == 0
            ? (Brush)FindResource("Dim") : Hex("#FF6B63");
        DrawChart();
    }

    private void DrawChart()
    {
        Chart.Children.Clear();
        double w = Chart.ActualWidth, h = Chart.Height;
        if (w < 50) return;

        const double left = 74, top = 10, bottom = 24;
        double plotW = w - left - 10, laneH = (h - top - bottom) / Lanes.Length;
        double X(int minuteOfDay) => left + plotW * minuteOfDay / 1440.0;

        // grid and hour labels
        for (int hr = 0; hr <= 24; hr += 3)
        {
            double x = X(hr * 60);
            Chart.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = h - bottom, Stroke = Hex("#1E2328"), StrokeThickness = 1 });
            var t = new TextBlock { Text = $"{hr:00}", FontSize = 11, Foreground = (Brush)FindResource("Dim") };
            Canvas.SetLeft(t, x - 7); Canvas.SetTop(t, h - bottom + 4);
            Chart.Children.Add(t);
        }

        // lane labels and baselines
        for (int i = 0; i < Lanes.Length; i++)
        {
            double y = top + i * laneH;
            var label = new TextBlock { Text = Lanes[i] == Activity.Availability ? "AVAIL" : Lanes[i].ToString().ToUpperInvariant(),
                FontSize = 10, Foreground = Colors[Lanes[i]] };
            Canvas.SetLeft(label, 8); Canvas.SetTop(label, y + laneH / 2 - 7);
            Chart.Children.Add(label);
            Chart.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = y + laneH, Y2 = y + laneH, Stroke = Hex("#1E2328"), StrokeThickness = 1 });
        }

        // activity bars, plus a connector line between lanes when the activity changes
        var d = HistoryReport.ForDay(_state, _day);
        int dayStart = _day * 1440;
        DaySegment? prev = null;
        foreach (var s in d.Segments)
        {
            int lane = Array.IndexOf(Lanes, s.Activity);
            double x1 = X(s.Start - dayStart), x2 = Math.Max(X(s.End - dayStart), x1 + 1.5);
            var bar = new Rectangle
            {
                Width = x2 - x1, Height = laneH - 8, Fill = Colors[s.Activity], RadiusX = 2, RadiusY = 2,
                ToolTip = $"{s.Activity}  {StatusSpeech.Clock(s.Start - dayStart)}â€“{StatusSpeech.Clock(s.End - dayStart)}  ({StatusSpeech.Clock(s.End - s.Start)})"
            };
            Canvas.SetLeft(bar, x1); Canvas.SetTop(bar, top + lane * laneH + 4);
            Chart.Children.Add(bar);

            if (prev is not null && prev.End == s.Start)
            {
                int pl = Array.IndexOf(Lanes, prev.Activity);
                Chart.Children.Add(new Line
                {
                    X1 = x1, X2 = x1, Y1 = top + pl * laneH + laneH / 2, Y2 = top + lane * laneH + laneH / 2,
                    Stroke = Hex("#5B6670"), StrokeThickness = 1
                });
            }
            prev = s;
        }

        // infringement markers
        foreach (var v in d.Violations)
        {
            double x = X(v.Minute - dayStart);
            Chart.Children.Add(new Polygon
            {
                Points = new PointCollection { new(x, top - 2), new(x - 5, top - 10), new(x + 5, top - 10) },
                Fill = Hex("#FF6B63"), ToolTip = v.Message
            });
        }

        if (d.Segments.Count == 0)
        {
            var none = new TextBlock { Text = "No data recorded for this day", FontSize = 13, Foreground = (Brush)FindResource("Dim") };
            Canvas.SetLeft(none, left + plotW / 2 - 90); Canvas.SetTop(none, h / 2 - 10);
            Chart.Children.Add(none);
        }
    }
}
