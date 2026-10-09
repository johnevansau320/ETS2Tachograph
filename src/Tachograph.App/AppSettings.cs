using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tachograph.Core;

namespace Tachograph.App;

public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "http://localhost:31377";
    public bool SpeechEnabled { get; set; } = true;
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
    public bool ChimeEnabled { get; set; } = true;
    /// <summary>Off by default: the check contacts GitHub, so the user opts in.</summary>
    public bool CheckUpdatesAtStartup { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>Width of the instrument window; the height follows the fixed aspect ratio.</summary>
    public double WindowWidth { get; set; } = 460;
    /// <summary>Last screen position of the instrument window; null until it has been closed once.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public MapperSettings Mapper { get; set; } = new();

    /// <summary>Action name to key combination. An empty string means "unassigned".</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    /// <summary>Single hotkey from version 1.0; migrated into <see cref="Hotkeys"/> on load.</summary>
    [JsonPropertyName("Hotkey")] public string? LegacyHotkey { get; set; }

    public string GetHotkey(HotkeyAction a) => Hotkeys.TryGetValue(a.ToString(), out var v) ? v : "";
    public void SetHotkey(HotkeyAction a, string spec) => Hotkeys[a.ToString()] = spec;

    private void Normalize()
    {
        if (!string.IsNullOrWhiteSpace(LegacyHotkey))
            Hotkeys[nameof(HotkeyAction.SpeakStatus)] = LegacyHotkey;
        LegacyHotkey = null;
        foreach (var a in Enum.GetValues<HotkeyAction>())
            if (!Hotkeys.ContainsKey(a.ToString())) Hotkeys[a.ToString()] = HotkeyInfo.Default(a);
    }

    private static string Path_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ets2Tachograph", "settings.json");

    public static AppSettings Load()
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { s = new(); }
        s.Normalize();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
        File.WriteAllText(Path_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
