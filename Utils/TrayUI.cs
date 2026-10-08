using System.IO;
using NetShiftST.Core;

namespace NetShiftST.Utils;

/// <summary>
/// Botification icon and tray actions for network and startup control
/// </summary>
public class TrayUI : IDisposable
{
    #region Properties
    private readonly NetworkManager _network;
    private readonly IconManager _icons;
    private readonly StartupManager _startup;
    private readonly ToolStripMenuItem _startupItem;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _forceEthernetItem;
    private readonly ToolStripMenuItem _forceWifiItem;
    private readonly ToolStripMenuItem _preventItem;
    private readonly MainWindow _mainWindow;
    #endregion

    public TrayUI(NetworkManager network, StartupManager startup, MainWindow mainWindow)
    {
        _network = network;
        _startup = startup;
        _mainWindow = mainWindow;
        _icons = new IconManager(Path.Combine(AppContext.BaseDirectory, "res", "ico"));

        _trayIcon = new NotifyIcon
        {
            Text = "NetShift",
            Icon = _icons.Get(IconState.Offline),
            Visible = true
        };

        _preventItem = new ToolStripMenuItem("Prevent Auto-Switching")
        {
            CheckOnClick = true,
            Checked = _network.PreventAutoSwitching
        };

        _preventItem.Click += (_, _) =>
        {
            _network.PreventAutoSwitching = _preventItem.Checked;
            ShowBalloon("NetShift", _preventItem.Checked ? "Automatic switching disabled." : "Automatic switching enabled.");
        };

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _startup.IsStartupEnabled()
        };

        _startupItem.Click += (_, _) =>
            OnStartupClicked(_startupItem);

        _forceEthernetItem = new ToolStripMenuItem("Force Ethernet", null, async (_, _) =>
            await ForceAsync(ethernet: true));

        _forceWifiItem = new ToolStripMenuItem("Force Wi-Fi", null, async (_, _) =>
            await ForceAsync(ethernet: false));

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) =>
            ExitApplication());

        var openItem = new ToolStripMenuItem("Open NetShift", null, (_, _) =>
            ShowMainWindow());

        BuildContextMenu(openItem, exitItem);

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        _network.StatusChanged += OnStatusChanged;
        _network.IconChanged += OnIconChanged;
        _network.PreventAutoSwitchingChanged += OnPreventAutoSwitchingChanged;
        _startup.StartupChanged += OnStartupChanged;
    }

    private void BuildContextMenu(ToolStripMenuItem openItem, ToolStripMenuItem exitItem)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_preventItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_forceEthernetItem);
        menu.Items.Add(_forceWifiItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = menu;
    }

    #region User actions
    private async Task ForceAsync(bool ethernet)
    {
        SetForceItemsEnabled(false);
        try
        {
            await (ethernet ? _network.ForceEthernetAsync() : _network.ForceWiFiAsync());
        }
        catch (Exception ex)
        {
            Logger.Log($"Force {(ethernet ? "Ethernet" : "Wi-Fi")} error: {ex.Message}");
        }
        finally
        {
            SetForceItemsEnabled(true);
        }
    }

    private void OnStartupClicked(ToolStripMenuItem item)
    {
        bool wanted = item.Checked;
        if (_startup.SetStartupEnabled(wanted))
        {
            ShowBalloon("NetShift", wanted ? "NetShift will start with Windows." : "NetShift will not start with Windows.");
        }
        else
        {
            item.Checked = !wanted;
            ShowBalloon("NetShift", "Failed to update startup setting.", ToolTipIcon.Warning);
        }
    }

    private void SetForceItemsEnabled(bool enabled)
    {
        _forceEthernetItem.Enabled = enabled;
        _forceWifiItem.Enabled = enabled;
    }

    private void ShowMainWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();

            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                _mainWindow.WindowState = System.Windows.WindowState.Normal;

            _mainWindow.Activate();
        });
    }

    private void ExitApplication()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.ExitApplication();
        });
    }
    #endregion

    #region Event handlers
    private void OnPreventAutoSwitchingChanged(bool enabled)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(() =>
            {
                _preventItem.Checked = enabled;
            });
            return;
        }

        _preventItem.Checked = enabled;
    }

    private void OnStatusChanged(string title, string message) =>
        ShowBalloon(title, message);

    private void OnIconChanged(IconState state) =>
        _trayIcon.Icon = _icons.Get(state);

    private void OnStartupChanged(bool enabled)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.Invoke(new Action(() =>
            {
                _startupItem.Checked = enabled;
            }));
            return;
        }

        _startupItem.Checked = enabled;
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info) =>
        _trayIcon.ShowBalloonTip(2000, title, message, icon);
    #endregion

    #region Disposal
    public void Dispose()
    {
        _network.StatusChanged -= OnStatusChanged;
        _network.IconChanged -= OnIconChanged;
        _network.PreventAutoSwitchingChanged -= OnPreventAutoSwitchingChanged;
        _startup.StartupChanged -= OnStartupChanged;

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _icons.Dispose();

        GC.SuppressFinalize(this);
    }
    #endregion

}