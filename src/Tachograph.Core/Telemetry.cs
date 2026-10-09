using System.Text.Json;

namespace Tachograph.Core;

public sealed record TelemetrySnapshot(
    bool Connected, bool Paused, int GameMinute, double SpeedKmh,
    bool EngineOn, bool ElectricOn, bool ParkBrakeOn, bool Ferry, bool Train)
{
    private static readonly DateTime Epoch = new(1, 1, 1); // ETS2 game time 0 = Monday 00:00

    public static TelemetrySnapshot? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var game = root.GetProperty("game");
            var truck = root.GetProperty("truck");
            var gameplay = root.TryGetProperty("gameplay", out var gp) ? gp : default;

            var time = game.GetProperty("time").GetDateTime();
            return new TelemetrySnapshot(
                game.GetProperty("connected").GetBoolean(),
                game.GetProperty("paused").GetBoolean(),
                (int)(time - Epoch).TotalMinutes,
                truck.GetProperty("speed").GetDouble(),
                truck.GetProperty("engineOn").GetBoolean(),
                truck.GetProperty("electricOn").GetBoolean(),
                truck.GetProperty("parkBrakeOn").GetBoolean(),
                Flag(gameplay, "ferry"), Flag(gameplay, "train"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

public sealed class MapperSettings
{
    /// <summary>Stationary with engine running stays Driving this long (junctions, traffic lights).</summary>
    public int DrivingHoldMinutes { get; set; } = 10;
    public double MovingThresholdKmh { get; set; } = 1.0;
    /// <summary>Ferry/train crossings: Rest when a bunk is available (Art. 9), otherwise Availability.</summary>
    public Activity TransportActivity { get; set; } = Activity.Rest;
}

/// <summary>Turns telemetry into a tachograph activity. Stateful because of the driving hold.</summary>
public sealed class ActivityMapper
{
    private readonly MapperSettings _s;
    private int _lastMovingMinute = int.MinValue;
    private bool _transportSeen;

    public ActivityMapper(MapperSettings? settings = null) => _s = settings ?? new();

    public Activity Map(TelemetrySnapshot t)
    {
        if (t.Ferry || t.Train) _transportSeen = true;
        if (t.SpeedKmh > _s.MovingThresholdKmh) { _transportSeen = false; _lastMovingMinute = t.GameMinute; return Activity.Driving; }

        if (_transportSeen) return _s.TransportActivity;
        if (!t.EngineOn && t.ParkBrakeOn) return Activity.Rest;
        if (t.GameMinute - _lastMovingMinute <= _s.DrivingHoldMinutes) return Activity.Driving;
        return Activity.Work;
    }
}

/// <summary>Glue: telemetry in, engine updated, warnings out. Pausing and disconnects freeze the clock.</summary>
public sealed class TachographSession
{
    public TachographEngine Engine { get; }
    private readonly ActivityMapper _mapper;

    public TachographSession(TachographEngine engine, ActivityMapper mapper)
    {
        Engine = engine;
        _mapper = mapper;
    }

    public IReadOnlyList<TachographWarning> Update(TelemetrySnapshot t)
    {
        if (!t.Connected || t.Paused || t.GameMinute <= 0) return Array.Empty<TachographWarning>();
        return Engine.Advance(t.GameMinute, _mapper.Map(t));
    }
}

public static class SessionStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ets2Tachograph", "state.json");

    public static TachographState Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<TachographState>(File.ReadAllText(path ?? DefaultPath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(TachographState state, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class TelemetryClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _url;

    public TelemetryClient(string baseUrl = "http://localhost:31377") => _url = baseUrl.TrimEnd('/') + "/api/ets2/telemetry";

    public async Task<TelemetrySnapshot?> PollAsync(CancellationToken ct = default)
    {
        try { return TelemetrySnapshot.Parse(await _http.GetStringAsync(_url, ct)); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    public void Dispose() => _http.Dispose();
}
