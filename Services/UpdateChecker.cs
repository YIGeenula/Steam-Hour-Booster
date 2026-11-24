using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace SteamPulse.Services
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    public static class UpdateChecker
    {
        public const string CurrentVersion = "1.0.0";
        // User/repo on GitHub for releases
        public const string GitHubRepo = "GeeNuuSs/SteamPulse";

        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SteamPulse", CurrentVersion));
                client.Timeout = TimeSpan.FromSeconds(6);

                string apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagElem))
                {
                    return null;
                }

                string latestTag = tagElem.GetString() ?? "";
                string releaseUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "";
                string releaseNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

                // Strip leading 'v' if present (e.g. v1.1.0 -> 1.1.0)
                string cleanLatest = latestTag.TrimStart('v', 'V').Trim();
                string cleanCurrent = CurrentVersion.TrimStart('v', 'V').Trim();

                if (Version.TryParse(cleanLatest, out var vLatest) && Version.TryParse(cleanCurrent, out var vCurrent))
                {
                    bool isNewer = vLatest > vCurrent;
                    return new UpdateInfo
                    {
                        IsUpdateAvailable = isNewer,
                        LatestVersion = cleanLatest,
                        CurrentVersion = cleanCurrent,
                        ReleaseUrl = string.IsNullOrWhiteSpace(releaseUrl) ? $"https://github.com/{GitHubRepo}/releases" : releaseUrl,
                        ReleaseNotes = releaseNotes
                    };
                }

                return null;
            }
            catch
            {
                // Graceful fallback if offline, rate limited or repo not yet published
                return null;
            }
        }

        public static void OpenReleaseUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }
}
