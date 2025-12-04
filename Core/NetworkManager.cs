using System.Diagnostics;
using System.Net.NetworkInformation;
using NetShift.Utils;

namespace NetShift.Core
{
    public class NetworkManager
    {
        private readonly Config _config;
        private volatile bool _ethernetActive = true;
        private int _isSwitching = 0;
        private readonly TimeSpan _pingTimeout = TimeSpan.FromMilliseconds(2000);

        //debounce/hysteresis state
        private int _consecutiveFailures = 0;
        private int _consecutiveSuccesses = 0;
        private readonly int _failureThreshold;
        private readonly int _successThreshold;
        private readonly TimeSpan _minWifiUptime;
        private DateTime _lastSwitchedToWifi = DateTime.MinValue;

        private volatile bool _preventAutoSwitching = false;
        public bool PreventAutoSwitching
        {
            get => _preventAutoSwitching;
            set => _preventAutoSwitching = value;
        }

        public event Action<string, string>? StatusChanged;
        public event Action<string>? IconChanged;

        public NetworkManager(Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _failureThreshold = Math.Max(1, _config.FailureThreshold);
            _successThreshold = Math.Max(1, _config.SuccessThreshold);
            _minWifiUptime = TimeSpan.FromSeconds(Math.Max(0, _config.MinWifiUptimeSeconds));
        }

        public async Task MonitorNetworkAsync(CancellationToken cancellationToken = default)
        {
            if (_preventAutoSwitching)
            {
                Logger.Log("Auto-switching prevented by user.");
                IconChanged?.Invoke(_ethernetActive ? "ethernet" : "wifi");
                return;
            }

            //ensure only one switch attempt at a time
            if (Interlocked.CompareExchange(ref _isSwitching, 1, 0) == 1)
                return;

            try
            {
                var internetOk = await TestInternetAsync(cancellationToken).ConfigureAwait(false);
                Logger.Log($"Ping to {_config.PingTarget}: {(internetOk ? "Success" : "Failed")}");

                if (!internetOk)
                {
                    await HandleInternetFailureAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                await HandleInternetSuccessAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _isSwitching, 0);
            }
        }

        private async Task HandleInternetFailureAsync(CancellationToken cancellationToken)
        {
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;
            Logger.Log($"Consecutive failures: {_consecutiveFailures}");

            if (!_ethernetActive || _consecutiveFailures < _failureThreshold)
            {
                IconChanged?.Invoke("none");
                return;
            }

            //threshold reached, switch to Wi-Fi
            StatusChanged?.Invoke("Switching Network", "Switching from Ethernet to Wi-Fi...");
            await SwitchToWiFi(cancellationToken).ConfigureAwait(false);
            _lastSwitchedToWifi = DateTime.UtcNow;
            IconChanged?.Invoke("wifi");
            StatusChanged?.Invoke("Active Network", "Now using Wi-Fi");

            _consecutiveFailures = 0;
            _consecutiveSuccesses = 0;
        }

        private async Task HandleInternetSuccessAsync(CancellationToken cancellationToken)
        {
            _consecutiveSuccesses++;
            _consecutiveFailures = 0;
            Logger.Log($"Consecutive successes: {_consecutiveSuccesses}");

            if (_ethernetActive)
            {
                IconChanged?.Invoke("ethernet");
                return;
            }

            //make sure we've been on Wi‑Fi long enough and have enough successes
            var timeOnWifiOk = (DateTime.UtcNow - _lastSwitchedToWifi) >= _minWifiUptime;
            if (_consecutiveSuccesses < _successThreshold || !timeOnWifiOk)
            {
                IconChanged?.Invoke("wifi");
                if (!timeOnWifiOk)
                {
                    Logger.Log($"Waiting min Wi‑Fi uptime before switching back to Ethernet (elapsed={(DateTime.UtcNow - _lastSwitchedToWifi).TotalSeconds:F1}s / required={_minWifiUptime.TotalSeconds}s)");
                }
                return;
            }

            //conditions satisfied, switch back to Ethernet
            StatusChanged?.Invoke("Switching Network", "Switching from Wi-Fi to Ethernet...");
            await SwitchToEthernet(cancellationToken).ConfigureAwait(false);
            IconChanged?.Invoke("ethernet");
            StatusChanged?.Invoke("Active Network", "Now using Ethernet");

            _consecutiveSuccesses = 0;
            _consecutiveFailures = 0;
        }

        public async Task ForceEthernet(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _isSwitching, 1, 0) == 1) return;
            try
            {
                await SwitchToEthernet(cancellationToken).ConfigureAwait(false);
                _consecutiveFailures = 0;
                _consecutiveSuccesses = 0;
                _lastSwitchedToWifi = DateTime.MinValue;
            }
            finally
            {
                Interlocked.Exchange(ref _isSwitching, 0);
            }
        }

        public async Task ForceWiFi(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _isSwitching, 1, 0) == 1) return;
            try
            {
                await SwitchToWiFi(cancellationToken).ConfigureAwait(false);
                _lastSwitchedToWifi = DateTime.UtcNow;
                _consecutiveFailures = 0;
                _consecutiveSuccesses = 0;
            }
            finally
            {
                Interlocked.Exchange(ref _isSwitching, 0);
            }
        }

        private async Task SwitchToEthernet(CancellationToken cancellationToken)
        {
            LogAdapterStatus("before enabling Ethernet");
            await EnableAdapterAsync(_config.EthernetName, cancellationToken).ConfigureAwait(false);
            await WaitForAdapterStatusAsync(_config.EthernetName, OperationalStatus.Up, cancellationToken).ConfigureAwait(false);
            await DisableAdapterAsync(_config.WiFiName, cancellationToken).ConfigureAwait(false);
            _ethernetActive = true;
            Logger.Log("Switched to Ethernet (connection restored)");
        }

        private async Task SwitchToWiFi(CancellationToken cancellationToken)
        {
            LogAdapterStatus("before enabling Wi-Fi");
            await EnableAdapterAsync(_config.WiFiName, cancellationToken).ConfigureAwait(false);
            await WaitForAdapterStatusAsync(_config.WiFiName, OperationalStatus.Up, cancellationToken).ConfigureAwait(false);
            await DisableAdapterAsync(_config.EthernetName, cancellationToken).ConfigureAwait(false);
            _ethernetActive = false;
            Logger.Log("Switched to Wi-Fi (Ethernet offline)");
        }

        public void LogAdapterStatus(string step = "")
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                Logger.Log($"--- Adapter status {step} ---");
                foreach (var adapter in adapters)
                    Logger.Log($"Adapter: {adapter.Name}, Type: {adapter.NetworkInterfaceType}, Status: {adapter.OperationalStatus}");
                Logger.Log("---------------------------------");
            }
            catch (Exception ex) { Logger.Log($"Error checking adapter status: {ex.Message}"); }
        }

        private static async Task RunNetshCommandAsync(string args, CancellationToken cancellationToken)
        {
            Logger.Log($"Executing: netsh {args}");
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false, //capture output
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.Start();

                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waitTask = proc.WaitForExitAsync(linked.Token);

                var completed = await Task.WhenAny(waitTask).ConfigureAwait(false);
                if (completed != waitTask)
                    try { linked.Cancel(); } catch { }

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                Logger.Log($"Completed: netsh {args} (Exit code: {proc.ExitCode})");
                if (!string.IsNullOrWhiteSpace(output)) Logger.Log($"netsh output: {output.Trim()}");
                if (!string.IsNullOrWhiteSpace(error)) Logger.Log($"netsh error: {error.Trim()}");
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"netsh {args} canceled.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error running netsh {args}: {ex.Message}");
            }
        }

        private async Task<bool> TestInternetAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                //Ping.SendPingAsync doesn't accept CancellationToken directly; rely on timeout + cancellation
                var reply = await ping.SendPingAsync(_config.PingTarget, (int)_pingTimeout.TotalMilliseconds).ConfigureAwait(false);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                Logger.Log($"Ping failed: {ex.Message}");
                return false;
            }
        }

        private static async Task WaitForAdapterStatusAsync(string adapterName, OperationalStatus desiredStatus, CancellationToken cancellationToken, int timeoutMs = 10000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(a => a.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));

                if (adapter != null && adapter.OperationalStatus == desiredStatus)
                    return;

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            Logger.Log($"Timeout waiting for adapter '{adapterName}' to reach {desiredStatus}");
        }

        private static Task EnableAdapterAsync(string name, CancellationToken cancellationToken) => RunNetshCommandAsync($"interface set interface \"{name}\" admin=enabled", cancellationToken);
        private static Task DisableAdapterAsync(string name, CancellationToken cancellationToken) => RunNetshCommandAsync($"interface set interface \"{name}\" admin=disabled", cancellationToken);

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                LogAdaptersInfo();

                var (ethUp, wifiUp) = GetAdapterUpStates();
                var internetOk = await TestInternetAsync(cancellationToken).ConfigureAwait(false);

                ApplyInitialState(ethUp, wifiUp, internetOk);
            }
            catch (Exception ex)
            {
                Logger.Log($"InitializeAsync error: {ex.Message}");
                IconChanged?.Invoke("none");
            }
        }

        private static void LogAdaptersInfo()
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var a in adapters)
                Logger.Log($"Adapter found: Name='{a.Name}' Description='{a.Description}' Type={a.NetworkInterfaceType} Status={a.OperationalStatus}");
        }

        private (bool ethUp, bool wifiUp) GetAdapterUpStates()
        {
            var ethernetAdapter = FindAdapterByConfiguredName(_config.EthernetName);
            var wifiAdapter = FindAdapterByConfiguredName(_config.WiFiName);

            var ethernetUp = ethernetAdapter != null && ethernetAdapter.OperationalStatus == OperationalStatus.Up;
            var wifiUp = wifiAdapter != null && wifiAdapter.OperationalStatus == OperationalStatus.Up;

            return (ethernetUp, wifiUp);
        }

        private void ApplyInitialState(bool ethUp, bool wifiUp, bool internetOk)
        {
            if (ethUp && internetOk) { SetActiveEthernet(); return; }
            if (wifiUp && internetOk) { SetActiveWiFi(); return; }
            if (ethUp) { SetActiveEthernet(); return; }
            if (wifiUp) { SetActiveWiFi(); return; }

            IconChanged?.Invoke("none");
        }

        private void SetActiveEthernet()
        {
            _ethernetActive = true;
            IconChanged?.Invoke("ethernet");
        }

        private void SetActiveWiFi()
        {
            _ethernetActive = false;
            IconChanged?.Invoke("wifi");
            _lastSwitchedToWifi = DateTime.UtcNow;
        }

        private static NetworkInterface? FindAdapterByConfiguredName(string configuredName)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                return null;

            var adapters = NetworkInterface.GetAllNetworkInterfaces();

            var match = adapters.FirstOrDefault(a => a.Name.Equals(configuredName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            match = adapters.FirstOrDefault(a => a.Name.Contains(configuredName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            match = adapters.FirstOrDefault(a => a.Description.Contains(configuredName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            return null;
        }
    }
}
