using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SteamPulse.Native
{
    public static class SteamNative
    {
        private const string DllName = "steam_api64.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SteamAPI_InitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SteamAPI_InitSafeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SteamAPI_InitFlatDelegate(IntPtr pOutErrMsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SteamAPI_ShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SteamAPI_RunCallbacksDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SteamAPI_IsSteamRunningDelegate();

        private static IntPtr _hModule = IntPtr.Zero;
        private static SteamAPI_InitDelegate? _init;
        private static SteamAPI_InitSafeDelegate? _initSafe;
        private static SteamAPI_InitFlatDelegate? _initFlat;
        private static SteamAPI_ShutdownDelegate? _shutdown;
        private static SteamAPI_RunCallbacksDelegate? _runCallbacks;
        private static SteamAPI_IsSteamRunningDelegate? _isSteamRunning;
        private static readonly object _lock = new();

        public static bool EnsureLoaded()
        {
            if (_hModule != IntPtr.Zero) return true;

            lock (_lock)
            {
                if (_hModule != IntPtr.Zero) return true;

                // Ensure Steam install directory is in PATH for dependencies
                EnsureSteamInPath();

                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string dllPath = Path.Combine(appDir, DllName);

                if (!File.Exists(dllPath))
                {
                    return false;
                }

                SetDllDirectory(appDir);
                _hModule = LoadLibrary(dllPath);
                if (_hModule == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr pInit = GetProcAddress(_hModule, "SteamAPI_Init");
                if (pInit != IntPtr.Zero)
                {
                    _init = Marshal.GetDelegateForFunctionPointer<SteamAPI_InitDelegate>(pInit);
                }

                IntPtr pInitSafe = GetProcAddress(_hModule, "SteamAPI_InitSafe");
                if (pInitSafe != IntPtr.Zero)
                {
                    _initSafe = Marshal.GetDelegateForFunctionPointer<SteamAPI_InitSafeDelegate>(pInitSafe);
                }

                IntPtr pInitFlat = GetProcAddress(_hModule, "SteamAPI_InitFlat");
                if (pInitFlat != IntPtr.Zero)
                {
                    _initFlat = Marshal.GetDelegateForFunctionPointer<SteamAPI_InitFlatDelegate>(pInitFlat);
                }

                IntPtr pShutdown = GetProcAddress(_hModule, "SteamAPI_Shutdown");
                if (pShutdown != IntPtr.Zero)
                {
                    _shutdown = Marshal.GetDelegateForFunctionPointer<SteamAPI_ShutdownDelegate>(pShutdown);
                }

                IntPtr pRunCallbacks = GetProcAddress(_hModule, "SteamAPI_RunCallbacks");
                if (pRunCallbacks != IntPtr.Zero)
                {
                    _runCallbacks = Marshal.GetDelegateForFunctionPointer<SteamAPI_RunCallbacksDelegate>(pRunCallbacks);
                }

                IntPtr pIsRunning = GetProcAddress(_hModule, "SteamAPI_IsSteamRunning");
                if (pIsRunning != IntPtr.Zero)
                {
                    _isSteamRunning = Marshal.GetDelegateForFunctionPointer<SteamAPI_IsSteamRunningDelegate>(pIsRunning);
                }

                return true;
            }
        }

        private static void EnsureSteamInPath()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                    if (key?.GetValue("SteamPath") is string steamPath && !string.IsNullOrWhiteSpace(steamPath))
                    {
                        steamPath = Path.GetFullPath(steamPath);
                        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                        if (!currentPath.Contains(steamPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Environment.SetEnvironmentVariable("PATH", $"{steamPath};{currentPath}");
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static bool SteamAPI_Init(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!EnsureLoaded())
            {
                errorMessage = "steam_api64.dll could not be loaded.";
                return false;
            }

            // 1. Try SteamAPI_InitSafe (standard C API exported by modern steam_api64.dll)
            if (_initSafe != null)
            {
                try
                {
                    bool ok = _initSafe();
                    if (ok) return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }

            // 2. Try SteamAPI_Init if exported
            if (_init != null)
            {
                try
                {
                    bool ok = _init();
                    if (ok) return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }

            // 3. Try SteamAPI_InitFlat fallback
            if (_initFlat != null)
            {
                IntPtr pBuffer = Marshal.AllocHGlobal(1024);
                try
                {
                    for (int i = 0; i < 1024; i++) Marshal.WriteByte(pBuffer, i, 0);
                    bool ok = _initFlat(pBuffer);
                    if (ok) return true;

                    string flatErr = Marshal.PtrToStringAnsi(pBuffer) ?? "";
                    if (!string.IsNullOrWhiteSpace(flatErr))
                    {
                        errorMessage = flatErr;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = "Steam is not running or this account does not own the game.";
            }

            return false;
        }

        public static void SteamAPI_Shutdown()
        {
            if (!EnsureLoaded()) return;
            _shutdown?.Invoke();
        }

        public static void SteamAPI_RunCallbacks()
        {
            if (!EnsureLoaded()) return;
            _runCallbacks?.Invoke();
        }

        public static bool SteamAPI_IsSteamRunning()
        {
            if (!EnsureLoaded()) return false;
            return _isSteamRunning?.Invoke() ?? false;
        }

        public static bool IsDllAvailable()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return File.Exists(Path.Combine(appDir, DllName));
        }

        public static bool SafeIsSteamRunning()
        {
            try
            {
                if (!IsDllAvailable()) return false;
                return SteamAPI_IsSteamRunning();
            }
            catch
            {
                return false;
            }
        }
    }
}
