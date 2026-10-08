using NetShiftST.Utils;

namespace NetShiftST.Core
{
    public enum IconState { Offline, Ethernet, WiFi }

    /// <summary>
    /// Monitors connectivity and switches between the configured Ethernet and Wi-Fi adapters
    /// </summary>
    public class NetworkManager
    {
        #region Properties
        private readonly Config _config;
        private bool _onEthernet = true;
        private int _failures; //consecutive failed pings
        private int _successes; //consecutive successful pings
        private DateTime _wifiSince = DateTime.MinValue;
        private bool _busy;

        private bool _preventAutoSwitching;
        public event Action<bool>? PreventAutoSwitchingChanged;

        public event Action<string, string>? StatusChanged;
        public event Action<IconState>? IconChanged;
        #endregion

        public NetworkManager(Config config)
        {
            _config = config;
        }

        public bool PreventAutoSwitching
        {
            get => _preventAutoSwitching;
            set
            {
                if (_preventAutoSwitching == value)
                    return;

                _preventAutoSwitching = value;
                PreventAutoSwitchingChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// Initializes the displayed network state from adapter status and a connectivity probe
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                NetworkTools.LogAdapters("startup");

                bool ethUp = NetworkTools.IsUp(_config.EthernetName);
                bool wifiUp = NetworkTools.IsUp(_config.WiFiName);
                bool online = await NetworkTools.PingAsync(_config.PingTarget);

                if (ethUp && online) UseEthernet();
                else if (wifiUp && online) UseWiFi();
                else if (ethUp) UseEthernet();
                else if (wifiUp) UseWiFi();
                else IconChanged?.Invoke(IconState.Offline);
            }
            catch (Exception ex)
            {
                Logger.Log($"InitializeAsync error: {ex.Message}");
                IconChanged?.Invoke(IconState.Offline);
            }
        }

        /// <summary>
        /// Checks connectivity and applies failure, success and minimum Wi-Fi uptime thresholds.
        /// Skips checks while busy or automatic switching is prevented
        /// </summary>
        public async Task CheckAsync()
        {
            if (PreventAutoSwitching)
            {
                Logger.Log("Auto-switching prevented by user.");
                IconChanged?.Invoke(_onEthernet ? IconState.Ethernet : IconState.WiFi);
                return;
            }

            if (_busy) 
                return;
            _busy = true;

            try
            {
                bool online = await NetworkTools.PingAsync(_config.PingTarget);
                Logger.Log($"Ping to {_config.PingTarget}: {(online ? "Success" : "Failed")}");

                if (online) await OnPingSucceededAsync();
                else await OnPingFailedAsync();
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Requests a switch to Ethernet. The request is skipped while another operation is busy
        /// </summary>
        public Task ForceEthernetAsync() => ForceAsync(ethernet: true);

        /// <summary>
        /// Requests a switch to Wi-Fi. The request is skipped while another operation is busy
        /// </summary>
        public Task ForceWiFiAsync() => ForceAsync(ethernet: false);

        private async Task OnPingFailedAsync()
        {
            _failures++;
            _successes = 0;

            Logger.Log($"Consecutive failures: {_failures}");

            if (!_onEthernet ||
                _failures < Math.Max(1, _config.FailureThreshold))
            {
                IconChanged?.Invoke(IconState.Offline);
                return;
            }

            await SwitchToAsync(ethernet: false);
        }

        private async Task OnPingSucceededAsync()
        {
            _successes++;
            _failures = 0;

            Logger.Log($"Consecutive successes: {_successes}");

            if (_onEthernet)
            {
                IconChanged?.Invoke(IconState.Ethernet);
                return;
            }

            var timeOnWifi = DateTime.UtcNow - _wifiSince;

            var minWifiUptime = TimeSpan.FromSeconds(
                Math.Max(0, _config.MinWifiUptimeSeconds));

            if (_successes < Math.Max(1, _config.SuccessThreshold) ||
                timeOnWifi < minWifiUptime)
            {
                IconChanged?.Invoke(IconState.WiFi);

                if (timeOnWifi < minWifiUptime)
                {
                    Logger.Log(
                        $"Waiting for min Wi-Fi uptime " +
                        $"({timeOnWifi.TotalSeconds:F1}s / " +
                        $"{minWifiUptime.TotalSeconds}s)");
                }

                return;
            }

            await SwitchToAsync(ethernet: true);
        }

        private async Task ForceAsync(bool ethernet)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await SwitchToAsync(ethernet);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task SwitchToAsync(bool ethernet)
        {
            string from = ethernet ? "Wi-Fi" : "Ethernet";
            string to = ethernet ? "Ethernet" : "Wi-Fi";

            string adapterToEnable =
                ethernet ? _config.EthernetName : _config.WiFiName;

            string adapterToDisable =
                ethernet ? _config.WiFiName : _config.EthernetName;

            StatusChanged?.Invoke(
                "Switching Network",
                $"Switching from {from} to {to}...");

            NetworkTools.LogAdapters($"before enabling {to}");

            await NetworkTools.EnableAdapterAsync(adapterToEnable);

            bool adapterIsUp =
                await NetworkTools.WaitUntilUpAsync(adapterToEnable);

            if (!adapterIsUp)
            {
                Logger.Log(
                    $"Failed to switch to {to}: " +
                    $"adapter '{adapterToEnable}' did not come up.");

                StatusChanged?.Invoke(
                    "Network Switch Failed",
                    $"Could not switch to {to}.");

                return;
            }

            await NetworkTools.DisableAdapterAsync(adapterToDisable);

            _onEthernet = ethernet;
            _wifiSince = ethernet
                ? DateTime.MinValue
                : DateTime.UtcNow;

            _failures = 0;
            _successes = 0;

            Logger.Log($"Switched to {to}");

            IconChanged?.Invoke(
                ethernet ? IconState.Ethernet : IconState.WiFi);

            StatusChanged?.Invoke(
                "Active Network",
                $"Now using {to}");
        }

        private void UseEthernet()
        {
            _onEthernet = true;
            IconChanged?.Invoke(IconState.Ethernet);
        }

        private void UseWiFi()
        {
            _onEthernet = false;
            _wifiSince = DateTime.UtcNow;
            IconChanged?.Invoke(IconState.WiFi);
        }
    }
}
