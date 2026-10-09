namespace Tachograph.Core;

/// <summary>Formats tachograph values for the display and for text-to-speech.</summary>
public static class StatusSpeech
{
    public static string Duration(int minutes)
    {
        minutes = Math.Max(0, minutes);
        int h = minutes / 60, m = minutes % 60;
        if (h == 0) return $"{m} minute{(m == 1 ? "" : "s")}";
        if (m == 0) return $"{h} hour{(h == 1 ? "" : "s")}";
        return $"{h} hour{(h == 1 ? "" : "s")} {m}";
    }

    /// <summary>Compact h:mm for the instrument display.</summary>
    public static string Clock(int minutes)
    {
        bool neg = minutes < 0;
        minutes = Math.Abs(minutes);
        return $"{(neg ? "-" : "")}{minutes / 60}:{minutes % 60:00}";
    }

    public static string Spoken(TachographStatus s)
    {
        var parts = new List<string> { s.Activity.ToString() + "." };
        parts.Add(s.BreakRemaining <= 0
            ? "Break overdue."
            : $"Break required in {Duration(s.BreakRemaining)}.");
        parts.Add($"Daily driving left, {Duration(s.DailyRemaining)}" +
                  (s.ExtensionAvailable && s.DailyRemaining > 60 ? ", including extension." : "."));
        parts.Add($"Weekly driving left, {Duration(s.WeekRemaining)}.");
        parts.Add($"Fortnight left, {Duration(s.FortnightRemaining)}.");
        if (s.Activity != Activity.Rest)
            parts.Add($"Daily rest must start within {Duration(s.DailyRestMustStartIn)}.");
        if (s.CompensationOwedMinutes > 0)
            parts.Add($"Rest compensation owed, {Duration(s.CompensationOwedMinutes)}.");
        return string.Join(" ", parts);
    }

    public static string SpokenDay(DaySummary d)
    {
        if (d.Recorded == 0) return "Nothing recorded today.";
        var parts = new List<string> { $"Today. Driving, {Duration(d.Driving)}." };
        if (d.Work > 0) parts.Add($"Work, {Duration(d.Work)}.");
        if (d.Availability > 0) parts.Add($"Availability, {Duration(d.Availability)}.");
        parts.Add($"Rest, {Duration(d.Rest)}.");
        if (d.Violations.Count > 0)
            parts.Add($"{d.Violations.Count} infringement{(d.Violations.Count == 1 ? "" : "s")} today.");
        return string.Join(" ", parts);
    }

    public static string DayAndTime(int gameMinute)
    {
        string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        int d = gameMinute / 1440 % 7, t = gameMinute % 1440;
        return $"{days[d]} {t / 60:00}:{t % 60:00}";
    }
}
