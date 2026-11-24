using System;
using System.IO;
using System.Threading;
using SteamPulse.Native;

namespace SteamPulse.Worker
{
    public static class WorkerProgram
    {
        private static readonly ManualResetEventSlim ShutdownEvent = new(false);

        public static int Run(uint appId)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            SteamNative.SetDllDirectory(appDir);

            // Set required Steam App ID environment variables
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(), EnvironmentVariableTarget.Process);

            // Write steam_appid.txt in current app directory
            string appIdFile = Path.Combine(appDir, "steam_appid.txt");
            try
            {
                File.WriteAllText(appIdFile, appId.ToString());
            }
            catch
            {
            }

            Console.WriteLine($"[SteamPulse Worker] Initializing SteamAPI for AppID: {appId}");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                ShutdownEvent.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                Cleanup();
            };

            bool initialized = false;
            try
            {
                if (!SteamNative.IsDllAvailable())
                {
                    Console.Error.WriteLine("[SteamPulse Worker] steam_api64.dll is missing.");
                    return 2;
                }

                initialized = SteamNative.SteamAPI_Init(out string errorMsg);
                if (!initialized)
                {
                    Console.Error.WriteLine($"[SteamPulse Worker] SteamAPI_Init failed: {errorMsg}");
                    return 1;
                }

                Console.WriteLine($"[SteamPulse Worker] Successfully boosting AppID: {appId}. Entering callback loop...");

                // Low-overhead loop calling SteamAPI_RunCallbacks() every 1000ms (~0% CPU/RAM)
                while (!ShutdownEvent.IsSet)
                {
                    try
                    {
                        SteamNative.SteamAPI_RunCallbacks();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[SteamPulse Worker] Error in RunCallbacks: {ex.Message}");
                    }

                    if (ShutdownEvent.Wait(1000))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SteamPulse Worker] Fatal error: {ex.Message}");
                return 3;
            }
            finally
            {
                if (initialized)
                {
                    Cleanup();
                }
            }

            Console.WriteLine("[SteamPulse Worker] Worker exited gracefully.");
            return 0;
        }

        private static void Cleanup()
        {
            try
            {
                SteamNative.SteamAPI_Shutdown();
                Console.WriteLine("[SteamPulse Worker] SteamAPI_Shutdown called.");
            }
            catch
            {
            }
        }
    }
}
