using NetShiftST.Utils;
using System.IO;

namespace NetShiftST.Core
{
    public enum CloseAction { Ask, MinimizeToTray, Exit}
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
        public bool InternetCutoffEnabled { get; set; } = false;
        public TimeOnly InternetCutoffTime { get; set; } = new(22, 17);

        public static Config Load(string path)
        {
            var cfg = new Config();

            if (!File.Exists(path))
            {
                Logger.Log($"Config file '{path}' not found - using defaults.");
                return cfg;
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
                    case "ethernetname": cfg.EthernetName = value; break;
                    case "wifiname": cfg.WiFiName = value; break;
                    case "pingtarget": cfg.PingTarget = value; break;
                    case "checkintervalseconds": cfg.CheckIntervalSeconds = ParseInt(value, cfg.CheckIntervalSeconds); break;
                    case "failurethreshold": cfg.FailureThreshold = ParseInt(value, cfg.FailureThreshold); break;
                    case "successthreshold": cfg.SuccessThreshold = ParseInt(value, cfg.SuccessThreshold); break;
                    case "minwifiuptimeseconds": cfg.MinWifiUptimeSeconds = ParseInt(value, cfg.MinWifiUptimeSeconds); break;
                    case "closeaction": if (Enum.TryParse<CloseAction>(value, ignoreCase: true, out var closeAction)) { cfg.CloseAction = closeAction; } break;
                    case "internetcutoffenabled": if (bool.TryParse(value, out var cutoffEnabled)) cfg.InternetCutoffEnabled = cutoffEnabled; break;
                    case "internetcutofftime": if (TimeOnly.TryParse(value, out var cutoffTime)) cfg.InternetCutoffTime = cutoffTime; break;
                }
            }

            return cfg;
        }

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
                $"closeaction={CloseAction}",
                $"internetcutoffenabled={InternetCutoffEnabled}",
                $"internetcutofftime={InternetCutoffTime:HH:mm}"
            ]);
        }

        private static int ParseInt(string text, int fallback) => 
            int.TryParse(text, out var n) ? n : fallback;
    }
}
