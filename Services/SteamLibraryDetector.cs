using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SteamPulse.Services
{
    public class DetectedGame
    {
        public uint AppId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public static class SteamLibraryDetector
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public static bool IsSteamRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("steam");
                return procs.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsUserLoggedIn(out uint activeUserId, out string steamInstallDir)
        {
            activeUserId = 0;
            steamInstallDir = GetSteamInstallPath();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
                if (key != null)
                {
                    object? userVal = key.GetValue("ActiveUser");
                    if (userVal is int iVal && iVal > 0)
                    {
                        activeUserId = (uint)iVal;
                    }
                    else if (userVal is long lVal && lVal > 0)
                    {
                        activeUserId = (uint)lVal;
                    }
                }
            }
            catch
            {
                // Ignore registry access errors
            }

            return activeUserId > 0;
        }

        public static string GetSteamInstallPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    string? path = key.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path.Replace('/', '\\');
                    }
                }
            }
            catch { }

            string defaultPath = @"C:\Program Files (x86)\Steam";
            if (Directory.Exists(defaultPath)) return defaultPath;
            return string.Empty;
        }

        public static async Task<List<DetectedGame>> DetectUserGamesAsync()
        {
            var detectedMap = new ConcurrentDictionary<uint, string>();
            string steamDir = GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamDir) || !Directory.Exists(steamDir))
            {
                return new List<DetectedGame>();
            }

            // 1. Scan installed manifests from libraryfolders.vdf & steamapps
            ScanInstalledManifests(steamDir, detectedMap);

            // 2. Scan active user's localconfig.vdf (owned/played library games)
            IsUserLoggedIn(out uint activeUser, out _);
            if (activeUser > 0)
            {
                string userConfigPath = Path.Combine(steamDir, "userdata", activeUser.ToString(), "config", "localconfig.vdf");
                if (File.Exists(userConfigPath))
                {
                    ScanUserLocalConfig(userConfigPath, detectedMap);
                }
            }
            else
            {
                // Scan all userdata accounts if activeUser wasn't strictly found
                string userdataDir = Path.Combine(steamDir, "userdata");
                if (Directory.Exists(userdataDir))
                {
                    foreach (var userDir in Directory.GetDirectories(userdataDir))
                    {
                        string cfg = Path.Combine(userDir, "config", "localconfig.vdf");
                        if (File.Exists(cfg))
                        {
                            ScanUserLocalConfig(cfg, detectedMap);
                        }
                    }
                }
            }

            // Ignore common non-game tool/runtime app IDs
            var ignoredAppIds = new HashSet<uint> { 7, 760, 228980, 228988, 228990, 244210 };
            foreach (var id in ignoredAppIds)
            {
                detectedMap.TryRemove(id, out _);
            }

            // 3. Resolve missing game names from local appcache/appinfo.vdf
            string appinfoPath = Path.Combine(steamDir, "appcache", "appinfo.vdf");
            if (File.Exists(appinfoPath))
            {
                ResolveNamesFromAppInfo(appinfoPath, detectedMap);
            }

            // 4. Resolve remaining missing names via Steam Web API concurrently
            var missingList = detectedMap.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value)).Select(kvp => kvp.Key).ToList();
            if (missingList.Count > 0)
            {
                var tasks = missingList.Select(async appId =>
                {
                    string? onlineName = await FetchGameNameOnlineAsync(appId);
                    if (!string.IsNullOrWhiteSpace(onlineName))
                    {
                        detectedMap[appId] = onlineName;
                    }
                    else
                    {
                        detectedMap[appId] = $"Steam App {appId}";
                    }
                });
                await Task.WhenAll(tasks);
            }

            return detectedMap
                .Select(kvp => new DetectedGame { AppId = kvp.Key, Name = kvp.Value })
                .OrderBy(g => g.Name)
                .ToList();
        }

        private static void ScanInstalledManifests(string steamDir, ConcurrentDictionary<uint, string> detectedMap)
        {
            var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamDir };

            string libFoldersVdf = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libFoldersVdf))
            {
                try
                {
                    string content = File.ReadAllText(libFoldersVdf);
                    var matches = Regex.Matches(content, @"\""path\""\s*\""([^\""]+)\""");
                    foreach (Match m in matches)
                    {
                        if (m.Groups.Count > 1)
                        {
                            string p = m.Groups[1].Value.Replace(@"\\", @"\");
                            if (Directory.Exists(p)) libraryPaths.Add(p);
                        }
                    }
                }
                catch { }
            }

            foreach (var lib in libraryPaths)
            {
                string appsDir = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(appsDir)) continue;

                try
                {
                    var acfFiles = Directory.GetFiles(appsDir, "appmanifest_*.acf");
                    foreach (var acf in acfFiles)
                    {
                        try
                        {
                            string text = File.ReadAllText(acf);
                            var idMatch = Regex.Match(text, @"\""appid\""\s*\""(\d+)\""");
                            var nameMatch = Regex.Match(text, @"\""name\""\s*\""([^\""]+)\""");

                            if (idMatch.Success && uint.TryParse(idMatch.Groups[1].Value, out uint appId))
                            {
                                string name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : string.Empty;
                                detectedMap[appId] = name;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void ScanUserLocalConfig(string configPath, ConcurrentDictionary<uint, string> detectedMap)
        {
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                bool inAppsSection = false;
                int braceDepth = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Equals("\"Apps\"", StringComparison.OrdinalIgnoreCase))
                    {
                        inAppsSection = true;
                        braceDepth = 0;
                        continue;
                    }

                    if (inAppsSection)
                    {
                        if (line == "{")
                        {
                            braceDepth++;
                        }
                        else if (line == "}")
                        {
                            braceDepth--;
                            if (braceDepth <= 0)
                            {
                                inAppsSection = false;
                                break;
                            }
                        }
                        else if (braceDepth == 1)
                        {
                            var match = Regex.Match(line, @"^\""(\d{3,9})\$");
                            if (!match.Success)
                            {
                                match = Regex.Match(line, @"^\""(\d{3,9})\""$");
                            }

                            if (match.Success && uint.TryParse(match.Groups[1].Value, out uint appId))
                            {
                                if (!detectedMap.ContainsKey(appId))
                                {
                                    detectedMap[appId] = string.Empty;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ResolveNamesFromAppInfo(string appInfoPath, ConcurrentDictionary<uint, string> detectedMap)
        {
            try
            {
                byte[] data = File.ReadAllBytes(appInfoPath);
                var emptyKeys = detectedMap.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value)).Select(kvp => kvp.Key).ToList();

                byte[] namePrefix1 = Encoding.ASCII.GetBytes("\x01\x04\x00\x00\x00");
                byte[] namePrefix2 = Encoding.ASCII.GetBytes("\x01name\x00");

                foreach (uint aid in emptyKeys)
                {
                    byte[] aidBytes = BitConverter.GetBytes(aid);
                    int idx = FindBytes(data, aidBytes, 0);

                    while (idx != -1 && idx < data.Length - 200)
                    {
                        int searchWindow = Math.Min(300, data.Length - (idx + 4));
                        int nPos = FindBytes(data, namePrefix1, idx + 4, searchWindow);
                        int prefixLen = namePrefix1.Length;

                        if (nPos == -1)
                        {
                            nPos = FindBytes(data, namePrefix2, idx + 4, searchWindow);
                            prefixLen = namePrefix2.Length;
                        }

                        if (nPos != -1)
                        {
                            int strStart = nPos + prefixLen;
                            int strEnd = Array.IndexOf(data, (byte)0, strStart);
                            if (strEnd > strStart && strEnd - strStart < 120)
                            {
                                string name = Encoding.UTF8.GetString(data, strStart, strEnd - strStart).Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    detectedMap[aid] = name;
                                    break;
                                }
                            }
                        }

                        idx = FindBytes(data, aidBytes, idx + 1);
                    }
                }
            }
            catch { }
        }

        private static int FindBytes(byte[] src, byte[] pattern, int startIndex, int maxCount = -1)
        {
            if (src == null || pattern == null || src.Length == 0 || pattern.Length == 0) return -1;
            int limit = (maxCount == -1) ? (src.Length - pattern.Length) : Math.Min(src.Length - pattern.Length, startIndex + maxCount);

            for (int i = startIndex; i <= limit; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (src[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private static async Task<string?> FetchGameNameOnlineAsync(uint appId)
        {
            try
            {
                string url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "SteamPulse/1.0");

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(appId.ToString(), out var appProp) &&
                        appProp.TryGetProperty("success", out var succ) && succ.GetBoolean() &&
                        appProp.TryGetProperty("data", out var dataProp) &&
                        dataProp.TryGetProperty("name", out var nameProp))
                    {
                        return nameProp.GetString();
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
