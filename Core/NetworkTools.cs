using System.Diagnostics;
using System.Net.NetworkInformation;
using NetShiftST.Utils;

namespace NetShiftST.Core
{
    /// <summary>
    /// Provides adapter lookup, connectivity probes, adapter commands, and diagnostics.
    /// </summary>
    internal static class NetworkTools
    {
        private const int PingTimeoutMs = 2000;
        private const int AdapterUpTimeoutMs = 10000;

        /// <summary>
        /// Finds an adapter by exact name, then partial name or description. Partial matches select the first matching adapter.
        /// </summary>
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

        /// <summary>
        /// Checks operational adapter status; this does not verify internet connectivity.
        /// </summary>
        public static bool IsUp(string configuredName) =>
                    FindAdapter(configuredName)?.OperationalStatus == OperationalStatus.Up;

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

        /// <summary>
        /// Waits for operational adapter status until the adapter timeout expires.
        /// </summary>
        public static async Task<bool> WaitUntilUpAsync(string name)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < AdapterUpTimeoutMs)
            {
                if (IsUp(name))
                    return true;

                await Task.Delay(500);
            }

            Logger.Log($"Timeout waiting for adapter '{name}' to come up");
            return false;
        }

        /// <summary>
        /// Requests adapter enablement through netsh. Command failures are logged rather than returned.
        /// </summary>
        public static Task EnableAdapterAsync(string name) =>
                    RunNetshAsync($"interface set interface \"{name}\" admin=enabled");

        /// <summary>
        /// Requests adapter disablement through netsh. Command failures are logged rather than returned.
        /// </summary>
        public static Task DisableAdapterAsync(string name) =>
                    RunNetshAsync($"interface set interface \"{name}\" admin=disabled");

        private static async Task RunNetshAsync(string args)
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
                    return;
                }

                // Drain both streams concurrently so a full output pipe cannot block netsh.
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Logger.Log($"netsh exit code {process.ExitCode}");
                var stdout = (await output).Trim();
                var stderr = (await error).Trim();
                if (stdout.Length > 0) Logger.Log($"netsh output: {stdout}");
                if (stderr.Length > 0) Logger.Log($"netsh error: {stderr}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error running netsh {args}: {ex.Message}");
            }
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

    }
}
