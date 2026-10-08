using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NetShiftST.Core;
using NetShiftST.Utils;
using System.Net;
using SkiaSharp;
using LiveChartsCore.Measure;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;


namespace NetShiftST
{
    /// <summary>
    /// Displays network controls, settings, live charts, and window modes.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Properties
        private readonly Config _config;
        private readonly NetworkManager _network;
        private readonly StartupManager _startup;
        private readonly string _configPath = AppPaths.ConfigFile;

        private readonly DispatcherTimer _networkTimer;
        private readonly DispatcherTimer _feedbackTimer;

        private readonly ObservableCollection<DateTimePoint> _downloadValues = [];
        private readonly ObservableCollection<DateTimePoint> _uploadValues = [];
        private readonly ObservableCollection<DateTimePoint> _pingValues = [];

        private readonly Axis _throughputTimeAxis = CreateTimeAxis();
        private readonly Axis _pingTimeAxis = CreateTimeAxis();
        private static readonly TimeSpan HistoryDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan LiveDuration = TimeSpan.FromMinutes(1);
        private bool _followLive = true;
        private bool _sampling;

        private NetworkInterface? _currentAdapter;
        private NetworkInterface? _routedAdapter;
        private long _previousBytesReceived;
        private long _previousBytesSent;
        private double? _currentPing;

        private bool _isLoadingUI = true;
        private bool _allowClose;
        private bool _monitorMode;
        #endregion

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
            ThroughputChart.LegendTextPaint = new SolidColorPaint(SKColor.Parse("#A9B8CD"));
            ThroughputChart.Series =
            [
                new LineSeries<DateTimePoint>
                {
                    Name = "Download",
                    Stroke = new SolidColorPaint(SKColor.Parse("#67E8CA"), 2.5f),
                    Values = _downloadValues,
                    GeometrySize = 0,
                    Fill = null,
                    XToolTipLabelFormatter = point => FormatTimestamp(point.Coordinate.SecondaryValue),
                    YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:0.00} Mbps"
                },

                new LineSeries<DateTimePoint>
                {
                    Name = "Upload",
                    Stroke = new SolidColorPaint(SKColor.Parse("#81A7FF"), 2.5f),
                    Values = _uploadValues,
                    GeometrySize = 0,
                    Fill = null,
                    XToolTipLabelFormatter = point => FormatTimestamp(point.Coordinate.SecondaryValue),
                    YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:0.00} Mbps"
                }
            ];

            ThroughputChart.XAxes = [_throughputTimeAxis];

            ThroughputChart.YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#A9B8CD")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2B374A")),
                    TextSize = 11,
                    MinStep = 1,
                    Labeler = value => $"{value:0.#}"
                }
            ];

            PingChart.Series =
            [
                new LineSeries<DateTimePoint>
                {
                    Name = "Ping",
                    Stroke = new SolidColorPaint(SKColor.Parse("#C4A4FF"), 2.5f),
                    Values = _pingValues,
                    GeometrySize = 0,
                    Fill = null,
                    XToolTipLabelFormatter = point => FormatTimestamp(point.Coordinate.SecondaryValue),
                    YToolTipLabelFormatter = point => $"{point.Coordinate.PrimaryValue:0} ms"
                }
            ];

            PingChart.XAxes = [_pingTimeAxis];

            PingChart.YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#A9B8CD")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2B374A")),
                    TextSize = 11,
                    MinStep = 5,
                    Labeler = value => $"{value:0}"
                }
            ];

            ThroughputChart.ZoomMode = ZoomAndPanMode.X;
            PingChart.ZoomMode = ZoomAndPanMode.X;

            UpdateLiveWindow();
        }

        private static Axis CreateTimeAxis()
        {
            return new Axis
            {
                Labeler = value => FormatTimestamp(value),
                UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                MinStep = TimeSpan.FromSeconds(1).Ticks,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#A9B8CD")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2B374A")),
                TextSize = 10
            };
        }

        private static string FormatTimestamp(double ticks)
        {
            if (!double.IsFinite(ticks) || ticks < DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
            {
                return "";
            }

            return new DateTime((long)ticks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");
        }

        private void UpdateLiveWindow()
        {
            if (!_followLive)
                return;

            DateTime now = DateTime.UtcNow;
            double start = (now - LiveDuration).Ticks;
            double end = now.Ticks;

            _throughputTimeAxis.MinLimit = start;
            _throughputTimeAxis.MaxLimit = end;

            _pingTimeAxis.MinLimit = start;
            _pingTimeAxis.MaxLimit = end;
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

        #region Monitoring
        private async void NetworkTimer_Tick(object? sender, EventArgs e)
        {
            if (_sampling)
                return;

            _sampling = true;

            try
            {
                UpdateThroughput();
                await UpdatePingAsync();
                UpdateLiveWindow();
            }
            catch (Exception ex)
            {
                Logger.Log($"Chart sampling failed: {ex.Message}");
            }
            finally
            {
                _sampling = false;
            }
        }

        private NetworkInterface? FindActiveAdapter()
        {
            return _routedAdapter?.OperationalStatus == OperationalStatus.Up ? _routedAdapter : null;
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

            DateTime timestamp = DateTime.UtcNow;

            AddValue(_downloadValues, downloadMbps, timestamp);
            AddValue(_uploadValues, uploadMbps, timestamp);

            _previousBytesReceived = received;
            _previousBytesSent = sent;
        }

        private static void AddValue(ObservableCollection<DateTimePoint> collection, double? value, DateTime? timestamp = null)
        {
            DateTime now = timestamp ?? DateTime.UtcNow;
            collection.Add(new DateTimePoint(now, value));
            DateTime cutoff = now - HistoryDuration;

            while (collection.Count > 0 && collection[0].DateTime < cutoff)
            {
                collection.RemoveAt(0);
            }
        }

        private async Task UpdatePingAsync()
        {
            try
            {
                string target = _config.PingTarget;

                IPAddress address;

                if (IPAddress.TryParse(target, out var parsedAddress))
                {
                    address = parsedAddress;
                }
                else
                {
                    var addresses = await Dns.GetHostAddressesAsync(target);
                    address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? addresses.First();
                }

                _routedAdapter = NetworkTools.FindRoutedAdapter(address);

                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(address, 1000);

                //refresh in case routing changed while the probe was running
                _routedAdapter = NetworkTools.FindRoutedAdapter(address);

                if (reply.Status == IPStatus.Success)
                {
                    _currentPing = reply.RoundtripTime;
                    AddValue(_pingValues, reply.RoundtripTime);
                }
                else
                {
                    _currentPing = null;
                    AddValue(_pingValues, null);
                }

                UpdateDetectedNetworkStatus();
            }
            catch (Exception ex)
            {
                Logger.Log($"Network observation failed: {ex.Message}");

                _currentPing = null;
                _routedAdapter = null;

                AddValue(_pingValues, null);
                UpdateDetectedNetworkStatus();
            }
        }

        private void UpdateMonitorStatus()
        {
            UpdateDetectedNetworkStatus();
        }

        private void OnNetworkStateChanged(IconState state)
        {
            Dispatcher.Invoke(UpdateDetectedNetworkStatus);
        }

        private void UpdateDetectedNetworkStatus()
        {
            if (_routedAdapter == null)
            {
                NetworkStatusText.Text = "Connection unknown";
                NetworkDetailText.Text = "Could not identify the active route.";
                MonitorStatusText.Text = "Unknown  •  -- ms";
                return;
            }

            string networkName;

            switch (_routedAdapter.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Wireless80211:
                    networkName = "Wi-Fi";
                    break;

                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetFx:
                case NetworkInterfaceType.FastEthernetT:
                    networkName = "Ethernet";
                    break;

                default:
                    networkName = _routedAdapter.Name;
                    break;
            }

            NetworkStatusText.Text = _currentPing.HasValue ? $"{networkName} connected" : $"{networkName} — probe failed";
            NetworkDetailText.Text = _routedAdapter.Name;
            string pingText = _currentPing.HasValue ? $"{_currentPing.Value:0} ms" : "-- ms";
            MonitorStatusText.Text = $"{networkName}  •  {pingText}";
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

        private void FullViewButton_Click(object sender, RoutedEventArgs e)
        {
            ExitMonitorMode();
        }

        private void MonitorModeButton_Click(object sender, RoutedEventArgs e)
        {
            EnterMonitorMode();
        }

        private void EnterMonitorMode()
        {
            if (_monitorMode)
                return;

            _monitorMode = true;

            MainPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            NormalWindowControls.Visibility = Visibility.Collapsed;

            GraphArea.SetValue(Grid.ColumnProperty, 0);
            GraphArea.SetValue(Grid.ColumnSpanProperty, 5);

            MainColumn.Width = new GridLength(0);
            SettingsColumn.Width = new GridLength(0);

            RootGrid.Margin = new Thickness(12);

            MonitorHeader.Visibility = Visibility.Visible;
            MonitorHeaderRow.Height = GridLength.Auto;

            MonitorHeader.RowDefinitions.Clear();
            MonitorHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MonitorHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(MonitorStatusText, 0);
            Grid.SetColumn(MonitorStatusText, 0);
            Grid.SetColumnSpan(MonitorStatusText, 2);

            Grid.SetRow(MonitorControls, 1);
            Grid.SetColumn(MonitorControls, 0);
            Grid.SetColumnSpan(MonitorControls, 2);

            MonitorControls.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            MonitorControls.Margin = new Thickness(0, 10, 0, 0);

            MinWidth = 340;
            MinHeight = 420;
            Width = 380;
            Height = 520;

            ResizeMode = ResizeMode.CanResizeWithGrip;

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

            Grid.SetColumn(GraphArea, 4);
            Grid.SetColumnSpan(GraphArea, 1);

            MainColumn.Width = new GridLength(200);
            SettingsColumn.Width = new GridLength(310);
            GraphsColumn.Width = new GridLength(1, GridUnitType.Star);

            RootGrid.Margin = new Thickness(24);

            MonitorHeader.Visibility = Visibility.Collapsed;
            MonitorHeaderRow.Height = new GridLength(0);

            ResizeMode = ResizeMode.NoResize;

            Width = 1120;
            Height = 700;
            MinWidth = 1120;
            MinHeight = 700;
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

        private void Chart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            PauseLiveFollowing();
        }

        private void Chart_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PauseLiveFollowing();
        }

        private void PauseLiveFollowing()
        {
            _followLive = false;
            LiveButton.Content = "Return to live";
        }

        private void LiveButton_Click(object sender, RoutedEventArgs e)
        {
            _followLive = true;
            LiveButton.Content = "Live";
            UpdateLiveWindow();
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