using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SteamPulse
{
    public partial class SplashScreen : Window
    {
        private readonly DispatcherTimer _holdTimer;
        public event EventHandler? SplashClosed;

        public SplashScreen()
        {
            InitializeComponent();

            // Fade in on load
            Loaded += (_, _) =>
            {
                var fadeIn = (Storyboard)Resources["FadeIn"];
                fadeIn.Begin(SplashWindow);
            };

            // Hold for 2 seconds after fade-in, then fade out
            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            _holdTimer.Tick += (_, _) =>
            {
                _holdTimer.Stop();
                var fadeOut = (Storyboard)Resources["FadeOut"];
                fadeOut.Completed += (_, _) =>
                {
                    SplashClosed?.Invoke(this, EventArgs.Empty);
                    Close();
                };
                fadeOut.Begin(SplashWindow);
            };
            _holdTimer.Start();
        }
    }
}
