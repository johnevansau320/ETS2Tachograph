namespace Tachograph.Core;

/// <summary>Driver activity as shown by the tachograph pictograms.</summary>
public enum Activity { Rest, Availability, Work, Driving }

public enum WarningKind
{
    Break,            // 4h30 continuous driving -> 45 min break
    DailyDriving,     // 9h (10h twice a week)
    DailyDrivingExtension,
    WeeklyDriving,    // 56h
    FortnightDriving, // 90h
    DailyRest,        // daily rest must start
    WeeklyRest,       // weekly rest must start
    ReducedDailyRestsExceeded
}

public enum Severity { Info, Warning, Violation }

public sealed record TachographWarning(
    WarningKind Kind, Severity Severity, int RemainingMinutes, int GameMinute, string Message);

/// <summary>EU Regulation 561/2006 limits, in minutes. Game time 0 is assumed to be Monday 00:00.</summary>
public static class Rules
{
    public const int MinutesPerWeek = 7 * 24 * 60;
    public const int ContinuousDriving = 270;     // Art. 7
    public const int BreakFull = 45;
    public const int BreakSplitFirst = 15;
    public const int BreakSplitSecond = 30;
    public const int DailyDrivingNormal = 540;    // Art. 6(1)
    public const int DailyDrivingExtended = 600;  // max twice per week
    public const int ExtensionsPerWeek = 2;
    public const int WeeklyDriving = 3360;        // 56h
    public const int FortnightDriving = 5400;     // 90h
    public const int DailyRestRegular = 660;      // 11h
    public const int DailyRestReduced = 540;      // 9h, max 3 between weekly rests
    public const int DailyRestSplitFirst = 180;   // 3h + 9h
    public const int ReducedRestsAllowed = 3;
    public const int DailyPeriod = 1440;          // rest must be completed within 24h
    public const int WeeklyRestRegular = 2700;    // 45h
    public const int WeeklyRestReduced = 1440;    // 24h, compensated
    public const int WeeklyCycle = 6 * 1440;      // weekly rest must start within 6 x 24h
    public static readonly int[] WarningThresholds = { 30, 15, 5, 0 };
}

/// <summary>Everything the engine needs to resume across sessions. JSON-serializable.</summary>
public sealed class TachographState
{
    public bool Started { get; set; }
    public int Minute { get; set; }
    public Activity Current { get; set; } = Activity.Rest;

    public int DriveSinceBreak { get; set; }
    public bool SplitFirstDone { get; set; }

    public int DailyDriving { get; set; }
    public int PeriodStart { get; set; }
    public bool Split3Done { get; set; }
    public int ReducedDailyCount { get; set; }

    public int CurrentRest { get; set; }
    public bool DailyRestTaken { get; set; }
    public bool RegularViaSplit { get; set; }

    public int WeeklyCycleStart { get; set; }
    public int CompensationOwedMinutes { get; set; }

    public Dictionary<int, int> WeekDriving { get; set; } = new();
    public Dictionary<int, int> WeekExtensions { get; set; } = new();

    /// <summary>Merged activity records, oldest first. Kept for 28 days like a driver card.</summary>
    public List<ActivityRecord> Log { get; set; } = new();
    public List<ViolationRecord> Violations { get; set; } = new();
}

/// <summary>A span of game minutes [Start, End) spent in one activity.</summary>
public sealed class ActivityRecord
{
    public int Start { get; set; }
    public int End { get; set; }
    public Activity Activity { get; set; }
}

public sealed class ViolationRecord
{
    public int Minute { get; set; }
    public WarningKind Kind { get; set; }
    public string Message { get; set; } = "";
}

public sealed record TachographStatus(
    int GameMinute, Activity Activity,
    int DriveSinceBreak, int BreakRemaining,
    int DailyDriving, int DailyRemaining, bool ExtensionAvailable,
    int WeekDriving, int WeekRemaining,
    int FortnightDriving, int FortnightRemaining,
    int DailyRestMustStartIn, int WeeklyRestMustStartIn,
    int CurrentRest, int CompensationOwedMinutes, int ReducedDailyRestsUsed);
