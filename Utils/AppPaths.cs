using System.IO;

namespace NetShiftST.Utils
{
    public static class AppPaths
    {
        public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetShift");
        public static string ConfigFile { get; } = Path.Combine(DataDirectory, "settings.cfg");
        public static string LogFile { get; } = Path.Combine(DataDirectory, "log.txt");

        static AppPaths()
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }
}