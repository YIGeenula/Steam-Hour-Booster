using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SteamPulse.Models;
using SteamPulse.Native;
using SteamPulse.Services;

namespace SteamPulse
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GameEntry> _games = new();
        private ICollectionView? _gamesView;
        private readonly ConcurrentDictionary<uint, Process> _workerProcesses = new();
        private readonly ConcurrentDictionary<uint, DateTime> _sessionStartTimes = new();
        private readonly DispatcherTimer _uiTimer;
        private readonly DispatcherTimer _steamCheckTimer;
        private readonly string _configFilePath;
        private bool _isSteamDetected;
        private bool _isUserLoggedIn;
        private bool _isSyncingLibrary;
        private string _activeFilter = "All";
        private string _latestReleaseUrl = $"https://github.com/{UpdateChecker.GitHubRepo}/releases";

        public MainWindow()
        {
            InitializeComponent();
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_config.json");

            _gamesView = CollectionViewSource.GetDefaultView(_games);
            _gamesView.Filter = FilterGames;
            GamesItemsControl.ItemsSource = _gamesView;

            _games.CollectionChanged += (s, e) =>
            {
                UpdateMetrics();
                UpdateEmptyState();
            };

            // 1-second tick for session timer update & cumulative playtime
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // Periodic Steam detection check (every 5s)
            _steamCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _steamCheckTimer.Tick += (s, e) => CheckSteamStatus();
            _steamCheckTimer.Start();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckSteamStatus();
            LoadGamesConfig();
            UpdateMetrics();
            UpdateEmptyState();

            // Auto-detect and sync Steam account games if library is fresh/empty or on startup
            await AutoSyncSteamGamesAsync(silent: true);

            // Automatically check for GitHub updates in background on launch
            await CheckUpdatesAsync(silent: true);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            StopAllWorkers();
            SaveGamesConfig();
        }

        #region Title Bar & Window Controls

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnTray_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DismissBanner_Click(object sender, RoutedEventArgs e)
        {
            NotificationBanner.Visibility = Visibility.Collapsed;
        }

        private void DismissUpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }

        private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateChecker.OpenReleaseUrl(_latestReleaseUrl);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync(silent: false);
        }

        private async Task CheckUpdatesAsync(bool silent)
        {
            var update = await UpdateChecker.CheckForUpdatesAsync();
            if (update != null && update.IsUpdateAvailable)
            {
                _latestReleaseUrl = update.ReleaseUrl;
                UpdateBannerText.Text = $"🚀 New version {update.LatestVersion} available on GitHub! (Current: {update.CurrentVersion})";
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else if (!silent)
            {
                ShowBanner($"You are already using the latest version of SteamPulse (v{UpdateChecker.CurrentVersion}).", isError: false);
            }
        }

        private void ShowBanner(string message, bool isError = true)
        {
            NotificationText.Text = message;
            NotificationBanner.Background = isError
                ? (System.Windows.Media.Brush)FindResource("BrushDangerSurface")
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 12, 31, 56));
            NotificationText.Foreground = isError
                ? (System.Windows.Media.Brush)FindResource("BrushDangerForeground")
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 147, 197, 253));
            NotificationBanner.Visibility = Visibility.Visible;
        }

        #endregion

        #region Steam Connectivity & Account Check

        private void CheckSteamStatus()
        {
            bool running = false;
            try
            {
                running = SteamNative.SafeIsSteamRunning();
                if (!running)
                {
                    running = SteamLibraryDetector.IsSteamRunning();
                }
            }
            catch
            {
                running = false;
            }

            _isSteamDetected = running;
            _isUserLoggedIn = SteamLibraryDetector.IsUserLoggedIn(out uint activeUserId, out _);

            if (_isSteamDetected && _isUserLoggedIn)
            {
                SteamStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                SteamStatusText.Text = $"Steam Connected (User: {activeUserId})";
                SteamStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            }
            else if (_isSteamDetected && !_isUserLoggedIn)
            {
                SteamStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
                SteamStatusText.Text = "Steam running (Not Logged In)";
                SteamStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
                ShowBanner("Steam is running but no active account is logged in. Please log in to your Steam account to boost games.", isError: true);
            }
            else
            {
                SteamStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                SteamStatusText.Text = "Steam is not running";
                SteamStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
            }
        }

        #endregion

        #region Game Management (Add, Delete, Persistence, Filters)

        private void TxtAppId_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void BtnAddGame_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtGameName.Text.Trim();
            string appIdStr = TxtAppId.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowBanner("Please provide a Game Title.", true);
                TxtGameName.Focus();
                return;
            }

            if (!uint.TryParse(appIdStr, out uint appId) || appId == 0)
            {
                ShowBanner("Please enter a valid numeric Steam App ID (e.g. 730 for CS2, 570 for Dota 2).", true);
                TxtAppId.Focus();
                return;
            }

            if (_games.Any(g => g.AppId == appId))
            {
                ShowBanner($"Game with App ID {appId} already exists in your library.", true);
                return;
            }

            var newGame = new GameEntry
            {
                Name = name,
                AppId = appId,
                StatusMessage = "Ready"
            };

            _games.Insert(0, newGame);
            TxtGameName.Clear();
            TxtAppId.Clear();
            SaveGamesConfig();
            UpdateMetrics();
            UpdateEmptyState();
        }

        private void BtnDeleteGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GameEntry game)
            {
                if (game.IsBoosting)
                {
                    StopBoostingGame(game);
                }

                _games.Remove(game);
                SaveGamesConfig();
                UpdateMetrics();
                UpdateEmptyState();
            }
        }

        private void NavFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _activeFilter = tag;
                if (TxtViewHeading != null)
                {
                    TxtViewHeading.Text = tag switch
                    {
                        "Boosting" => "Active Boosting Sessions",
                        "Idle" => "Idle Games Library",
                        _ => "My Boost Library"
                    };
                }
                _gamesView?.Refresh();
                UpdateEmptyState();
            }
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _gamesView?.Refresh();
            UpdateEmptyState();
        }

        private bool FilterGames(object item)
        {
            if (item is not GameEntry game) return false;

            string search = TxtSearchFilter?.Text?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                bool matchesSearch = game.Name.ToLowerInvariant().Contains(search) || game.AppId.ToString().Contains(search);
                if (!matchesSearch) return false;
            }

            return _activeFilter switch
            {
                "Boosting" => game.IsBoosting,
                "Idle" => !game.IsBoosting,
                _ => true
            };
        }

        private void UpdateEmptyState()
        {
            bool hasItems = _gamesView != null && !_gamesView.IsEmpty;
            EmptyStatePanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateMetrics()
        {
            int total = _games.Count;
            int active = _games.Count(g => g.IsBoosting);

            MetricTotalGames.Text = total.ToString();
            MetricActiveCount.Text = active.ToString();

            long totalSeconds = _games.Sum(g => g.CurrentSessionTime.Ticks) / TimeSpan.TicksPerSecond;
            var ts = TimeSpan.FromSeconds(totalSeconds);
            MetricTotalSessionTime.Text = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00} Active";
        }

        private void SaveGamesConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_games.ToList(), options);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private void LoadGamesConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<ObservableCollection<GameEntry>>(json);
                    if (list != null)
                    {
                        _games.Clear();
                        foreach (var g in list)
                        {
                            g.IsBoosting = false;
                            g.CurrentSessionTime = TimeSpan.Zero;
                            g.StatusMessage = "Ready";
                            _games.Add(g);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        private async void BtnSyncSteam_Click(object sender, RoutedEventArgs e)
        {
            await AutoSyncSteamGamesAsync(silent: false);
        }

        private async Task AutoSyncSteamGamesAsync(bool silent)
        {
            if (_isSyncingLibrary) return;

            if (!_isSteamDetected)
            {
                if (!silent)
                {
                    ShowBanner("Steam is not running. Please launch Steam and log into your account first.", isError: true);
                }
                return;
            }

            if (!_isUserLoggedIn)
            {
                ShowBanner("Steam is running, but no logged-in account was detected. Please log in to Steam to auto-detect your games.", isError: true);
                return;
            }

            _isSyncingLibrary = true;
            if (TxtSyncSteam != null) TxtSyncSteam.Text = "Syncing...";
            if (BtnSyncSteam != null) BtnSyncSteam.IsEnabled = false;

            try
            {
                var detectedGames = await Task.Run(() => SteamLibraryDetector.DetectUserGamesAsync());

                int addedCount = 0;
                foreach (var dg in detectedGames)
                {
                    if (!_games.Any(g => g.AppId == dg.AppId))
                    {
                        _games.Add(new GameEntry
                        {
                            Name = dg.Name,
                            AppId = dg.AppId,
                            StatusMessage = "Ready"
                        });
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    SaveGamesConfig();
                    UpdateMetrics();
                    UpdateEmptyState();
                    _gamesView?.Refresh();
                    ShowBanner($"🎉 Successfully imported {addedCount} games from your Steam account!", isError: false);
                }
                else if (!silent)
                {
                    ShowBanner($"Steam library sync complete. All {detectedGames.Count} detected games are already in your list.", isError: false);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    ShowBanner($"Failed to sync Steam library: {ex.Message}", isError: true);
                }
            }
            finally
            {
                _isSyncingLibrary = false;
                if (TxtSyncSteam != null) TxtSyncSteam.Text = "Sync Steam";
                if (BtnSyncSteam != null) BtnSyncSteam.IsEnabled = true;
            }
        }

        #endregion

        #region Boosting Controls (Worker Spawning & Shutdown)

        private void BtnToggleGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GameEntry game)
            {
                if (game.IsBoosting)
                {
                    StopBoostingGame(game);
                }
                else
                {
                    StartBoostingGame(game);
                }
            }
        }

        private void StartBoostingGame(GameEntry game)
        {
            if (!_isSteamDetected)
            {
                ShowBanner("Steam is not running. Please launch and log into the Steam client first!", true);
                return;
            }

            if (!_isUserLoggedIn)
            {
                ShowBanner("No active user is logged into Steam. Please log in to your Steam account to boost games.", true);
                return;
            }

            if (_workerProcesses.ContainsKey(game.AppId))
            {
                return;
            }

            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SteamPulse.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--worker {game.AppId}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            try
            {
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                process.Exited += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_workerProcesses.TryRemove(game.AppId, out _))
                        {
                            game.IsBoosting = false;
                            string stdErr = process.StandardError.ReadToEnd();
                            if (process.ExitCode != 0)
                            {
                                game.StatusMessage = "Init Failed";
                                string detail = !string.IsNullOrWhiteSpace(stdErr) ? stdErr.Trim() : "Steam not running or unowned game.";
                                ShowBanner($"'{game.Name}' ({game.AppId}) could not boost: {detail}", true);
                            }
                            else
                            {
                                game.StatusMessage = "Stopped";
                            }
                            UpdateMetrics();
                            _gamesView?.Refresh();
                        }
                    });
                };

                process.Start();

                Task.Delay(800).ContinueWith(t =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (process.HasExited && process.ExitCode != 0)
                        {
                            game.IsBoosting = false;
                            game.StatusMessage = "Failed to Init";
                            _workerProcesses.TryRemove(game.AppId, out _);
                        }
                        else
                        {
                            game.IsBoosting = true;
                            game.StatusMessage = "Boosting";
                            _sessionStartTimes[game.AppId] = DateTime.UtcNow;
                        }
                        UpdateMetrics();
                        _gamesView?.Refresh();
                    });
                });

                _workerProcesses[game.AppId] = process;
                game.IsBoosting = true;
                _sessionStartTimes[game.AppId] = DateTime.UtcNow;
                UpdateMetrics();
                _gamesView?.Refresh();
            }
            catch (Exception ex)
            {
                game.IsBoosting = false;
                game.StatusMessage = "Failed";
                ShowBanner($"Failed to start worker process for '{game.Name}': {ex.Message}", true);
            }
        }

        private void StopBoostingGame(GameEntry game)
        {
            if (_workerProcesses.TryRemove(game.AppId, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                }
                catch
                {
                }
            }

            if (_sessionStartTimes.TryRemove(game.AppId, out var startTime))
            {
                long elapsed = (long)(DateTime.UtcNow - startTime).TotalSeconds;
                game.TotalBoostedSeconds += Math.Max(0, elapsed);
            }

            game.IsBoosting = false;
            game.CurrentSessionTime = TimeSpan.Zero;
            game.StatusMessage = "Stopped";
            UpdateMetrics();
            _gamesView?.Refresh();
            SaveGamesConfig();
        }

        private void BtnStartAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var game in _games)
            {
                if (!game.IsBoosting)
                {
                    StartBoostingGame(game);
                }
            }
        }

        private void BtnStopAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var game in _games)
            {
                if (game.IsBoosting)
                {
                    StopBoostingGame(game);
                }
            }
        }

        private void StopAllWorkers()
        {
            foreach (var kvp in _workerProcesses)
            {
                try
                {
                    if (!kvp.Value.HasExited)
                    {
                        kvp.Value.Kill();
                    }
                }
                catch
                {
                }
            }
            _workerProcesses.Clear();
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            bool anyBoosting = false;
            foreach (var game in _games)
            {
                if (game.IsBoosting && _sessionStartTimes.TryGetValue(game.AppId, out var start))
                {
                    game.CurrentSessionTime = now - start;
                    game.TotalBoostedSeconds += 1;
                    anyBoosting = true;
                }
            }

            if (anyBoosting)
            {
                UpdateMetrics();
            }
        }

        #endregion
    }
}
