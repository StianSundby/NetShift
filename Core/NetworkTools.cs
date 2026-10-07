using NetShiftST.Utils;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetShiftST.Core
{
    internal static class NetworkTools
    {
        private const int PingTimeoutMs = 2000;
        private const int AdapterUpTimeoutMs = 10000;

        public static async Task<bool> PingAsync(string target)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, PingTimeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                Logger.Log($"Ping failed: {ex.Message}");
                return false;
            }
        }

        public static NetworkInterface? FindAdapter(string configuredName)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                return null;

            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;

            return adapters.FirstOrDefault(a => a.Name.Equals(configuredName, ignoreCase))
                ?? adapters.FirstOrDefault(a => a.Name.Contains(configuredName, ignoreCase))
                ?? adapters.FirstOrDefault(a => a.Description.Contains(configuredName, ignoreCase));
        }

        public static bool IsUp(string configuredName) =>
            FindAdapter(configuredName)?.OperationalStatus == OperationalStatus.Up;

        public static Task<bool> EnableAdapterAsync(string name) =>
            RunNetshAsync($"interface set interface \"{name}\" admin=enabled");

        public static Task<bool> DisableAdapterAsync(string name) =>
            RunNetshAsync($"interface set interface \"{name}\" admin=disabled");

        public static async Task<bool> WaitUntilUpAsync(string name)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < AdapterUpTimeoutMs)
            {
                if (IsUp(name))
                    return true;

                await Task.Delay(500);
            }

            Logger.Log($"Timeout waiting for adapter '{name}' to come up.");
            return false;
        }

        public static void LogAdapters(string context)
        {
            try
            {
                Logger.Log($"--- Adapters ({context}) ---");
                foreach (var a in NetworkInterface.GetAllNetworkInterfaces())
                    Logger.Log($"Name='{a.Name}' Description='{a.Description}' Type={a.NetworkInterfaceType} Status={a.OperationalStatus}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error listing adapters: {ex.Message}");
            }
        }

        private static async Task<bool> RunNetshAsync(string args)
        {
            Logger.Log($"Executing: netsh {args}");
            var startInfo = new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Logger.Log("Could not start netsh.");
                    return false;
                }

                //read both streams while waiting so a full pipe cant block netsh
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Logger.Log($"netsh exit code {process.ExitCode}");
                var stdout = (await output).Trim();
                var stderr = (await error).Trim();
                if (stdout.Length > 0) Logger.Log($"netsh output: {stdout}");
                if (stderr.Length > 0) Logger.Log($"netsh error: {stderr}");
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error running netsh {args}: {ex.Message}");
                return false;
            }
        }
    }
}
