using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NetShift.Core;

namespace NetShift.Utils
{
    public class TrayUI : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        private readonly NetworkManager _network;
        private readonly string _iconDir;
        private readonly SynchronizationContext _syncContext;
        private readonly Dictionary<string, Icon> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        // keep references so we can enable/disable
        private ToolStripMenuItem? _forceEthernetItem;
        private ToolStripMenuItem? _forceWifiItem;

        public TrayUI(NetworkManager network)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _iconDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "ico");
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

            // Preload common icons (failsafe to SystemIcons.Warning)
            PreloadIcon("red.ico");
            PreloadIcon("green.ico");
            PreloadIcon("yellow.ico");

            _trayIcon = new NotifyIcon
            {
                Text = "NetShift",
                Icon = GetIconForState("none"),
                Visible = true
            };

            var menu = new ContextMenuStrip();

            _forceEthernetItem = new ToolStripMenuItem("Force Ethernet", null, OnForceEthernetClicked);
            _forceWifiItem = new ToolStripMenuItem("Force Wi-Fi", null, OnForceWifiClicked);

            menu.Items.Add(_forceEthernetItem);
            menu.Items.Add(_forceWifiItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); }));

            _trayIcon.ContextMenuStrip = menu;

            // Subscribe to notifications from NetworkManager
            _network.StatusChanged += OnStatusChanged;
            _network.IconChanged += OnIconChanged;
        }

        private void PreloadIcon(string fileName)
        {
            try
            {
                string path = Path.Combine(_iconDir, fileName);
                if (File.Exists(path))
                {
                    var key = fileName;
                    if (!_iconCache.ContainsKey(key))
                        _iconCache[key] = new Icon(path);
                }
            }
            catch
            {
                // ignore load errors; fallback used later
            }
        }

        private Icon GetIconForState(string state)
        {
            string fileName = state.ToLower() switch
            {
                "ethernet" => "green.ico",
                "wifi" => "yellow.ico",
                "none" => "red.ico",
                _ => "red.ico"
            };

            if (_iconCache.TryGetValue(fileName, out var icon))
                return icon;

            // try lazy load if not preloaded
            try
            {
                string path = Path.Combine(_iconDir, fileName);
                if (File.Exists(path))
                {
                    var newIcon = new Icon(path);
                    _iconCache[fileName] = newIcon;
                    return newIcon;
                }
            }
            catch { }

            return SystemIcons.Warning;
        }

        private void OnStatusChanged(string title, string message)
        {
            // marshal to UI thread
            _syncContext.Post(_ =>
            {
                try { _trayIcon.ShowBalloonTip(2000, title, message, ToolTipIcon.Info); }
                catch { } // swallow to avoid bringing down caller threads
            }, null);
        }

        private void OnIconChanged(string state)
        {
            _syncContext.Post(_ =>
            {
                try { _trayIcon.Icon = GetIconForState(state); }
                catch { }
            }, null);
        }

        private async void OnForceEthernetClicked(object? sender, EventArgs e)
        {
            // async void is acceptable for event handlers; errors are logged
            try
            {
                SetForceItemsEnabled(false);
                await _network.ForceEthernet();
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
                await _network.ForceWiFi();
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

        private void SetForceItemsEnabled(bool enabled)
        {
            // ensure run on UI sync context
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
                _network.StatusChanged -= OnStatusChanged;
                _network.IconChanged -= OnIconChanged;

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.ContextMenuStrip?.Dispose();
                    _trayIcon.Dispose();
                }

                foreach (var kv in _iconCache)
                {
                    // avoid disposing SystemIcons.Warning (not in cache)
                    try { kv.Value.Dispose(); } catch { }
                }

                _iconCache.Clear();
            }
            catch (Exception ex)
            {
                Logger.Log($"TrayUI.Dispose error: {ex.Message}");
            }
        }
    }
}
