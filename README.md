# 🛜 NetShift 🌐

NetShift is a Windows network utility that monitors internet connectivity and automatically switches between Ethernet and Wi-Fi adapters.

The WPF interface provides network controls, configurable settings, live throughput and ping charts, and a compact monitor mode. A system tray icon keeps NetShift accessible when the window is hidden.


## 🔑 Key features

- Automatic switching to Wi-Fi after consecutive failed connectivity checks while using Ethernet.
- Automatic attempts to return to Ethernet after consecutive successful checks and a minimum period on Wi-Fi.
- Manual **Force Ethernet** and **Force Wi-Fi** actions.
- **Prevent Auto-Switching** toggle.
- Live download, upload, and ping charts.
- Compact monitor mode and an always-on-top option.
- Tray status icons and notifications.
- Configurable adapter names, ping target, check interval, and switching thresholds.
- Start-with-Windows option.
- Configurable close behavior: ask, minimize to tray, or exit.
- Diagnostic logging.

## 📋 Requirements

- Windows 10 version 2004 or later, or Windows 11.
- Administrator privileges for enabling and disabling network adapters.
- .NET 10 Desktop Runtime for framework-dependent builds. Self-contained builds include the runtime.
- .NET 10 SDK to build from source.

The project targets `net10.0-windows10.0.19041.0` and uses WPF for its main interface and Windows Forms components for its tray menu.

## 🚀 Installation

Published builds are available on the [Releases page](https://github.com/StianSundby/NetShift/releases).

## Usage

1. Run NetShift as Administrator.
2. Confirm the Ethernet and Wi-Fi adapter names in the settings panel.
3. Adjust the connectivity checks and switching thresholds if needed.
4. Click **Save** to persist changes.

Use **Force Ethernet** or **Force Wi-Fi** to request a manual switch. Enable **Prevent Auto-Switching** to pause automatic switching.

## 🛡️ How automatic switching works

NetShift periodically sends a ping to the configured target (defaults to Google DNS `8.8.8.8`).

- While using Ethernet, consecutive failed checks trigger an attempt to switch to Wi-Fi.
- While using Wi-Fi, consecutive successful checks trigger an attempt to return to Ethernet once the minimum Wi-Fi uptime has elapsed.
- During a switch, NetShift enables the requested adapter and waits for it to become operational before requesting that the other adapter be disabled.

The thresholds reduce switching caused by isolated failed checks.

### Current limitations

- A failed ping can indicate an unreachable target or blocked traffic, rather than a complete internet outage.
- An operational adapter does not necessarily have working internet access.
- Successful checks over Wi-Fi do not independently verify that Ethernet connectivity has recovered.
- Enabling or disabling adapters can interrupt active connections.

## ⚙️ Configuration

Settings can be edited in the application or directly in `settings.cfg`, located beside the executable. If the file is missing, NetShift uses its defaults.

Use one `key=value` setting per line. Do not add quotes, commas, or inline comments.

```ini
ethernetname=Wu-Tag LAN
wifiname=Silence of the LANS
pingtarget=8.8.8.8
checkintervalseconds=15
failurethreshold=3
successthreshold=2
minwifiuptimeseconds=10
closeaction=Ask
```

| Setting | Description |
|---|---|
| `ethernetname` | Name of the Ethernet adapter. |
| `wifiname` | Name of the Wi-Fi adapter. |
| `pingtarget` | Hostname or IP address used for connectivity checks. |
| `checkintervalseconds` | Time between monitoring checks, in seconds. |
| `failurethreshold` | Consecutive failures required before attempting Wi-Fi failover. |
| `successthreshold` | Consecutive successes required before attempting a return to Ethernet. |
| `minwifiuptimeseconds` | Minimum time on Wi-Fi before attempting a return to Ethernet. |
| `closeaction` | `Ask`, `MinimizeToTray`, or `Exit`. |

**Use the exact Windows adapter names where possible.** Status lookup also supports partial names and descriptions, but adapter commands require a valid interface name.

Restart NetShift after editing the configuration file manually.

## Permissions

NetShift uses local `netsh` commands to enable and disable network adapters. These operations require Administrator privileges.

The **Start with Windows** option attempts to register a scheduled task with elevated privileges and falls back to registry startup if task creation fails. Registry startup does not automatically grant elevation.

## 🛠️ Troubleshooting

Diagnostic information is written to `log.txt` beside the executable, including adapter details, connectivity checks, switching events, and command output.

If switching fails:

- Confirm that NetShift is running as Administrator.
- Check adapter names in Windows network settings or `log.txt`.
- Verify that the configured ping target is reachable and accepts ICMP traffic.
- Check whether Ethernet has a physical connection and Wi-Fi can connect to a network.

If tray icons are missing, confirm that these files are present under `res/ico`:

- `green.ico` — Ethernet
- `yellow.ico` — Wi-Fi
- `red.ico` — Offline
