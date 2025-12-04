using System.Diagnostics;
using Microsoft.Win32;

namespace NetShift.Utils
{
    internal sealed class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string _appName;

        public StartupManager(string appName)
        {
            _appName = string.IsNullOrWhiteSpace(appName) ? throw new ArgumentNullException(nameof(appName)) : appName;
        }

        public bool IsStartupEnabled()
        {
            try
            {
                if (IsScheduledTaskPresent()) return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"IsScheduledTaskPresent error: {ex.Message}");
            }

            //fallback to registry check
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                if (key == null) return false;
                var val = key.GetValue(_appName) as string;
                if (string.IsNullOrWhiteSpace(val)) return false;

                var exe = Application.ExecutablePath;
                return string.Equals(val.Trim('"'), exe, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(val, $"\"{exe}\"", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public bool SetStartupEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (CreateScheduledTask()) return true;
                    Logger.Log("Scheduled task creation failed; falling back to registry startup.");
                    return SetRegistryStartup(true);
                }
                else
                {
                    if (DeleteScheduledTask()) return true;
                    Logger.Log("Scheduled task deletion failed or not found; falling back to registry removal.");
                    return SetRegistryStartup(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SetStartupEnabled (task) error: {ex.Message}");
                return SetRegistryStartup(enabled);
            }
        }

        private bool SetRegistryStartup(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return false;

                if (enabled)
                {
                    var exe = Application.ExecutablePath;
                    key.SetValue(_appName, $"\"{exe}\"", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(_appName, throwOnMissingValue: false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"SetRegistryStartup error: {ex.Message}");
                return false;
            }
        }

        private bool IsScheduledTaskPresent()
        {
            var result = RunSchtasks($"/Query /TN \"{_appName}\"");
            return result.success;
        }

        private bool CreateScheduledTask()
        {
            var exe = Application.ExecutablePath;
            var args = $"/Create /TN \"{_appName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F";
            var result = RunSchtasks(args);

            if (result.success)
            {
                Logger.Log("Scheduled task created.");
                return true;
            }

            Logger.Log($"CreateScheduledTask failed: {result.output}");
            return false;
        }

        private bool DeleteScheduledTask()
        {
            var args = $"/Delete /TN \"{_appName}\" /F";
            var result = RunSchtasks(args);
            if (result.success)
            {
                Logger.Log("Scheduled task deleted.");
                return true;
            }

            if (result.output != null &&
                result.output.Contains("ERROR: The system cannot find the file specified", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("Scheduled task not found.");
                return true;
            }

            Logger.Log($"DeleteScheduledTask failed: {result.output}");
            return false;
        }

        private static (bool success, string? output) RunSchtasks(string args, int timeoutMs = 10000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdOut = proc.StandardOutput.ReadToEnd();
                var stdErr = proc.StandardError.ReadToEnd();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return (false, "schtasks timed out");
                }

                var combined = (stdOut + "\n" + stdErr).Trim();
                return (proc.ExitCode == 0, combined);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
