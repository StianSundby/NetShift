using System.Drawing;
using System.IO;
using NetShiftST.Core;

namespace NetShiftST.Utils
{
    /// <summary>
    /// Loads and owns network status icons, with a system icon fallback.
    /// </summary>
    internal sealed class IconManager : IDisposable
    {
        private readonly Dictionary<IconState, Icon> _icons = [];

        public IconManager(string iconDirectory)
        {
            Load(IconState.Ethernet, Path.Combine(iconDirectory, "green.ico"));
            Load(IconState.WiFi, Path.Combine(iconDirectory, "yellow.ico"));
            Load(IconState.Offline, Path.Combine(iconDirectory, "red.ico"));
        }

        public Icon Get(IconState state) =>
            _icons.TryGetValue(state, out var icon) ? icon : SystemIcons.Warning;

        private void Load(IconState state, string path)
        {
            try
            {
                if (File.Exists(path))
                    _icons[state] = new Icon(path);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not load icon '{path}': {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var icon in _icons.Values)
                icon.Dispose();
            _icons.Clear();
        }
    }
}
