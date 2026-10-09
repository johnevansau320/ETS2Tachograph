namespace Tachograph.Core;

/// <summary>
/// Minute-stepped EU 561/2006 rules engine driven by in-game time.
/// Feed it with <see cref="Advance"/>; it returns any warnings raised in that span.
/// </summary>
public sealed class TachographEngine
{
    public TachographState State { get; }

    public TachographEngine(TachographState? state = null) => State = state ?? new TachographState();

    public IReadOnlyList<TachographWarning> Advance(int toMinute, Activity activity)
    {
        var warnings = new List<TachographWarning>();
        if (!State.Started)
        {
            State.Started = true;
            State.Minute = toMinute;
            State.PeriodStart = toMinute;
            State.WeeklyCycleStart = toMinute;
            State.Current = activity;
            return warnings;
        }
        if (toMinute < State.Minute) // game time went backwards (reload): drop log beyond the new time
        {
            State.Minute = toMinute;
            TruncateLog(toMinute);
            return warnings;
        }

        while (State.Minute < toMinute) Step(activity, warnings);
        State.Current = activity;
        foreach (var w in warnings.Where(w => w.Severity == Severity.Violation))
            State.Violations.Add(new ViolationRecord { Minute = w.GameMinute, Kind = w.Kind, Message = w.Message });
        return warnings;
    }

    private const int HistoryDays = 28;

    private void RecordMinute(Activity a)
    {
        var log = State.Log;
        int start = State.Minute - 1;
        if (log.Count > 0 && log[^1].End == start && log[^1].Activity == a) log[^1].End = State.Minute;
        else log.Add(new ActivityRecord { Start = start, End = State.Minute, Activity = a });

        if (State.Minute % 1440 == 0) // prune once a game day
        {
            int cutoff = State.Minute - HistoryDays * 1440;
            log.RemoveAll(r => r.End <= cutoff);
            if (log.Count > 0 && log[0].Start < cutoff) log[0].Start = cutoff;
            State.Violations.RemoveAll(v => v.Minute < cutoff);
        }
    }

    private void TruncateLog(int minute)
    {
        State.Log.RemoveAll(r => r.Start >= minute);
        foreach (var r in State.Log.Where(r => r.End > minute)) r.End = minute;
        State.Violations.RemoveAll(v => v.Minute > minute);
    }

    private void Step(Activity a, List<TachographWarning> warnings)
    {
        var before = Remaining(State.Current);
        var s = State;
        s.Minute++;
        int week = (s.Minute - 1) / Rules.MinutesPerWeek;
        RecordMinute(a);

        if (a == Activity.Rest)
        {
            s.CurrentRest++;
            OnRestMinute();
        }
        else
        {
            if (s.CurrentRest > 0) EndRestBlock(warnings);
            if (a == Activity.Driving)
            {
                s.DriveSinceBreak++;
                s.DailyDriving++;
                s.WeekDriving[week] = s.WeekDriving.GetValueOrDefault(week) + 1;
                if (s.DailyDriving == Rules.DailyDrivingNormal && ExtensionsUsed(week) < Rules.ExtensionsPerWeek)
                    warnings.Add(new(WarningKind.DailyDrivingExtension, Severity.Info, 0, s.Minute,
                        "Nine hours driven. Extension to ten hours is available."));
            }
        }

        var after = Remaining(a);
        foreach (var (kind, rem) in after)
        {
            int b = before[kind];
            int? hit = null;
            foreach (var t in Rules.WarningThresholds)
                if (b > t && rem <= t) hit = t; // keep the lowest threshold crossed
            if (hit is int th) warnings.Add(Describe(kind, th));
        }
    }

    private void OnRestMinute()
    {
        var s = State;
        int r = s.CurrentRest;
        if (r == Rules.BreakFull || (r == Rules.BreakSplitSecond && s.SplitFirstDone))
        {
            s.DriveSinceBreak = 0;
            s.SplitFirstDone = false;
        }
        if (r == Rules.DailyRestReduced)
        {
            s.DailyRestTaken = true;
            s.RegularViaSplit = s.Split3Done;
            int week = (s.Minute - 1) / Rules.MinutesPerWeek;
            if (s.DailyDriving > Rules.DailyDrivingNormal)
                s.WeekExtensions[week] = ExtensionsUsed(week) + 1;
            s.DailyDriving = 0;
            s.Split3Done = false;
            s.DriveSinceBreak = 0;
            s.SplitFirstDone = false;
        }
    }

    private void EndRestBlock(List<TachographWarning> warnings)
    {
        var s = State;
        int len = s.CurrentRest;

        if (len >= Rules.BreakSplitFirst && len < Rules.BreakFull && !s.SplitFirstDone && s.DriveSinceBreak > 0)
            s.SplitFirstDone = true;

        if (len >= Rules.DailyRestSplitFirst && len < Rules.DailyRestReduced)
            s.Split3Done = true;

        if (s.DailyRestTaken)
        {
            if (len < Rules.DailyRestRegular && !s.RegularViaSplit && len < Rules.WeeklyRestReduced)
            {
                s.ReducedDailyCount++;
                if (s.ReducedDailyCount > Rules.ReducedRestsAllowed)
                    warnings.Add(new(WarningKind.ReducedDailyRestsExceeded, Severity.Violation, 0, s.Minute,
                        "More than three reduced daily rests since the last weekly rest."));
            }
            s.PeriodStart = s.Minute - 1;
        }

        if (len >= Rules.WeeklyRestReduced)
        {
            if (len < Rules.WeeklyRestRegular)
                s.CompensationOwedMinutes += Rules.WeeklyRestRegular - len;
            s.WeeklyCycleStart = s.Minute - 1;
            s.ReducedDailyCount = 0;
            s.PeriodStart = s.Minute - 1;
        }

        s.CurrentRest = 0;
        s.DailyRestTaken = false;
        s.RegularViaSplit = false;
    }

    private int ExtensionsUsed(int week) => State.WeekExtensions.GetValueOrDefault(week);

    private Dictionary<WarningKind, int> Remaining(Activity a)
    {
        var s = State;
        int week = Math.Max(0, (s.Minute - 1) / Rules.MinutesPerWeek);
        bool resting = a == Activity.Rest;
        int dailyLimit = ExtensionsUsed(week) < Rules.ExtensionsPerWeek
            ? Rules.DailyDrivingExtended : Rules.DailyDrivingNormal;
        int wk = s.WeekDriving.GetValueOrDefault(week);
        int fn = wk + s.WeekDriving.GetValueOrDefault(week - 1);
        return new()
        {
            [WarningKind.Break] = Rules.ContinuousDriving - s.DriveSinceBreak,
            [WarningKind.DailyDriving] = dailyLimit - s.DailyDriving,
            [WarningKind.WeeklyDriving] = Rules.WeeklyDriving - wk,
            [WarningKind.FortnightDriving] = Rules.FortnightDriving - fn,
            [WarningKind.DailyRest] = resting ? int.MaxValue
                : s.PeriodStart + Rules.DailyPeriod - Rules.DailyRestRegular - s.Minute,
            [WarningKind.WeeklyRest] = resting ? int.MaxValue
                : s.WeeklyCycleStart + Rules.WeeklyCycle - s.Minute,
        };
    }

    public TachographStatus GetStatus()
    {
        var s = State;
        int week = Math.Max(0, (s.Minute - 1) / Rules.MinutesPerWeek);
        var rem = Remaining(s.Current);
        int wk = s.WeekDriving.GetValueOrDefault(week);
        bool ext = ExtensionsUsed(week) < Rules.ExtensionsPerWeek;
        return new(s.Minute, s.Current,
            s.DriveSinceBreak, rem[WarningKind.Break],
            s.DailyDriving, rem[WarningKind.DailyDriving], ext,
            wk, rem[WarningKind.WeeklyDriving],
            wk + s.WeekDriving.GetValueOrDefault(week - 1), rem[WarningKind.FortnightDriving],
            s.PeriodStart + Rules.DailyPeriod - Rules.DailyRestRegular - s.Minute,
            s.WeeklyCycleStart + Rules.WeeklyCycle - s.Minute,
            s.CurrentRest, s.CompensationOwedMinutes, s.ReducedDailyCount);
    }

    private TachographWarning Describe(WarningKind kind, int t)
    {
        string what = kind switch
        {
            WarningKind.Break => "driving before a 45 minute break is required",
            WarningKind.DailyDriving => "daily driving time left",
            WarningKind.WeeklyDriving => "weekly driving time left",
            WarningKind.FortnightDriving => "fortnightly driving time left",
            WarningKind.DailyRest => "before you must start your daily rest",
            WarningKind.WeeklyRest => "before you must start your weekly rest",
            _ => kind.ToString()
        };
        if (t == 0)
            return new(kind, Severity.Violation, 0, State.Minute, $"Limit reached: {what}. Stop now.");
        return new(kind, t <= 5 ? Severity.Warning : Severity.Info, t, State.Minute, $"{t} minutes {what}.");
    }
}
