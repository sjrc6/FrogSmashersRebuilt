using System.Runtime.InteropServices;
using Steamworks;

namespace FrogSmashers.Network;

internal static class SteamRuntime
{
    private static readonly object gate = new();
    private static int leaseCount;
    private static uint activeAppId;

    [DllImport("libc", EntryPoint = "setenv", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetEnv(string name, string value, int overwrite);

    private static void SetProcessEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsLinux() && SetEnv(name, value, 1) != 0)
        {
            throw new InvalidOperationException($"Could not set native environment variable {name}");
        }
    }

    static SteamRuntime()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(SteamAPI).Assembly,
            (name, assembly, paths) =>
            {
                if (name is not ("steam_api" or "steam_api64"))
                {
                    return IntPtr.Zero;
                }

                string file = OperatingSystem.IsWindows() ? "steam_api64.dll" : "libsteam_api.so";
                return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, file));
            }
        );
    }

    internal static bool TryAcquire(uint appId, out string? error)
    {
        lock (gate)
        {
            error = null;
            if (leaseCount > 0)
            {
                if (activeAppId != appId)
                {
                    error = $"Steam is already initialized for AppID {activeAppId}";
                    return false;
                }

                leaseCount++;
                return true;
            }

            bool initialized = false;
            try
            {
                SetProcessEnvironment("SteamAppId", appId.ToString());
                SetProcessEnvironment("SteamGameId", appId.ToString());
                var result = SteamAPI.InitEx(out string detail);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    error = $"Steam initialization failed: {detail}";
                    return false;
                }

                initialized = true;
                SteamNetworkingUtils.InitRelayNetworkAccess();
                leaseCount = 1;
                activeAppId = appId;
                return true;
            }
            catch (DllNotFoundException ex)
            {
                error = $"Steam runtime unavailable: {ex.Message}";
            }
            catch (EntryPointNotFoundException ex)
            {
                error = $"Steam runtime version mismatch: {ex.Message}";
            }
            catch (BadImageFormatException ex)
            {
                error = $"Steam runtime architecture mismatch: {ex.Message}";
            }

            if (initialized)
            {
                SteamAPI.Shutdown();
            }

            return false;
        }
    }

    internal static void Release()
    {
        lock (gate)
        {
            if (leaseCount <= 0)
            {
                return;
            }

            if (--leaseCount == 0)
            {
                SteamAPI.Shutdown();
                activeAppId = 0;
            }
        }
    }
}
