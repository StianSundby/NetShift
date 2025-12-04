namespace NetShift.Utils
{
    internal sealed class IconManager : IDisposable
    {
        private readonly string _iconDir;
        private readonly Dictionary<string, Icon> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public IconManager(string iconDirectory)
        {
            _iconDir = iconDirectory ?? throw new ArgumentNullException(nameof(iconDirectory));
        }

        public void Preload(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            try
            {
                string path = Path.Combine(_iconDir, fileName);
                if (File.Exists(path))
                {
                    if (!_iconCache.ContainsKey(fileName))
                        _iconCache[fileName] = new Icon(path);
                }
            }
            catch
            {
                //ignore load errors. Fallback used later
            }
        }

        public Icon GetIconForState(string state)
        {
            state ??= string.Empty;

            string fileName = state.ToLower() switch
            {
                "ethernet" => "green.ico",
                "wifi" => "yellow.ico",
                "none" => "red.ico",
                _ => "red.ico"
            };

            if (_iconCache.TryGetValue(fileName, out var icon))
                return icon;

            //try lazy load if not preloaded
            try
            {
                string path = Path.Combine(_iconDir, fileName);
                if (File.Exists(path))
                {
                    var newIcon = new Icon(path);
                    _iconCache[fileName] = newIcon;
                    return newIcon;
                }
            }
            catch { }

            return SystemIcons.Warning;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _iconCache)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _iconCache.Clear();
        }
    }
}
