namespace NetDownloader.Core.Engine;

public class BandwidthLimiter
{
    private long _maxSpeedBytesPerSecond;
    private long _availableTokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public long MaxSpeedBytesPerSecond
    {
        get => Interlocked.Read(ref _maxSpeedBytesPerSecond);
        set
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _maxSpeedBytesPerSecond, Math.Max(0, value));
                _availableTokens = Math.Max(0, value);
                _lastRefillTicks = Environment.TickCount64;
            }
        }
    }

    public BandwidthLimiter(long maxSpeedBytesPerSecond = 0)
    {
        _maxSpeedBytesPerSecond = Math.Max(0, maxSpeedBytesPerSecond);
        _availableTokens = _maxSpeedBytesPerSecond;
        _lastRefillTicks = Environment.TickCount64;
    }

    public async ValueTask ConsumeAsync(int byteCount, CancellationToken cancellationToken = default)
    {
        long limit = MaxSpeedBytesPerSecond;
        if (limit <= 0 || byteCount <= 0)
            return;

        while (byteCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int waitMs = 0;
            lock (_lock)
            {
                RefillTokens(limit);

                if (_availableTokens >= byteCount)
                {
                    _availableTokens -= byteCount;
                    return;
                }

                if (_availableTokens > 0)
                {
                    byteCount -= (int)_availableTokens;
                    _availableTokens = 0;
                }

                // Calculate wait time needed for remaining byteCount
                double secondsNeeded = (double)byteCount / limit;
                waitMs = Math.Clamp((int)(secondsNeeded * 1000), 10, 200);
            }

            if (waitMs > 0)
            {
                await Task.Delay(waitMs, cancellationToken);
            }
        }
    }

    private void RefillTokens(long limit)
    {
        long now = Environment.TickCount64;
        long elapsedMs = now - _lastRefillTicks;
        if (elapsedMs <= 0) return;

        double elapsedSeconds = elapsedMs / 1000.0;
        long newTokens = (long)(elapsedSeconds * limit);
        if (newTokens > 0)
        {
            _availableTokens = Math.Min(limit, _availableTokens + newTokens);
            _lastRefillTicks = now;
        }
    }
}
