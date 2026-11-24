using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SteamPulse.Models
{
    public class GameEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private uint _appId;
        private string _customCoverUrl = string.Empty;
        private bool _isBoosting;
        private TimeSpan _currentSessionTime = TimeSpan.Zero;
        private long _totalBoostedSeconds;
        private string _statusMessage = "Idle";

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public uint AppId
        {
            get => _appId;
            set
            {
                if (_appId != value)
                {
                    _appId = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderImageUrl));
                }
            }
        }

        public string CustomCoverUrl
        {
            get => _customCoverUrl;
            set
            {
                if (_customCoverUrl != value)
                {
                    _customCoverUrl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderImageUrl));
                }
            }
        }

        [JsonIgnore]
        public string HeaderImageUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomCoverUrl))
                    return CustomCoverUrl;
                return $"https://cdn.akamai.steamstatic.com/steam/apps/{AppId}/header.jpg";
            }
        }

        public long TotalBoostedSeconds
        {
            get => _totalBoostedSeconds;
            set
            {
                if (_totalBoostedSeconds != value)
                {
                    _totalBoostedSeconds = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalBoostedDisplay));
                }
            }
        }

        [JsonIgnore]
        public bool IsBoosting
        {
            get => _isBoosting;
            set
            {
                if (_isBoosting != value)
                {
                    _isBoosting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActionBtnText));
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        [JsonIgnore]
        public TimeSpan CurrentSessionTime
        {
            get => _currentSessionTime;
            set
            {
                if (_currentSessionTime != value)
                {
                    _currentSessionTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SessionTimeDisplay));
                }
            }
        }

        [JsonIgnore]
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        [JsonIgnore]
        public string StatusDisplay => IsBoosting ? "BOOSTING" : StatusMessage;

        [JsonIgnore]
        public string ActionBtnText => IsBoosting ? "STOP" : "BOOST";

        [JsonIgnore]
        public string SessionTimeDisplay => CurrentSessionTime.ToString(@"hh\:mm\:ss");

        [JsonIgnore]
        public string TotalBoostedDisplay
        {
            get
            {
                var ts = TimeSpan.FromSeconds(TotalBoostedSeconds);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}h {ts.Minutes}m total";
                return $"{ts.Minutes}m {ts.Seconds}s total";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
