namespace NetShift.Core
{
    public class Config
    {
        public string EthernetName { get; set; } = "Ethernet";
        public string WiFiName { get; set; } = "Wi-Fi";
        public string PingTarget { get; set; } = "8.8.8.8";
        public int CheckIntervalSeconds { get; set; } = 15;
        public int FailureThreshold { get; set; } = 3;
        public int SuccessThreshold { get; set; } = 2;
        public int MinWifiUptimeSeconds { get; set; } = 10;

        public static Config Load(string path)
        {
            var cfg = new Config();

            if (!File.Exists(path))
            {
                Console.WriteLine($".cfg file '{path}' not found - using default values.");
                return cfg;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split("=", 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;

                switch (parts[0].ToLower())
                {
                    case "ethernetname": cfg.EthernetName = parts[1]; break;
                    case "wifiname": cfg.WiFiName = parts[1]; break;
                    case "pingtarget": cfg.PingTarget = parts[1]; break;
                    case "checkintervalseconds":
                        if (int.TryParse(parts[1], out int ci))
                            cfg.CheckIntervalSeconds = ci;
                        break;
                    case "failurethreshold":
                        if (int.TryParse(parts[1], out int ft))
                            cfg.FailureThreshold = ft;
                        break;
                    case "successthreshold":
                        if (int.TryParse(parts[1], out int st))
                            cfg.SuccessThreshold = st;
                        break;
                    case "minwifiuptimeseconds":
                        if (int.TryParse(parts[1], out int ms))
                            cfg.MinWifiUptimeSeconds = ms;
                        break;
                }
            }

            return cfg;
        }
    }
}
