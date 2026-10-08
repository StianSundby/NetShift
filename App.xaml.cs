using NetShiftST.Utils;
using NetShiftST.Core;
using System.Windows;

namespace NetShiftST
{
    /// <summary>
    /// Creates application services and handles network monitoring
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private TrayUI? _tray;
        private Config _config = null!;
        private NetworkManager _network = null!;
        private StartupManager _startup = null!;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                Logger.Log($"Unhandled exception: {args.Exception}");
                args.Handled = true;
            };

            _config = Config.Load(AppPaths.ConfigFile);
            _network = new NetworkManager(_config);
            _startup = new StartupManager("NetShift");

            var mainWindow = new MainWindow(_config, _network, _startup);

            _tray = new TrayUI(_network, _startup, mainWindow);

            mainWindow.Show();

            _ = MonitorAsync(_network, _config);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }

        private static async Task MonitorAsync(NetworkManager network, Config config)
        {
            await network.InitializeAsync();

            while (true)
            {
                var interval = TimeSpan.FromSeconds(Math.Max(1, config.CheckIntervalSeconds));
                await Task.Delay(interval);

                try
                {
                    await network.CheckAsync();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Monitor error: {ex.Message}");
                }
            }
        }
    }

}
