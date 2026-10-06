using System.Diagnostics;
using Microsoft.Win32;

namespace NetShiftST.Utils
{
    public sealed class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string _appName;
        public event Action<bool>? StartupChanged;

        public StartupManager(string appName)
        {
            _appName = string.IsNullOrWhiteSpace(appName) ? throw new ArgumentNullException(nameof(appName)) : appName;
        }
        
        public bool IsStartupEnabled()
        {
            try
            {
                if (IsScheduledTaskPresent()) 
                    return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"IsScheduledTaskPresent error: {ex.Message}");
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);

                if (key == null) 
                    return false;

                var val = key.GetValue(_appName) as string;
                if (string.IsNullOrWhiteSpace(val)) 
                    return false;

                var exe = Environment.ProcessPath!;
                return string.Equals(val.Trim('"'), exe, StringComparison.OrdinalIgnoreCase);
            }
            catch 
            { 
                return false; 
            }
        }

        public bool SetStartupEnabled(bool enabled)
        {
            bool success;

            try
            {
                if (enabled)
                {
                    success = CreateScheduledTask();
                    if (!success)
                    {
                        Logger.Log("Scheduled task creation failed; " + "falling back to registry startup.");
                        success = SetRegistryStartup(true);
                    }
                }
                else
                {
                    success = DeleteScheduledTask();
                    if (!success)
                    {
                        Logger.Log("Scheduled task deletion failed; " + "falling back to registry removal.");
                        success = SetRegistryStartup(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SetStartupEnabled (task) error: {ex.Message}");
                success = SetRegistryStartup(enabled);
            }

            if (success)
                StartupChanged?.Invoke(enabled);

            return success;
        }

        private bool SetRegistryStartup(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) 
                    ?? Registry.CurrentUser.CreateSubKey(RunKey);

                if (key == null) 
                    return false;

                if (enabled)
                {
                    var exe = Environment.ProcessPath!;
                    key.SetValue(_appName, $"\"{exe}\"", RegistryValueKind.String);
                }
                else key.DeleteValue(_appName, throwOnMissingValue: false);

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
            var result = RunScheduledTasks($"/Query /TN \"{_appName}\"");
            return result.success;
        }
        
        private bool CreateScheduledTask()
        {
            var exe = Environment.ProcessPath!;
            //https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create
            var args = $"/Create /TN \"{_appName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F";
            var result = RunScheduledTasks(args);

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
            var result = RunScheduledTasks(args);

            if (result.success)
            {
                Logger.Log("Scheduled task deleted.");
                return true;
            }

            if (result.output != null && result.output.Contains("ERROR: The system cannot find the file specified", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("Scheduled task not found.");
                return true;
            }

            Logger.Log($"DeleteScheduledTask failed: {result.output}");
            return false;
        }

        private static (bool success, string? output) RunScheduledTasks(string args, int timeoutMs = 10000)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var stdOut = process.StandardOutput.ReadToEnd();
                var stdErr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                    return (false, "schtasks timed out");
                }

                var combined = (stdOut + "\n" + stdErr).Trim();
                return (process.ExitCode == 0, combined);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}