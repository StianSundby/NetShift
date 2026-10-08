using NetShiftST.Utils;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

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
        /// <param name="configuredName">The adapter the find by name</param>
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
        /// Finds the adapter Windows routes traffic through for the specified address.
        /// Supports IPv4 and IPv6.
        /// </summary>
        public static NetworkInterface? FindRoutedAdapter(IPAddress destination)
        {
            try
            {
                var socketAddress = new IPEndPoint(destination, 0).Serialize();
                var addressBytes = new byte[socketAddress.Size];

                for (int i = 0; i < socketAddress.Size; i++)
                    addressBytes[i] = socketAddress[i];

                uint result = GetBestInterfaceEx(
                    addressBytes,
                    out uint interfaceIndex);

                if (result != 0)
                {
                    Logger.Log($"Route lookup failed with error {result}.");
                    return null;
                }

                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var properties = adapter.GetIPProperties();

                    int? index = destination.AddressFamily == AddressFamily.InterNetwork
                        ? properties.GetIPv4Properties()?.Index
                        : properties.GetIPv6Properties()?.Index;

                    if (index.HasValue && index.Value == interfaceIndex)
                        return adapter;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not detect routed adapter: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Checks operational adapter status
        /// </summary>
        /// <param name="configuredName">The name of the adapter to check</param>
        /// <remarks>
        /// This does not verify internet connectivity.
        /// </remarks>
        public static bool IsUp(string configuredName) =>
            FindAdapter(configuredName)?.OperationalStatus == OperationalStatus.Up;

        /// <summary>
        /// Sends a ping to the target using the configured ping timeout.
        /// </summary>
        /// <param name="target">The hostname or IP address to ping</param>
        /// <returns>
        /// True if a successful reply is received, otherwise false.
        /// Exceptions are logged and treated as failed probes
        /// </returns>
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
        /// Waits for operational adapter status until the adapter timeout expires
        /// </summary>
        /// <param name="name">Name of adapter to wait for</param>
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
        /// Requests adapter enabling through netsh. Command failures are logged
        /// </summary>
        /// <param name="name">Name of adapter to enable</param>
        public static Task EnableAdapterAsync(string name) =>
            RunNetshAsync($"interface set interface \"{name}\" admin=enabled");

        /// <summary>
        /// Requests adapter disabling through netsh. Command failures are logged
        /// </summary>
        /// <param name="name">Name of adapter to disable</param>
        public static Task DisableAdapterAsync(string name) =>
            RunNetshAsync($"interface set interface \"{name}\" admin=disabled");

        /// <summary>
        /// Logs the name, description, type, and operational status of every network adapter.
        /// </summary>
        /// <param name="context">
        /// When or why the adapter snapshot is being recorded.
        /// </param>
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

                //read both streams while waiting so a full pipe cant block netsh
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

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetBestInterfaceEx(byte[] destination, out uint interfaceIndex);
    }
}
