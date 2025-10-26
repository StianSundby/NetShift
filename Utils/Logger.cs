namespace NetShift.Utils
{
    public static class Logger
    {
        private static readonly SemaphoreSlim _sync = new(1,1);
        private static readonly string _logFile;

        static Logger()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            _logFile = Path.Combine(dir, "log.txt");
        }

        public static async Task LogAsync(string msg)
        {
            var line = $"{DateTime.Now:dd-MM-yyyy HH:mm:ss}  {msg}{Environment.NewLine}";
            await _sync.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(_logFile, line).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger error: {ex.Message}");
            }
            finally
            {
                _sync.Release();
            }
        }

        public static void Log(string msg) => LogAsync(msg).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}
