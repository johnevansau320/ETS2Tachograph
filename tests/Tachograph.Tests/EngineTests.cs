using System.Text.Json;
using Tachograph.Core;
using Xunit;

namespace Tachograph.Tests;

public class EngineTests
{
    private static void Run(TachographEngine e, Activity a, int minutes, List<TachographWarning>? log = null)
    {
        var w = e.Advance(e.State.Minute + minutes, a);
        log?.AddRange(w);
    }

    private static TachographEngine Fresh()
    {
        var e = new TachographEngine();
        e.Advance(1000, Activity.Rest); // start
        return e;
    }

    [Fact]
    public void ContinuousDriving_WarnsThenViolatesAt4h30()
    {
        var e = Fresh(); var log = new List<TachographWarning>();
        Run(e, Activity.Driving, 270, log);
        var breaks = log.Where(w => w.Kind == WarningKind.Break).ToList();
        Assert.Equal(new[] { 30, 15, 5, 0 }, breaks.Select(w => w.RemainingMinutes));
        Assert.Equal(Severity.Violation, breaks.Last().Severity);
    }

    [Fact]
    public void FullBreak_ResetsContinuousDriving()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 200); Run(e, Activity.Rest, 45); Run(e, Activity.Driving, 1);
        Assert.Equal(1, e.GetStatus().DriveSinceBreak);
    }

    [Fact]
    public void SplitBreak_15Then30_Resets()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 200); Run(e, Activity.Rest, 15);
        Run(e, Activity.Driving, 60); Run(e, Activity.Rest, 30); Run(e, Activity.Driving, 1);
        Assert.Equal(1, e.GetStatus().DriveSinceBreak);
    }

    [Fact]
    public void SplitBreak_30Then15_DoesNotReset()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 200); Run(e, Activity.Rest, 30);
        Run(e, Activity.Driving, 30); Run(e, Activity.Rest, 15); Run(e, Activity.Driving, 1);
        Assert.Equal(231, e.GetStatus().DriveSinceBreak);
    }

    [Fact]
    public void ShortRest_DoesNotReset()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 100); Run(e, Activity.Rest, 14); Run(e, Activity.Driving, 1);
        Assert.Equal(101, e.GetStatus().DriveSinceBreak);
    }

    [Fact]
    public void DailyDriving_ExtensionInfoAt9h_ViolationAt10h()
    {
        var e = Fresh(); var log = new List<TachographWarning>();
        for (int i = 0; i < 10; i++) { Run(e, Activity.Driving, 60, log); Run(e, Activity.Rest, 45); }
        Assert.Contains(log, w => w.Kind == WarningKind.DailyDrivingExtension);
        Assert.Contains(log, w => w.Kind == WarningKind.DailyDriving && w.Severity == Severity.Violation);
    }

    [Fact]
    public void RegularDailyRest_ResetsDailyDriving_AndCountsExtension()
    {
        var e = Fresh();
        for (int i = 0; i < 6; i++) { Run(e, Activity.Driving, 100); Run(e, Activity.Rest, 45); } // 600 min
        Run(e, Activity.Rest, 660);
        Run(e, Activity.Driving, 1);
        Assert.Equal(1, e.GetStatus().DailyDriving);
        Assert.Equal(1, e.State.WeekExtensions.Values.Sum());
    }

    [Fact]
    public void ReducedDailyRest_9h_IsCounted()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 60); Run(e, Activity.Rest, 600); Run(e, Activity.Driving, 1);
        Assert.Equal(1, e.GetStatus().ReducedDailyRestsUsed);
        Assert.Equal(1, e.GetStatus().DailyDriving);
    }

    [Fact]
    public void SplitDailyRest_3Plus9_CountsAsRegular()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 200); Run(e, Activity.Rest, 200);
        Run(e, Activity.Driving, 60); Run(e, Activity.Rest, 560); Run(e, Activity.Driving, 1);
        Assert.Equal(0, e.GetStatus().ReducedDailyRestsUsed);
        Assert.Equal(1, e.GetStatus().DailyDriving);
    }

    [Fact]
    public void DailyRestDeadline_Is13hAfterPeriodStart()
    {
        var e = Fresh();
        Run(e, Activity.Work, 1);
        Assert.Equal(1440 - 660 - 1, e.GetStatus().DailyRestMustStartIn);
    }

    [Fact]
    public void WeeklyRest_24h_OwesCompensation_AndResetsCycle()
    {
        var e = Fresh();
        Run(e, Activity.Driving, 10); Run(e, Activity.Rest, 1440); Run(e, Activity.Work, 1);
        Assert.Equal(Rules.WeeklyRestRegular - 1440, e.GetStatus().CompensationOwedMinutes);
        Assert.Equal(Rules.WeeklyCycle - 1, e.GetStatus().WeeklyRestMustStartIn);
    }

    [Fact]
    public void FortnightSpansTwoWeeks()
    {
        var e = new TachographEngine();
        e.Advance(0, Activity.Rest);
        Run(e, Activity.Driving, 100);
        e.Advance(Rules.MinutesPerWeek, Activity.Rest);
        Run(e, Activity.Driving, 50);
        var st = e.GetStatus();
        Assert.Equal(50, st.WeekDriving);
        Assert.Equal(150, st.FortnightDriving);
    }

    [Fact]
    public void State_RoundTripsThroughJson()
    {
        var e = Fresh(); Run(e, Activity.Driving, 123);
        var json = JsonSerializer.Serialize(e.State);
        var e2 = new TachographEngine(JsonSerializer.Deserialize<TachographState>(json)!);
        Assert.Equal(e.GetStatus(), e2.GetStatus());
    }
}

public class HistoryTests
{
    private static TachographEngine Fresh()
    {
        var e = new TachographEngine();
        e.Advance(1000, Activity.Rest);
        return e;
    }

    [Fact]
    public void Log_MergesContiguousMinutes_AndSplitsOnChange()
    {
        var e = Fresh();
        e.Advance(1060, Activity.Driving);
        e.Advance(1090, Activity.Rest);
        e.Advance(1100, Activity.Rest);
        Assert.Equal(2, e.State.Log.Count);
        Assert.Equal((1000, 1060, Activity.Driving), (e.State.Log[0].Start, e.State.Log[0].End, e.State.Log[0].Activity));
        Assert.Equal((1060, 1100, Activity.Rest), (e.State.Log[1].Start, e.State.Log[1].End, e.State.Log[1].Activity));
    }

    [Fact]
    public void ForDay_ClipsSegmentsAtMidnight_AndTotals()
    {
        var e = new TachographEngine();
        e.Advance(1400, Activity.Rest);
        e.Advance(1500, Activity.Driving); // 40 min on day 0, 60 on day 1
        var d0 = HistoryReport.ForDay(e.State, 0);
        var d1 = HistoryReport.ForDay(e.State, 1);
        Assert.Equal(40, d0.Driving);
        Assert.Equal(60, d1.Driving);
        Assert.Equal(1500, d1.Segments.Last().End);
    }

    [Fact]
    public void Violations_AreRecorded_AndFoundByDay()
    {
        var e = Fresh();
        e.Advance(1000 + 271, Activity.Driving);
        Assert.Contains(e.State.Violations, v => v.Kind == WarningKind.Break);
        Assert.Single(HistoryReport.ForDay(e.State, 0).Violations, v => v.Kind == WarningKind.Break);
    }

    [Fact]
    public void GameTimeGoingBackwards_TruncatesLog()
    {
        var e = Fresh();
        e.Advance(1100, Activity.Driving);
        e.Advance(1050, Activity.Driving);
        Assert.Equal(1050, e.State.Log[^1].End);
    }

    [Fact]
    public void LogOlderThan28Days_IsPruned()
    {
        var e = new TachographEngine();
        e.Advance(0, Activity.Rest);
        e.Advance(30 * 1440, Activity.Work);
        Assert.True(e.State.Log[0].Start >= 2 * 1440);
    }

    [Fact]
    public void Log_RoundTripsThroughJson()
    {
        var e = Fresh();
        e.Advance(1100, Activity.Driving);
        var json = System.Text.Json.JsonSerializer.Serialize(e.State);
        var s2 = System.Text.Json.JsonSerializer.Deserialize<TachographState>(json)!;
        Assert.Equal(e.State.Log.Count, s2.Log.Count);
        Assert.Equal(Activity.Driving, s2.Log[0].Activity);
    }
}
