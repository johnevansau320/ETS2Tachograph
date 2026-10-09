using Tachograph.Core;
using Xunit;

namespace Tachograph.Tests;

public class TelemetryTests
{
    private static TelemetrySnapshot Snap(int min, double speed = 0, bool engine = true, bool brake = false,
        bool ferry = false, bool paused = false, bool connected = true) =>
        new(connected, paused, min, speed, engine, engine, brake, ferry, false);

    [Fact]
    public void Moving_IsDriving()
        => Assert.Equal(Activity.Driving, new ActivityMapper().Map(Snap(100, speed: 60)));

    [Fact]
    public void EngineOff_ParkBrake_IsRestImmediately()
        => Assert.Equal(Activity.Rest, new ActivityMapper().Map(Snap(100, engine: false, brake: true)));

    [Fact]
    public void StoppedWithEngineOn_HoldsDriving_ThenWork()
    {
        var m = new ActivityMapper();
        m.Map(Snap(100, speed: 50));
        Assert.Equal(Activity.Driving, m.Map(Snap(105)));
        Assert.Equal(Activity.Work, m.Map(Snap(111)));
    }

    [Fact]
    public void Ferry_UsesConfiguredActivity_UntilMoving()
    {
        var m = new ActivityMapper(new MapperSettings { TransportActivity = Activity.Availability });
        Assert.Equal(Activity.Availability, m.Map(Snap(100, ferry: true)));
        Assert.Equal(Activity.Availability, m.Map(Snap(110)));
        Assert.Equal(Activity.Driving, m.Map(Snap(120, speed: 40)));
    }

    [Fact]
    public void Session_IgnoresPausedAndDisconnected()
    {
        var s = new TachographSession(new TachographEngine(), new ActivityMapper());
        s.Update(Snap(1000));
        s.Update(Snap(1050, speed: 60, paused: true));
        s.Update(Snap(1050, speed: 60, connected: false));
        Assert.Equal(1000, s.Engine.State.Minute);
    }

    [Fact]
    public void Parse_ReadsServerPayload()
    {
        const string json = """
        {"game":{"connected":true,"time":"0001-01-02T03:04:00","paused":false},
         "truck":{"speed":72.5,"engineOn":true,"electricOn":true,"parkBrakeOn":false},
         "gameplay":{"ferry":false,"train":true}}
        """;
        var t = TelemetrySnapshot.Parse(json)!;
        Assert.Equal(1440 + 3 * 60 + 4, t.GameMinute);
        Assert.Equal(72.5, t.SpeedKmh);
        Assert.True(t.Train);
        Assert.False(t.Ferry);
    }

    [Fact]
    public void Parse_BadJson_ReturnsNull() => Assert.Null(TelemetrySnapshot.Parse("{}"));
}

public class SpeechTests
{
    [Fact]
    public void Duration_ReadsNaturally()
    {
        Assert.Equal("1 hour 49", StatusSpeech.Duration(109));
        Assert.Equal("2 hours", StatusSpeech.Duration(120));
        Assert.Equal("1 minute", StatusSpeech.Duration(1));
        Assert.Equal("0 minutes", StatusSpeech.Duration(-5));
    }

    [Fact]
    public void Clock_And_DayTime()
    {
        Assert.Equal("4:30", StatusSpeech.Clock(270));
        Assert.Equal("-0:05", StatusSpeech.Clock(-5));
        Assert.Equal("Tue 03:04", StatusSpeech.DayAndTime(1440 + 184));
    }
}

public class DaySpeechTests
{
    [Fact]
    public void SpokenDay_SummarisesActivities()
    {
        var e = new TachographEngine();
        e.Advance(100, Activity.Rest);
        e.Advance(280, Activity.Driving);
        e.Advance(340, Activity.Work);
        var text = StatusSpeech.SpokenDay(HistoryReport.ForDay(e.State, 0));
        Assert.Contains("Driving, 3 hours", text);
        Assert.Contains("Work, 1 hour", text);
        Assert.DoesNotContain("Availability", text);
    }

    [Fact]
    public void SpokenDay_EmptyDay() =>
        Assert.Equal("Nothing recorded today.", StatusSpeech.SpokenDay(HistoryReport.ForDay(new TachographState(), 0)));
}

public class ChimeAndExportTests
{
    [Theory]
    [InlineData(Severity.Info)]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Violation)]
    public void Chime_IsValidWav_AndMoreUrgentIsLonger(Severity sev)
    {
        var c = ChimeSynth.Create(sev);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(c.Wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(c.Wav, 8, 4));
        Assert.Equal(BitConverter.ToInt32(c.Wav, 4), c.Wav.Length - 8);
        Assert.InRange(c.DurationMs, 100, 1000);
    }

    [Fact]
    public void Chime_LengthGrowsWithSeverity() =>
        Assert.True(ChimeSynth.Create(Severity.Violation).DurationMs > ChimeSynth.Create(Severity.Warning).DurationMs
                    && ChimeSynth.Create(Severity.Warning).DurationMs > ChimeSynth.Create(Severity.Info).DurationMs);

    [Fact]
    public void Chime_ZeroVolume_IsSilent()
    {
        var c = ChimeSynth.Create(Severity.Warning, 0);
        Assert.All(c.Wav.Skip(44), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Csv_HasHeaderRowsAndInfringements()
    {
        var e = new TachographEngine();
        e.Advance(1000, Activity.Rest);
        e.Advance(1000 + 275, Activity.Driving); // runs past 4h30
        var csv = HistoryExport.ToCsv(e.State);
        var lines = csv.TrimEnd().Split("\r\n");
        Assert.Equal("Week,Day,Weekday,Start,End,Activity,Minutes,Note", lines[0]);
        Assert.Contains(lines, l => l.Contains(",Driving,275,"));
        Assert.Contains(lines, l => l.Contains(",Infringement,0,"));
    }

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        var s = new TachographState { Minute = 100 };
        s.Log.Add(new ActivityRecord { Start = 0, End = 100, Activity = Activity.Rest });
        s.Violations.Add(new ViolationRecord { Minute = 50, Kind = WarningKind.Break, Message = "Stop, \"now\"" });
        Assert.Contains("\"Stop, \"\"now\"\"\"", HistoryExport.ToCsv(s));
    }

    [Fact]
    public void Csv_Empty_IsHeaderOnly() =>
        Assert.Single(HistoryExport.ToCsv(new TachographState()).TrimEnd().Split("\r\n"));
}

public class UpdateCheckTests
{
    private const string Release = """{"tag_name":"v1.4.0","name":"Version 1.4.0","html_url":"https://github.com/x/y/releases/tag/v1.4.0"}""";

    [Fact]
    public void Parse_ReadsRelease()
    {
        var info = UpdateCheck.Parse(Release)!;
        Assert.Equal(new Version(1, 4, 0), info.Latest);
        Assert.Equal("Version 1.4.0", info.Name);
        Assert.EndsWith("v1.4.0", info.Url);
    }

    [Theory]
    [InlineData("1.3.0", true)]
    [InlineData("1.3.9", true)]
    [InlineData("1.4.0", false)]
    [InlineData("1.4.1", false)]
    [InlineData("1.10.0", false)]
    [InlineData("2.0.0", false)]
    [InlineData("1.3.0+abc123", true)]
    public void IsNewer_ComparesNumerically(string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(UpdateCheck.Parse(Release)!, current));

    [Fact]
    public void Versions_AreNotComparedAsText() =>
        Assert.True(UpdateCheck.IsNewer(UpdateCheck.Parse("""{"tag_name":"v1.10.0"}""")!, "1.9.0"));

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"message":"Not Found"}""")]
    public void Parse_RejectsUnusableResponses(string json) => Assert.Null(UpdateCheck.Parse(json));
}
