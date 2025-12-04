using NetShift.Core;
using NetShift.Utils;

namespace NetShift
{
    //build command:
    //dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

    static class Program
    {
        private static Config? _config = null!;
        private static TrayUI? _tray;
        private static NetworkManager? _netManager = null!;
        private static readonly CancellationTokenSource _cts = new();

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            //global exception handlers (UI and non-UI threads)
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                InitializeServices();
                StartMonitoringLoop(_cts.Token);
                Application.ApplicationExit += OnApplicationExit;
                Application.Run();
            }
            catch (Exception ex)
            {
                Logger.Log($"Unhandled error in Program.cs Main(): {ex.Message}");
            }
        }

        private static void Application_ThreadException(object? sender, ThreadExceptionEventArgs e)
        {
            try { Logger.Log($"UI thread exception: {e.Exception.Message}"); }
            catch { }
        }

        private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.Log($"Unhandled domain exception: {ex.Message} (IsTerminating={e.IsTerminating})");
                else
                    Logger.Log($"Unhandled domain exception: {e.ExceptionObject}");
            }
            catch { }
        }

        private static void InitializeServices()
        {
            _config = Config.Load("settings.cfg");
            _netManager = new NetworkManager(_config);
            _tray = new TrayUI(_netManager);

            _ = _netManager.InitializeAsync().ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Logger.Log($"Init error: {t.Exception.InnerException?.Message}");

            }, TaskScheduler.Default);

            _netManager.LogAdapterStatus();
        }

        private static void StartMonitoringLoop(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await _netManager!.MonitorNetworkAsync(token);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Monitor error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_config!.CheckIntervalSeconds), token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private static void OnApplicationExit(object? sender, EventArgs e)
        {
            try
            {
                _cts.Cancel();
                _tray?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during shutdown: {ex.Message}");
            }
        }
    }
}
