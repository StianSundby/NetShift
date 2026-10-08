using System.IO;
using NetShiftST.Core;

namespace NetShiftST.Utils
{
    /// <summary>
    /// Loads network status icons with a system icon fallback.
    /// </summary>
    internal sealed class IconManager : IDisposable
    {
        private readonly Dictionary<IconState, Icon> _icons = [];

        /// <summary>
        /// Loads the Ethernet, Wi-Fi, and offline icons from the specified directory
        /// </summary>
        /// <param name="iconDirectory">The directory containing the icon files</param>
        public IconManager(string iconDirectory)
        {
            Load(IconState.Ethernet, Path.Combine(iconDirectory, "green.ico"));
            Load(IconState.WiFi, Path.Combine(iconDirectory, "yellow.ico"));
            Load(IconState.Offline, Path.Combine(iconDirectory, "red.ico"));
        }

        /// <summary>
        /// Gets the icon for the specified network state, falling back to the system warning icon if no matching icon was loaded.
        /// </summary>
        /// <param name="state">The network state to represent</param>
        /// <returns>
        /// An icon managed by this instance.
        /// </returns>
        /// <remarks>
        /// The caller should not dispose the returned icon.
        /// </remarks>
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

        /// <summary>
        /// Disposes all icons loaded by this instance
        /// </summary>
        public void Dispose()
        {
            foreach (var icon in _icons.Values)
                icon.Dispose();
            _icons.Clear();
        }
    }
}
