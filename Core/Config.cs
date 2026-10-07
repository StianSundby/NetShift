using System.IO;
using NetShiftST.Utils;

namespace NetShiftST.Core
{
    public enum CloseAction { Ask, MinimizeToTray, Exit }
    /// <summary>
    /// Stores connection thresholds and application preferences in a key-value settings file.
    /// </summary>
    public class Config
    {
        public string EthernetName { get; set; } = "Ethernet";
        public string WiFiName { get; set; } = "Wi-Fi";
        public string PingTarget { get; set; } = "8.8.8.8";
        public int CheckIntervalSeconds { get; set; } = 15;
        public int FailureThreshold { get; set; } = 3;
        public int SuccessThreshold { get; set; } = 2;
        public int MinWifiUptimeSeconds { get; set; } = 10;

        public CloseAction CloseAction { get; set; } = CloseAction.Ask;

        /// <summary>
        /// Loads settings, retaining defaults for missing files and unrecognized or unparseable values.
        /// </summary>
        public static Config Load(string path)
        {
            var config = new Config();

            if (!File.Exists(path))
            {
                Logger.Log($"Config file '{path}' not found - using defaults.");
                return config;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;

                var value = parts[1];
                switch (parts[0].ToLowerInvariant())
                {
                    case "ethernetname":
                        config.EthernetName = value;
                        break;
                    case "wifiname":
                        config.WiFiName = value;
                        break;
                    case "pingtarget":
                        config.PingTarget = value;
                        break;
                    case "checkintervalseconds":
                        config.CheckIntervalSeconds = ParseInt(value, config.CheckIntervalSeconds);
                        break;
                    case "failurethreshold":
                        config.FailureThreshold = ParseInt(value, config.FailureThreshold);
                        break;
                    case "successthreshold":
                        config.SuccessThreshold = ParseInt(value, config.SuccessThreshold);
                        break;
                    case "minwifiuptimeseconds":
                        config.MinWifiUptimeSeconds = ParseInt(value, config.MinWifiUptimeSeconds);
                        break;
                    case "closeaction":
                        if (Enum.TryParse<CloseAction>(value, ignoreCase: true, out var closeAction))
                        {
                            config.CloseAction = closeAction;
                        }
                        break;
                }
            }

            return config;
        }

        /// <summary>
        /// Writes the current settings to the specified file.
        /// </summary>
        public void Save(string path)
        {
            File.WriteAllLines(path,
            [
                $"ethernetname={EthernetName}",
                $"wifiname={WiFiName}",
                $"pingtarget={PingTarget}",
                $"checkintervalseconds={CheckIntervalSeconds}",
                $"failurethreshold={FailureThreshold}",
                $"successthreshold={SuccessThreshold}",
                $"minwifiuptimeseconds={MinWifiUptimeSeconds}",
                $"closeaction={CloseAction}"
            ]);
        }

        private static int ParseInt(string text, int fallback) =>
            int.TryParse(text, out var n) ? n : fallback;
    }
}
