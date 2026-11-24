using System;
using System.IO;
using System.Linq;
using System.Windows;
using SteamPulse.Worker;

namespace SteamPulse
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for worker argument: --worker <AppID>
            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--worker", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(e.Args[1], out uint appId))
                {
                    int exitCode = WorkerProgram.Run(appId);
                    Environment.Exit(exitCode);
                    return;
                }
            }

            // Normal UI Mode — show splash, then open MainWindow
            base.OnStartup(e);

            var splash = new SplashScreen();
            splash.Show();

            splash.SplashClosed += (_, _) =>
            {
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
            };
        }
    }
}
