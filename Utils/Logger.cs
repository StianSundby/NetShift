using System.IO;

namespace NetShiftST.Utils
{
    /// <summary>
    /// Appends timestamped diagnostic messages to the application log
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "log.txt");

        /// <summary>
        /// Appends a message to the log file with a timestamp in local time
        /// </summary>
        /// <param name="message">The message to record</param>
        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"{DateTime.Now:dd-MM-yyyy HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger error: {ex.Message}");
            }
        }
    }
}
