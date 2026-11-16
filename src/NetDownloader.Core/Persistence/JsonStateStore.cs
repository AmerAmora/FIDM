using System.Text.Json;
using System.Text.Json.Serialization;
using NetDownloader.Core.Models;

namespace NetDownloader.Core.Persistence;

public class JsonStateStore : IStateStore
{
    private readonly string _stateFilePath;
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly Timer? _throttledTimer;
    private readonly object _throttleLock = new();
    private List<DownloadPackage>? _pendingPackagesToSave;
    private bool _isDirty;
    private bool _isDisposed;

    public string BaseDirectory { get; }

    public JsonStateStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetDownloader"
        );

        Directory.CreateDirectory(BaseDirectory);
        _stateFilePath = Path.Combine(BaseDirectory, "state.json");
        _settingsFilePath = Path.Combine(BaseDirectory, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        // Auto-save timer fires every 2 seconds
        _throttledTimer = new Timer(OnThrottledTimerTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public async Task SavePackagesAsync(IReadOnlyList<DownloadPackage> packages, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await AtomicWriteAsync(_stateFilePath, packages, cancellationToken);
            lock (_throttleLock)
            {
                _isDirty = false;
                _pendingPackagesToSave = null;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<DownloadPackage>> LoadPackagesAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_stateFilePath))
                return new List<DownloadPackage>();

            await using var stream = File.OpenRead(_stateFilePath);
            var result = await JsonSerializer.DeserializeAsync<List<DownloadPackage>>(stream, _jsonOptions, cancellationToken);
            return result ?? new List<DownloadPackage>();
        }
        catch
        {
            return new List<DownloadPackage>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveSettingsAsync(EngineConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await AtomicWriteAsync(_settingsFilePath, configuration, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<EngineConfiguration> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = new EngineConfiguration();
                await AtomicWriteAsync(_settingsFilePath, defaultSettings, cancellationToken);
                return defaultSettings;
            }

            await using var stream = File.OpenRead(_settingsFilePath);
            var result = await JsonSerializer.DeserializeAsync<EngineConfiguration>(stream, _jsonOptions, cancellationToken);
            return result ?? new EngineConfiguration();
        }
        catch
        {
            return new EngineConfiguration();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void RequestThrottledSave(IReadOnlyList<DownloadPackage> packages)
    {
        lock (_throttleLock)
        {
            // Snapshot or clone package list
            _pendingPackagesToSave = packages.ToList();
            _isDirty = true;
        }
    }

    public async Task FlushPendingSavesAsync()
    {
        List<DownloadPackage>? toSave = null;
        lock (_throttleLock)
        {
            if (_isDirty && _pendingPackagesToSave != null)
            {
                toSave = _pendingPackagesToSave;
                _isDirty = false;
                _pendingPackagesToSave = null;
            }
        }

        if (toSave != null)
        {
            await SavePackagesAsync(toSave);
        }
    }

    private void OnThrottledTimerTick(object? state)
    {
        List<DownloadPackage>? toSave = null;
        lock (_throttleLock)
        {
            if (_isDirty && _pendingPackagesToSave != null)
            {
                toSave = _pendingPackagesToSave;
                _isDirty = false;
                _pendingPackagesToSave = null;
            }
        }

        if (toSave != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SavePackagesAsync(toSave);
                }
                catch
                {
                    // Ignore timer background save errors
                }
            });
        }
    }

    private async Task AtomicWriteAsync<T>(string targetFilePath, T data, CancellationToken cancellationToken)
    {
        var tempFilePath = $"{targetFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, data, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempFilePath, targetFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _throttledTimer?.Dispose();

        await FlushPendingSavesAsync();
        _fileLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
