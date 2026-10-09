using System.Text;

namespace Tachograph.Core;

public static class HistoryExport
{
    private static readonly string[] Weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    /// <summary>One row per activity span (split at midnight) and per infringement, oldest first.</summary>
    public static string ToCsv(TachographState s)
    {
        var sb = new StringBuilder("Week,Day,Weekday,Start,End,Activity,Minutes,Note\r\n");
        if (s.Log.Count == 0) return sb.ToString();

        for (int day = HistoryReport.FirstDay(s); day <= HistoryReport.LastDay(s); day++)
        {
            var d = HistoryReport.ForDay(s, day);
            int start = day * 1440;
            foreach (var seg in d.Segments)
                sb.Append(Row(day, Time(seg.Start - start), Time(seg.End - start),
                    seg.Activity.ToString(), seg.End - seg.Start, ""));
            foreach (var v in d.Violations)
                sb.Append(Row(day, Time(v.Minute - start), "", "Infringement", 0, v.Message));
        }
        return sb.ToString();
    }

    private static string Time(int minuteOfDay) => $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    private static string Row(int day, string start, string end, string activity, int minutes, string note) =>
        string.Join(",", day / 7 + 1, day + 1, Weekdays[day % 7], start, end, activity, minutes, Escape(note)) + "\r\n";

    private static string Escape(string v) =>
        v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
