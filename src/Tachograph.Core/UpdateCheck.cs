using System.Text.Json;

namespace Tachograph.Core;

/// <summary>The newest published release, as read from GitHub's "latest release" API response.</summary>
public sealed record UpdateInfo(Version Latest, string Url, string Name);

public static class UpdateCheck
{
    /// <summary>Reads tag_name / html_url / name. Returns null if the JSON isn't a usable release.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } t) return null;
            if (!TryVersion(t, out var v)) return null;
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? t : t;
            return new UpdateInfo(v, url, name);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }

    public static bool IsNewer(UpdateInfo info, string currentVersion) =>
        TryVersion(currentVersion, out var cur) && info.Latest > cur;

    /// <summary>Accepts "v1.3.0", "1.3", "1.3.0-beta+abc"; ignores anything after the numeric parts.</summary>
    public static bool TryVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4 || !parts.All(p => int.TryParse(p, out _))) return false;
        version = Version.Parse(parts.Length == 2 ? s + ".0" : s);
        return true;
    }
}
