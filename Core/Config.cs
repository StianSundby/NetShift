namespace NetShift.Core
{
    public class Config
    {
        public string EthernetName { get; set; } = "Ethernet";
        public string WiFiName { get; set; } = "Wi-Fi";
        public string PingTarget { get; set; } = "8.8.8.8";
        public int CheckIntervalSeconds { get; set; } = 15;

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
                        if (int.TryParse(parts[1], out int sec))
                            cfg.CheckIntervalSeconds = sec;
                        break;
                }
            }

            return cfg;
        }
    }
}
