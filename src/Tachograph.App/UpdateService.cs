using System.Net;
using System.Net.Http;
using Tachograph.Core;

namespace Tachograph.App;

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, UpdateInfo? Info = null, string? Error = null);

/// <summary>Asks GitHub for the latest published release. It never downloads or runs anything.</summary>
public static class UpdateService
{
    public const string Repo = "johnevansau320/ETS2Tachograph";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases";

    public static async Task<UpdateResult> CheckAsync(string currentVersion)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ETS2Tachograph/{currentVersion}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (resp.StatusCode == HttpStatusCode.NotFound) return new UpdateResult(UpdateStatus.NoReleases);
            if (!resp.IsSuccessStatusCode) return new UpdateResult(UpdateStatus.Failed, null, $"GitHub answered {(int)resp.StatusCode}.");

            var info = UpdateCheck.Parse(await resp.Content.ReadAsStringAsync());
            if (info is null) return new UpdateResult(UpdateStatus.Failed, null, "The latest release has no usable version number.");
            return UpdateCheck.IsNewer(info, currentVersion)
                ? new UpdateResult(UpdateStatus.Available, info)
                : new UpdateResult(UpdateStatus.UpToDate, info);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new UpdateResult(UpdateStatus.Failed, null, "Couldn't reach GitHub.");
        }
    }
}
