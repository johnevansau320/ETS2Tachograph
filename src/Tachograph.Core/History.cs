namespace Tachograph.Core;

/// <summary>A log span clipped to one day. Start/End are absolute game minutes.</summary>
public sealed record DaySegment(int Start, int End, Activity Activity);

public sealed record DaySummary(
    int Day,
    IReadOnlyList<DaySegment> Segments,
    int Driving, int Work, int Availability, int Rest,
    IReadOnlyList<ViolationRecord> Violations)
{
    /// <summary>Minutes with recorded data (the game wasn't running for the rest).</summary>
    public int Recorded => Driving + Work + Availability + Rest;
}

public sealed record WeekSummary(int Week, int Driving, int Work, int Availability, int Rest);

public static class HistoryReport
{
    public static int DayOf(int minute) => Math.Max(0, minute) / 1440;

    /// <summary>First game day that has any recorded data, or the current day if none.</summary>
    public static int FirstDay(TachographState s) =>
        s.Log.Count > 0 ? DayOf(s.Log[0].Start) : DayOf(s.Minute);

    public static int LastDay(TachographState s) => DayOf(Math.Max(0, s.Minute - 1));

    public static DaySummary ForDay(TachographState s, int day)
    {
        int from = day * 1440, to = from + 1440;
        var segs = new List<DaySegment>();
        foreach (var r in s.Log)
        {
            int a = Math.Max(r.Start, from), b = Math.Min(r.End, to);
            if (b > a) segs.Add(new DaySegment(a, b, r.Activity));
        }
        var viol = s.Violations.Where(v => v.Minute > from && v.Minute <= to).ToList();
        return new DaySummary(day, segs, Sum(segs, Activity.Driving), Sum(segs, Activity.Work),
            Sum(segs, Activity.Availability), Sum(segs, Activity.Rest), viol);
    }

    public static WeekSummary ForWeek(TachographState s, int week)
    {
        int from = week * Rules.MinutesPerWeek, to = from + Rules.MinutesPerWeek;
        var segs = new List<DaySegment>();
        foreach (var r in s.Log)
        {
            int a = Math.Max(r.Start, from), b = Math.Min(r.End, to);
            if (b > a) segs.Add(new DaySegment(a, b, r.Activity));
        }
        return new WeekSummary(week, Sum(segs, Activity.Driving), Sum(segs, Activity.Work),
            Sum(segs, Activity.Availability), Sum(segs, Activity.Rest));
    }

    private static int Sum(List<DaySegment> segs, Activity a) =>
        segs.Where(x => x.Activity == a).Sum(x => x.End - x.Start);
}
