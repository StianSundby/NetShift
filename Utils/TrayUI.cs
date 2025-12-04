using NetShift.Core;

namespace NetShift.Utils
{
    public class TrayUI : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        private readonly NetworkManager _networkManager;
        private readonly SynchronizationContext _syncContext;
        private bool _disposed;

        private readonly ToolStripMenuItem? _forceEthernetItem;
        private readonly ToolStripMenuItem? _forceWifiItem;
        private readonly ToolStripMenuItem? _preventSwitchItem;
        private readonly ToolStripMenuItem? _startupItem;

        private readonly IconManager _iconManager;
        private readonly StartupManager _startupManager;

        public TrayUI(NetworkManager network)
        {
            _networkManager = network ?? throw new ArgumentNullException(nameof(network));
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

            var icons = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "ico");
            _iconManager = new IconManager(icons);
            _iconManager.Preload("red.ico");
            _iconManager.Preload("green.ico");
            _iconManager.Preload("yellow.ico");

            _startupManager = new StartupManager("NetShift");

            _trayIcon = new NotifyIcon
            {
                Text = "NetShift",
                Icon = _iconManager.GetIconForState("none"),
                Visible = true
            };

            var menu = new ContextMenuStrip();

            _preventSwitchItem = new ToolStripMenuItem("Prevent Auto-Switching")
            {
                CheckOnClick = true,
                Checked = _networkManager.PreventAutoSwitching
            };
            _preventSwitchItem.Click += OnPreventSwitchClicked;

            _startupItem = new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = _startupManager.IsStartupEnabled()
            };
            _startupItem.Click += OnStartupClicked;

            _forceEthernetItem = new ToolStripMenuItem("Force Ethernet", null, OnForceEthernetClicked);
            _forceWifiItem = new ToolStripMenuItem("Force Wi-Fi", null, OnForceWifiClicked);

            menu.Items.Add(_preventSwitchItem);
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_forceEthernetItem);
            menu.Items.Add(_forceWifiItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); }));

            _trayIcon.ContextMenuStrip = menu;

            _networkManager.StatusChanged += OnStatusChanged;
            _networkManager.IconChanged += OnIconChanged;
        }

        private void OnStatusChanged(string title, string message)
        {
            _syncContext.Post(_ =>
            {
                try { _trayIcon.ShowBalloonTip(2000, title, message, ToolTipIcon.Info); }
                catch { }
            }, null);
        }

        private void OnIconChanged(string state)
        {
            _syncContext.Post(_ =>
            {
                try { _trayIcon.Icon = _iconManager.GetIconForState(state); }
                catch { }
            }, null);
        }

        private async void OnForceEthernetClicked(object? sender, EventArgs e)
        {
            try
            {
                SetForceItemsEnabled(false);
                await _networkManager.ForceEthernet();
            }
            catch (Exception ex)
            {
                Logger.Log($"ForceEthernet error: {ex.Message}");
            }
            finally
            {
                SetForceItemsEnabled(true);
            }
        }

        private async void OnForceWifiClicked(object? sender, EventArgs e)
        {
            try
            {
                SetForceItemsEnabled(false);
                await _networkManager.ForceWiFi();
            }
            catch (Exception ex)
            {
                Logger.Log($"ForceWiFi error: {ex.Message}");
            }
            finally
            {
                SetForceItemsEnabled(true);
            }
        }

        private void OnPreventSwitchClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_preventSwitchItem == null) return;
                _networkManager.PreventAutoSwitching = _preventSwitchItem.Checked;

                var message = _preventSwitchItem.Checked ? "Automatic switching disabled." : "Automatic switching enabled.";
                _syncContext.Post(_ =>
                {
                    try { _trayIcon.ShowBalloonTip(1500, "NetShift", message, ToolTipIcon.Info); }
                    catch { }
                }, null);
            }
            catch (Exception ex)
            {
                Logger.Log($"PreventSwitchClicked error: {ex.Message}");
            }
        }

        private void OnStartupClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_startupItem == null) return;
                bool startWithWindows = _startupItem.Checked;
                if (_startupManager.SetStartupEnabled(startWithWindows))
                {
                    var message = startWithWindows ? "NetShift will start with Windows." : "NetShift will not start with Windows.";
                    _syncContext.Post(_ =>
                    {
                        try { _trayIcon.ShowBalloonTip(1500, "NetShift", message, ToolTipIcon.Info); }
                        catch { }
                    }, null);
                }
                else
                {
                    _startupItem.Checked = !startWithWindows;
                    _syncContext.Post(_ =>
                    {
                        try { _trayIcon.ShowBalloonTip(1500, "NetShift", "Failed to update startup setting.", ToolTipIcon.Warning); }
                        catch { }
                    }, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"OnStartupClicked error: {ex.Message}");
            }
        }

        private void SetForceItemsEnabled(bool enabled)
        {
            _syncContext.Post(_ =>
            {
                try
                {
                    if (_forceEthernetItem != null) _forceEthernetItem.Enabled = enabled;
                    if (_forceWifiItem != null) _forceWifiItem.Enabled = enabled;
                }
                catch { }
            }, null);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _networkManager.StatusChanged -= OnStatusChanged;
                _networkManager.IconChanged -= OnIconChanged;

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.ContextMenuStrip?.Dispose();
                    _trayIcon.Dispose();
                }

                _iconManager.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"TrayUI.Dispose error: {ex.Message}");
            }
        }
    }
}
