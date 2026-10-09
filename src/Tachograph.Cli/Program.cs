using Tachograph.Core;

var engine = new TachographEngine(SessionStore.Load());
var session = new TachographSession(engine, new ActivityMapper());
using var client = new TelemetryClient();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("ETS2 Tachograph (console). Ctrl+C to quit.");
int ticks = 0;
while (!cts.IsCancellationRequested)
{
    var snap = await client.PollAsync(cts.Token);
    if (snap is null) Console.Write("\rWaiting for telemetry server...        ");
    else
    {
        foreach (var w in session.Update(snap)) Console.WriteLine($"\n[{w.Severity}] {w.Message}");
        var s = engine.GetStatus();
        Console.Write($"\r{s.Activity,-12} drive {s.DriveSinceBreak / 60}h{s.DriveSinceBreak % 60:00} | break in {s.BreakRemaining}m | day left {s.DailyRemaining}m | week left {s.WeekRemaining}m   ");
        if (++ticks % 20 == 0) SessionStore.Save(engine.State);
    }
    try { await Task.Delay(500, cts.Token); } catch (TaskCanceledException) { }
}
SessionStore.Save(engine.State);
