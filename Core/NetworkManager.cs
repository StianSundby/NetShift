using NetShiftST.Utils;

namespace NetShiftST.Core
{
    public enum IconState { Offline, Ethernet, WiFi }
    public class NetworkManager
    {
        #region Properties
        private readonly Config _config;
        private bool _onEthernet = true;
        private int _failures; //consecutive failed pings
        private int _successes; //consecutive successful pings
        private DateTime _wifiSince = DateTime.MinValue;
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private bool _preventAutoSwitching;
        public event Action<bool>? PreventAutoSwitchingChanged;
        public event Action<string, string>? StatusChanged;
        public event Action<IconState>? IconChanged;
        private bool _internetLocked;
        private bool _restoreEthernet = true;
        public bool IsInternetLocked => _internetLocked;
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

        public async Task CheckAsync()
        {
            if (_internetLocked)
                return;

            if (PreventAutoSwitching)
            {
                Logger.Log("Auto-switching prevented by user.");
                IconChanged?.Invoke(_onEthernet ? IconState.Ethernet : IconState.WiFi);
                return;
            }

            if (!await _operationLock.WaitAsync(0))
                return;

            try
            {
                if (_internetLocked)
                    return;

                bool online = await NetworkTools.PingAsync(_config.PingTarget);

                Logger.Log(
                    $"Ping to {_config.PingTarget}: {(online ? "Success" : "Failed")}");

                if (online)
                    await OnPingSucceededAsync();
                else
                    await OnPingFailedAsync();
            }
            finally
            {
                _operationLock.Release();
            }
        }

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

        public Task ForceEthernetAsync() => ForceAsync(ethernet: true);
        public Task ForceWiFiAsync() => ForceAsync(ethernet: false);
        private async Task ForceAsync(bool ethernet)
        {
            if (_internetLocked)
            {
                StatusChanged?.Invoke(
                    "Internet Locked",
                    "Complete the unlock challenge to restore internet.");

                return;
            }

            await _operationLock.WaitAsync();

            try
            {
                if (_internetLocked)
                    return;

                await SwitchToAsync(ethernet);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private async Task SwitchToAsync(bool ethernet)
        {
            string from = ethernet ? "Wi-Fi" : "Ethernet";
            string to = ethernet ? "Ethernet" : "Wi-Fi";

            string adapterToEnable = ethernet ? _config.EthernetName : _config.WiFiName;
            string adapterToDisable = ethernet ? _config.WiFiName : _config.EthernetName;

            StatusChanged?.Invoke("Switching Network", $"Switching from {from} to {to}...");
            NetworkTools.LogAdapters($"before enabling {to}");

            bool enabled = await NetworkTools.EnableAdapterAsync(adapterToEnable);

            if (!enabled)
            {
                Logger.Log($"Failed to enable adapter '{adapterToEnable}'.");
                StatusChanged?.Invoke("Network Switch Failed", $"Could not enable {to}.");
                return;
            }

            bool adapterIsUp = await NetworkTools.WaitUntilUpAsync(adapterToEnable);

            if (!adapterIsUp)
            {
                Logger.Log($"Adapter '{adapterToEnable}' did not come up.");
                StatusChanged?.Invoke("Network Switch Failed", $"{to} did not become available.");
                return;
            }

            bool disabled = await NetworkTools.DisableAdapterAsync(adapterToDisable);

            if (!disabled)
            {
                Logger.Log($"Failed to disable adapter '{adapterToDisable}'.");
                StatusChanged?.Invoke("Network Switch Warning", $"{to} was enabled, but {from} could not be disabled.");
            }

            _onEthernet = ethernet;
            _wifiSince = ethernet ? DateTime.MinValue : DateTime.UtcNow;
            _failures = 0;
            _successes = 0;

            Logger.Log($"Switched to {to}");
            NetworkTools.LogAdapters($"after switching to {to}");
            IconChanged?.Invoke(ethernet ? IconState.Ethernet : IconState.WiFi);
            StatusChanged?.Invoke("Active Network", $"Now using {to}");
        }

        public async Task DisableInternetAsync()
        {
            await _operationLock.WaitAsync();

            try
            {
                if (_internetLocked)
                    return;

                _internetLocked = true;
                _restoreEthernet = _onEthernet;

                Logger.Log("Internet cutoff activated.");

                bool ethernetDisabled =
                    await NetworkTools.DisableAdapterAsync(_config.EthernetName);

                bool wifiDisabled =
                    await NetworkTools.DisableAdapterAsync(_config.WiFiName);

                if (!ethernetDisabled || !wifiDisabled)
                {
                    Logger.Log(
                        $"Cutoff adapter results: Ethernet={ethernetDisabled}, Wi-Fi={wifiDisabled}");
                }

                _failures = 0;
                _successes = 0;

                IconChanged?.Invoke(IconState.Offline);
                StatusChanged?.Invoke(
                    "Internet Locked",
                    "Internet cutoff is active.");
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task RestoreInternetAsync()
        {
            await _operationLock.WaitAsync();

            try
            {
                if (!_internetLocked)
                    return;

                string adapterToRestore =
                    _restoreEthernet
                        ? _config.EthernetName
                        : _config.WiFiName;

                Logger.Log(
                    $"Restoring internet using '{adapterToRestore}'.");

                bool enabled =
                    await NetworkTools.EnableAdapterAsync(adapterToRestore);

                if (!enabled)
                    throw new InvalidOperationException(
                        $"Failed to enable adapter '{adapterToRestore}'.");

                bool adapterIsUp =
                    await NetworkTools.WaitUntilUpAsync(adapterToRestore);

                if (!adapterIsUp)
                    throw new InvalidOperationException(
                        $"Adapter '{adapterToRestore}' did not come up.");

                _onEthernet = _restoreEthernet;
                _wifiSince =
                    _onEthernet ? DateTime.MinValue : DateTime.UtcNow;

                _failures = 0;
                _successes = 0;
                _internetLocked = false;

                IconChanged?.Invoke(
                    _onEthernet
                        ? IconState.Ethernet
                        : IconState.WiFi);

                StatusChanged?.Invoke(
                    "Internet Restored",
                    $"Restored {(_onEthernet ? "Ethernet" : "Wi-Fi")}.");
            }
            finally
            {
                _operationLock.Release();
            }
        }

    }
}
