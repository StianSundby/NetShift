namespace NetShiftST.Core;

public sealed class InternetCutoffManager : IDisposable
{
    private readonly Config _config;
    private readonly NetworkManager _network;
    private readonly System.Threading.Timer _timer;
    private DateOnly? _lastCutoffDate;
    private bool _disposed;
    public bool IsLocked { get; private set; }
    public event Action? InternetLocked;
    public event Action? InternetUnlocked;

    public InternetCutoffManager(Config config, NetworkManager network)
    {
        _config = config;
        _network = network;
        _timer = new System.Threading.Timer( CheckCutoff, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    private void CheckCutoff(object? state)
    {
        if (!_config.InternetCutoffEnabled || IsLocked)
            return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        if (_lastCutoffDate == today)
            return;

        var cutoff = _config.InternetCutoffTime;
        var cutoffToday = new DateTime(now.Year, now.Month, now.Day, cutoff.Hour, cutoff.Minute, 0);

        if (now < cutoffToday)
            return;

        LockInternet();
    }

    private void LockInternet()
    {
        if (IsLocked)
            return;

        IsLocked = true;
        _lastCutoffDate = DateOnly.FromDateTime(DateTime.Now);

        InternetLocked?.Invoke();

        _ = DisableInternetAsync();
    }

    private async Task DisableInternetAsync()
    {
        try
        {
            await _network.DisableInternetAsync();
        }
        catch (Exception ex)
        {
            Utils.Logger.Log($"Failed to disable internet for cutoff: {ex}");
        }
    }

    public async Task UnlockInternetAsync()
    {
        if (!IsLocked)
            return;

        try
        {
            await _network.RestoreInternetAsync();

            IsLocked = false;
            InternetUnlocked?.Invoke();
        }
        catch (Exception ex)
        {
            Utils.Logger.Log($"Failed to restore internet after cutoff: {ex}");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
    }
}