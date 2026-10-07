using System.Collections.ObjectModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using System.Windows;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using NetShiftST.Core;
using NetShiftST.Utils;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NetShiftST
{
    /// <summary>
    /// Displays network controls, settings, live charts, and window modes.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Config _config;
        private readonly NetworkManager _network;
        private readonly StartupManager _startup;
        private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "settings.cfg");

        private readonly DispatcherTimer _networkTimer;
        private readonly DispatcherTimer _feedbackTimer;

        private readonly ObservableCollection<ObservableValue> _downloadValues = [];
        private readonly ObservableCollection<ObservableValue> _uploadValues = [];
        private readonly ObservableCollection<ObservableValue> _pingValues = [];
        private NetworkInterface? _currentAdapter;
        private long _previousBytesReceived;
        private long _previousBytesSent;
        private IconState _currentNetworkState = IconState.Offline;
        private double? _currentPing;

        private bool _isLoadingUI = true;
        private bool _allowClose;
        private bool _monitorMode;

        public MainWindow(Config config, NetworkManager network, StartupManager startup)
        {
            _config = config;
            _network = network;
            _startup = startup;

            InitializeComponent();

            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

            _feedbackTimer.Tick += (_, _) =>
            {
                _feedbackTimer.Stop();
                FeedbackText.Text = "";
            };

            _network.PreventAutoSwitchingChanged += OnPreventAutoSwitchingChanged;

            LoadConfigIntoUI();
            SetUpCharts();

            _network.StatusChanged += OnNetworkStatusChanged;
            _network.IconChanged += OnNetworkStateChanged;

            _networkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _networkTimer.Tick += NetworkTimer_Tick;
            _networkTimer.Start();
        }

        #region Initialization

        private void LoadConfigIntoUI()
        {
            _isLoadingUI = true;

            try
            {
                WiFiNameTextBox.Text = _config.WiFiName;
                EthernetNameTextBox.Text = _config.EthernetName;
                PingTargetTextBox.Text = _config.PingTarget;

                CheckIntervalTextBox.Text = _config.CheckIntervalSeconds.ToString();
                FailureThresholdTextBox.Text = _config.FailureThreshold.ToString();
                SuccessThresholdTextBox.Text = _config.SuccessThreshold.ToString();
                MinimumWiFiUptimeTextBox.Text = _config.MinWifiUptimeSeconds.ToString();
                PreventAutoSwitchingCheckBox.IsChecked = _network.PreventAutoSwitching;
                StartWithWindowsCheckBox.IsChecked = _startup.IsStartupEnabled();
            }
            finally
            {
                _isLoadingUI = false;
            }
        }

        private void SetUpCharts()
        {
            ThroughputChart.Series =
            [
                new LineSeries<ObservableValue>
                {
                    Name = "Download",
                    Values = _downloadValues,
                    GeometrySize = 0,
                    Fill = null
                },

                new LineSeries<ObservableValue>
                {
                    Name = "Upload",
                    Values = _uploadValues,
                    GeometrySize = 0,
                    Fill = null
                }
             ];

            ThroughputChart.XAxes =
            [
                new Axis
                {
                    IsVisible = false
                }
            ];

            ThroughputChart.YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    MinStep = 1,
                    Labeler = value => $"{value:0.#}"
                }
            ];

            PingChart.Series =
            [
                new LineSeries<ObservableValue>
                {
                    Name = "Ping",
                    Values = _pingValues,
                    GeometrySize = 0,
                    Fill = null
                }
            ];

            PingChart.XAxes =
            [
                new Axis
                {
                    IsVisible = false
                }
            ];

            PingChart.YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    MinStep = 5,
                    Labeler = value => $"{value:0}"
                }
            ];
        }

        #endregion

        #region Settings

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CheckIntervalTextBox.Text, out var checkInterval) ||
                !int.TryParse(FailureThresholdTextBox.Text, out var failureThreshold) ||
                !int.TryParse(SuccessThresholdTextBox.Text, out var successThreshold) ||
                !int.TryParse(MinimumWiFiUptimeTextBox.Text, out var minWifiUptime) ||
                checkInterval < 1 ||
                failureThreshold < 1 ||
                successThreshold < 1 ||
                minWifiUptime < 0)
            {
                MessageBox.Show(
                    "Check that all numeric settings contain valid values.",
                    "Invalid settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _config.WiFiName = WiFiNameTextBox.Text.Trim();
            _config.EthernetName = EthernetNameTextBox.Text.Trim();
            _config.PingTarget = PingTargetTextBox.Text.Trim();

            _config.CheckIntervalSeconds = checkInterval;
            _config.FailureThreshold = failureThreshold;
            _config.SuccessThreshold = successThreshold;
            _config.MinWifiUptimeSeconds = minWifiUptime;

            _config.Save(_configPath);
            UpdateSaveButtonVisibility();
            ShowFeedback("Settings saved");
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUI)
                return;

            UpdateSaveButtonVisibility();
        }

        private void UpdateSaveButtonVisibility()
        {
            bool hasChanges =
                WiFiNameTextBox.Text != _config.WiFiName ||
                EthernetNameTextBox.Text != _config.EthernetName ||
                PingTargetTextBox.Text != _config.PingTarget ||
                CheckIntervalTextBox.Text != _config.CheckIntervalSeconds.ToString() ||
                FailureThresholdTextBox.Text != _config.FailureThreshold.ToString() ||
                SuccessThresholdTextBox.Text != _config.SuccessThreshold.ToString() ||
                MinimumWiFiUptimeTextBox.Text != _config.MinWifiUptimeSeconds.ToString();

            SaveButton.Visibility = hasChanges ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PreventAutoSwitching_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUI)
                return;

            _network.PreventAutoSwitching = PreventAutoSwitchingCheckBox.IsChecked == true;
            ShowFeedback(_network.PreventAutoSwitching ? "Automatic switching disabled" : "Automatic switching enabled");
        }

        private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUI)
                return;

            bool enabled = StartWithWindowsCheckBox.IsChecked == true;

            if (!_startup.SetStartupEnabled(enabled))
            {
                _isLoadingUI = true;
                StartWithWindowsCheckBox.IsChecked = !enabled;
                _isLoadingUI = false;

                MessageBox.Show(
                    "Failed to update the Windows startup setting.",
                    "NetShift",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
                ShowFeedback(enabled ? "Start with Windows enabled" : "Start with Windows disabled");
        }

        private void OnPreventAutoSwitchingChanged(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                _isLoadingUI = true;

                try
                {
                    PreventAutoSwitchingCheckBox.IsChecked = enabled;
                }
                finally
                {
                    _isLoadingUI = false;
                }
            });
        }

        private void OnStartupChanged(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                _isLoadingUI = true;

                try
                {
                    StartWithWindowsCheckBox.IsChecked = enabled;
                }
                finally
                {
                    _isLoadingUI = false;
                }
            });
        }

        #endregion

        #region Monitoring and feedback

        private async void NetworkTimer_Tick(object? sender, EventArgs e)
        {
            UpdateThroughput();
            await UpdatePingAsync();
        }

        private NetworkInterface? FindActiveAdapter()
        {
            var ethernet = NetworkTools.FindAdapter(_config.EthernetName);

            if (ethernet?.OperationalStatus == OperationalStatus.Up)
                return ethernet;

            var wifi = NetworkTools.FindAdapter(_config.WiFiName);

            if (wifi?.OperationalStatus == OperationalStatus.Up)
                return wifi;

            return null;
        }

        private void UpdateThroughput()
        {
            var activeAdapter = FindActiveAdapter();

            if (activeAdapter == null)
                return;

            if (_currentAdapter?.Id != activeAdapter.Id)
            {
                _currentAdapter = activeAdapter;

                var initialStats = _currentAdapter.GetIPv4Statistics();
                _previousBytesReceived = initialStats.BytesReceived;
                _previousBytesSent = initialStats.BytesSent;

                return;
            }

            var stats = _currentAdapter.GetIPv4Statistics();
            long received = stats.BytesReceived;
            long sent = stats.BytesSent;
            double downloadMbps = (received - _previousBytesReceived) * 8.0 / 1000000.0;
            double uploadMbps = (sent - _previousBytesSent) * 8.0 / 1000000.0;

            AddValue(_downloadValues, downloadMbps);
            AddValue(_uploadValues, uploadMbps);

            _previousBytesReceived = received;
            _previousBytesSent = sent;
        }

        private static void AddValue(ObservableCollection<ObservableValue> collection, double value)
        {
            collection.Add(new ObservableValue(value));
            if (collection.Count > 60)
                collection.RemoveAt(0);
        }

        private async Task UpdatePingAsync()
        {
            string target = _config.PingTarget;

            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(target, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    _currentPing = reply.RoundtripTime;
                    AddValue(_pingValues, reply.RoundtripTime);
                }
                else
                {
                    _currentPing = null;
                    AddValue(_pingValues, 0);
                }

                UpdateMonitorStatus();
            }
            catch
            {
                _currentPing = null;
                AddValue(_pingValues, 0);
                UpdateMonitorStatus();
            }
        }

        private void UpdateMonitorStatus()
        {
            string network = _currentNetworkState switch
            {
                IconState.Ethernet => "Ethernet",
                IconState.WiFi => "Wi-Fi",
                _ => "Offline"
            };

            string ping = _currentPing.HasValue ? $"{_currentPing.Value:0} ms" : "-- ms";
            MonitorStatusText.Text = $"{network}  •  {ping}";
        }

        private void OnNetworkStateChanged(IconState state)
        {
            Dispatcher.Invoke(() =>
            {
                _currentNetworkState = state;

                switch (state)
                {
                    case IconState.Ethernet:
                        NetworkStatusText.Text = "Ethernet connected";
                        NetworkDetailText.Text = _config.EthernetName;
                        break;

                    case IconState.WiFi:
                        NetworkStatusText.Text = "Wi-Fi connected";
                        NetworkDetailText.Text = _config.WiFiName;
                        break;

                    case IconState.Offline:
                        NetworkStatusText.Text = "No connection";
                        NetworkDetailText.Text = "Waiting for network...";
                        break;
                }

                UpdateMonitorStatus();
            });
        }

        private void OnNetworkStatusChanged(string title, string message)
        {
            ShowFeedback(message);
        }

        private void ShowFeedback(string message)
        {
            Dispatcher.Invoke(() =>
            {
                FeedbackText.Text = message;

                _feedbackTimer.Stop();
                _feedbackTimer.Start();
            });
        }

        #endregion

        #region Network actions

        private async void ForceEthernetButton_Click(object sender, RoutedEventArgs e)
        {
            await _network.ForceEthernetAsync();
        }

        private async void ForceWiFiButton_Click(object sender, RoutedEventArgs e)
        {
            await _network.ForceWiFiAsync();
        }

        #endregion

        #region Window controls

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MonitorModeButton_Click(object sender, RoutedEventArgs e)
        {
            EnterMonitorMode();
        }

        private void FullViewButton_Click(object sender, RoutedEventArgs e)
        {
            ExitMonitorMode();
        }

        private void EnterMonitorMode()
        {
            if (_monitorMode)
                return;

            _monitorMode = true;

            MainPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            NormalWindowControls.Visibility = Visibility.Collapsed;

            MonitorHeader.Visibility = Visibility.Visible;

            MainColumn.Width = new GridLength(0);
            SettingsColumn.Width = new GridLength(0);
            GraphsColumn.Width = new GridLength(1, GridUnitType.Star);

            Width = 700;
            Height = 500;
            MinWidth = 700;
            MinHeight = 500;
            MonitorHeaderRow.Height = GridLength.Auto;

            UpdateMonitorStatus();
        }

        private void ExitMonitorMode()
        {
            if (!_monitorMode)
                return;

            _monitorMode = false;

            MainPanel.Visibility = Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Visible;
            NormalWindowControls.Visibility = Visibility.Visible;

            MonitorHeader.Visibility = Visibility.Collapsed;

            MainColumn.Width = new GridLength(170);
            SettingsColumn.Width = new GridLength(250);
            GraphsColumn.Width = new GridLength(1, GridUnitType.Star);

            Width = 900;
            Height = 440;
            MinWidth = 900;
            MinHeight = 440;

            MonitorHeaderRow.Height = new GridLength(0);
        }

        private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            AlwaysOnTopButton.Content = Topmost ? "Unpin" : "Pin";
        }

        private void MonitorExitButton_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });

            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Lifecycle

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            switch (_config.CloseAction)
            {
                case CloseAction.MinimizeToTray:
                    Hide();
                    return;

                case CloseAction.Exit:
                    ExitApplication();
                    return;

                case CloseAction.Ask:
                default:
                    ShowCloseConfirmation();
                    return;
            }
        }

        private void ShowCloseConfirmation()
        {
            var dialog = new CloseConfirmationWindow
            {
                Owner = this
            };

            bool? result = dialog.ShowDialog();

            if (result != true)
                return;

            if (dialog.DontAskAgain)
            {
                _config.CloseAction = dialog.SelectedAction;
                _config.Save(_configPath);
            }

            switch (dialog.SelectedAction)
            {
                case CloseAction.MinimizeToTray:
                    Hide();
                    break;

                case CloseAction.Exit:
                    ExitApplication();
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _network.IconChanged -= OnNetworkStateChanged;
            _network.StatusChanged -= OnNetworkStatusChanged;
            _network.PreventAutoSwitchingChanged -= OnPreventAutoSwitchingChanged;
            _startup.StartupChanged -= OnStartupChanged;

            _networkTimer.Stop();
            _feedbackTimer.Stop();

            base.OnClosed(e);
        }

        public void ExitApplication()
        {
            _allowClose = true;
            Application.Current.Shutdown();
        }

        #endregion

    }
}
