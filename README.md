# 🛜 NetShift 🌐 

NetShift is a small Windows tray utility that monitors internet connectivity and automatically switches between Ethernet and Wi‑Fi adapters using `netsh`. It displays a tray icon (green/yellow/red) for the active/available network state and provides context-menu actions to force Ethernet or Wi‑Fi.

---

## 🔑 Key features
- Automatic network failover: switches to Wi‑Fi when Ethernet loses connectivity and back to Ethernet when connectivity is restored.
- Tray icon with balloon notifications for status changes.
- Manual "Force Ethernet" / "Force Wi‑Fi" actions via the tray context menu.
- Configurable ping target and check interval.
- Lightweight logging to log.txt for diagnostics (this will be removed in the final version).
- Implements debounce/hysteresis to avoid rapid switching on unstable networks.


## 📋 Requirements
- Windows 10 or later (project targets `net8.0-windows10.0.19041.0`)
- .NET 8
- Administrator privileges required to run `netsh` commands.
## 🚀 Installation

Visit [Releases](https://github.com/StianSundby/NetShift/releases) and download the latest release.
## 🛡️ Security & Permissions
- NetShift runs `netsh` commands to enable/disable network adapters — Administrator rights are **required**. Exercise care when running elevated software.
- No elevated network credentials are stored; the app executes local system commands.
## ⚙️ Configuration

NetShift reads settings from `settings.cfg` (in the app folder). If missing, defaults are used.

Example `settings.cfg`:

```
EthernetName — "Ethernet", //adapter name (or substring) for the Ethernet interface.
WiFiName — "8HzWANIP",     //adapter name (or substring/description) for the Wi‑Fi interface.
PingTarget — 8.8.8.8,      //host to ping to verify internet (default: 8.8.8.8).
CheckIntervalSeconds — 15  //polling interval in seconds.
DebounceSeconds — 2,       //wait this long (in seconds) after connectivity change before switching (to prevent flapping).
HysteresisMinutes — 5      //minimum duration (in seconds) for a connection state before considering revert (helps stabilize transitions).
```
## 🛠️ Troubleshooting

- log.txt (application folder) contains runtime events, `netsh` output, adapter enumeration and errors.
- If switching fails:
  - Confirm adapter names in log.txt and adjust `settings.cfg`.
  - Ensure the process is running as Administrator (`netsh` requires elevated permissions).
  - Check that icons exist under `res/ico` (green.ico / yellow.ico / red.ico).
  - Verify the ping target is reachable from your network or change it in `settings.cfg`.
## Run Locally

Clone the project

```bash
    git clone https://github.com/StianSundby/NetShift
```

 Note: because the project is a WinForms tray app, running from the CLI is useful for publishing; prefer running from Visual Studio for interactive debugging.

Using Visual Studio 2022
1. Open the solution (NetShift.sln).
2. Set `NetShift` as the startup project.
3. Run Visual Studio as Administrator.
4. Select configuration __Debug__ or __Release__ and press __F5__ or use __Debug > Start Debugging__ / __Debug > Start Without Debugging__.
## Contributing

- Pull requests welcome. Keep changes small and focused.
- NetworkManager contains lots of side effects; consider abstraction for netsh/ping when adding tests)
## License

[MIT](https://github.com/StianSundby/NetShift/blob/main/LICENSE)