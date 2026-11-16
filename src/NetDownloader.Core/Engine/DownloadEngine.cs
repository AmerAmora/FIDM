using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;
using NetDownloader.Core.Extraction;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Services;
using NetDownloader.Core.Storage;
using Polly;
using Polly.Retry;

namespace NetDownloader.Core.Engine;

public class DownloadEngine : IDownloadEngine
{
    private readonly HttpClient _httpClient;
    private readonly IFileStorageService _fileStorage;
    private readonly IStateStore _stateStore;
    private readonly IArchiveExtractor? _archiveExtractor;
    private readonly BandwidthLimiter _bandwidthLimiter;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    private readonly List<DownloadPackage> _packages = new();
    private readonly object _packagesLock = new();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _packageCts = new();
    private readonly ConcurrentDictionary<Guid, Task> _packageTasks = new();

    private SemaphoreSlim _packageSemaphore;
    private EngineConfiguration _config;
    private readonly Timer _progressTimer;
    private readonly object _speedLock = new();

    private long _bytesDownloadedSinceLastInterval;
    private long _aggregateSpeed;
    private CancellationTokenSource _globalCts = new();
    private bool _isDisposed;

    public EngineConfiguration Configuration => _config;
    public IReadOnlyList<DownloadPackage> Packages
    {
        get
        {
            lock (_packagesLock)
            {
                return _packages.ToList();
            }
        }
    }

    public long AggregateSpeedBytesPerSecond => Interlocked.Read(ref _aggregateSpeed);

    public int ActiveTasksCount
    {
        get
        {
            lock (_packagesLock)
            {
                return _packages.Count(p => p.Status == PackageStatus.Downloading || p.Status == PackageStatus.Extracting);
            }
        }
    }

    public event EventHandler<EngineProgressEventArgs>? ProgressUpdated;
    public event EventHandler<DownloadPackage>? PackageCompleted;
    public event EventHandler<DownloadPackage>? PackageStatusChanged;

    public DownloadEngine(
        IFileStorageService fileStorage,
        IStateStore stateStore,
        IArchiveExtractor? archiveExtractor = null,
        HttpClient? httpClient = null,
        EngineConfiguration? configuration = null)
    {
        _fileStorage = fileStorage;
        _stateStore = stateStore;
        _archiveExtractor = archiveExtractor;
        _config = configuration ?? new EngineConfiguration();
        _httpClient = httpClient ?? CreateDefaultHttpClient();

        _bandwidthLimiter = new BandwidthLimiter(_config.MaxSpeedBytesPerSecond);
        _packageSemaphore = new SemaphoreSlim(_config.MaxConcurrentPackages, _config.MaxConcurrentPackages);

        // Polly retry pipeline for HTTP 429, 5xx, and network timeouts
        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .HandleResult(resp =>
                    {
                        int code = (int)resp.StatusCode;
                        return code == 429 || (code >= 500 && code <= 599);
                    }),
                DelayGenerator = args =>
                {
                    if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                    {
                        return ValueTask.FromResult<TimeSpan?>(delta);
                    }
                    return ValueTask.FromResult<TimeSpan?>(null);
                }
            })
            .Build();

        // 4 updates per second (every 250ms)
        _progressTimer = new Timer(OnProgressTimerTick, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loadedConfig = await _stateStore.LoadSettingsAsync(cancellationToken);
        UpdateConfiguration(loadedConfig);

        var loadedPackages = await _stateStore.LoadPackagesAsync(cancellationToken);
        lock (_packagesLock)
        {
            _packages.Clear();
            foreach (var pkg in loadedPackages)
            {
                // Reset active statuses to Queued or Paused if interrupted
                if (pkg.Status == PackageStatus.Downloading)
                {
                    pkg.Status = PackageStatus.Paused;
                }
                foreach (var item in pkg.Items)
                {
                    if (item.Status == DownloadItemStatus.Downloading)
                    {
                        item.Status = DownloadItemStatus.Paused;
                    }
                }
                _packages.Add(pkg);
            }
        }
    }

    public async Task EnqueuePackageAsync(DownloadPackage package, CancellationToken cancellationToken = default)
    {
        lock (_packagesLock)
        {
            if (!_packages.Any(p => p.Id == package.Id))
            {
                _packages.Add(package);
            }
        }

        await _stateStore.SavePackagesAsync(Packages, cancellationToken);
    }

    public async Task EnqueuePackagesAsync(IEnumerable<DownloadPackage> packages, CancellationToken cancellationToken = default)
    {
        lock (_packagesLock)
        {
            foreach (var package in packages)
            {
                if (!_packages.Any(p => p.Id == package.Id))
                {
                    _packages.Add(package);
                }
            }
        }

        await _stateStore.SavePackagesAsync(Packages, cancellationToken);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        List<DownloadPackage> targets;
        lock (_packagesLock)
        {
            targets = _packages
                .Where(p => p.Status is PackageStatus.Queued or PackageStatus.Paused or PackageStatus.Failed)
                .ToList();
        }

        foreach (var pkg in targets)
        {
            await ResumePackageAsync(pkg.Id, cancellationToken);
        }
    }

    public async Task PauseAllAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> activeIds;
        lock (_packagesLock)
        {
            activeIds = _packages
                .Where(p => p.Status == PackageStatus.Downloading)
                .Select(p => p.Id)
                .ToList();
        }

        foreach (var id in activeIds)
        {
            await PausePackageAsync(id, cancellationToken);
        }

        await _stateStore.FlushPendingSavesAsync();
    }

    public async Task PausePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        DownloadPackage? package;
        lock (_packagesLock)
        {
            package = _packages.FirstOrDefault(p => p.Id == packageId);
        }

        if (package == null) return;

        if (_packageCts.TryRemove(packageId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_packageTasks.TryRemove(packageId, out var task))
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); } catch { }
        }

        package.Status = PackageStatus.Paused;
        foreach (var item in package.Items.Where(i => i.Status == DownloadItemStatus.Downloading))
        {
            item.Status = DownloadItemStatus.Paused;
        }

        PackageStatusChanged?.Invoke(this, package);
        await _stateStore.SavePackagesAsync(Packages, cancellationToken);
    }

    public Task ResumePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        DownloadPackage? package;
        lock (_packagesLock)
        {
            package = _packages.FirstOrDefault(p => p.Id == packageId);
        }

        if (package == null || package.Status is PackageStatus.Completed or PackageStatus.Extracted)
            return Task.CompletedTask;

        package.Status = PackageStatus.Queued;
        PackageStatusChanged?.Invoke(this, package);

        var cts = new CancellationTokenSource();
        _packageCts[packageId] = cts;

        var runTask = Task.Run(async () =>
        {
            try
            {
                await ProcessPackageAsync(package, cts.Token);
            }
            finally
            {
                _packageCts.TryRemove(packageId, out _);
                _packageTasks.TryRemove(packageId, out _);
            }
        }, cts.Token);

        _packageTasks[packageId] = runTask;
        return Task.CompletedTask;
    }

    public async Task RemovePackageAsync(Guid packageId, bool deleteFiles = false, CancellationToken cancellationToken = default)
    {
        await PausePackageAsync(packageId, cancellationToken);

        DownloadPackage? package;
        lock (_packagesLock)
        {
            package = _packages.FirstOrDefault(p => p.Id == packageId);
            if (package != null)
            {
                _packages.Remove(package);
            }
        }

        if (package != null && deleteFiles)
        {
            foreach (var item in package.Items)
            {
                var partFile = _fileStorage.GetPartFilePath(package.DestinationDirectory, item.FileName);
                var finalFile = _fileStorage.GetFinalFilePath(package.DestinationDirectory, item.FileName);
                _fileStorage.DeleteFileIfExists(partFile);
                _fileStorage.DeleteFileIfExists(finalFile);
            }
        }

        await _stateStore.SavePackagesAsync(Packages, cancellationToken);
    }

    public void UpdateSpeedLimit(long maxSpeedBytesPerSecond)
    {
        _config.MaxSpeedBytesPerSecond = Math.Max(0, maxSpeedBytesPerSecond);
        _bandwidthLimiter.MaxSpeedBytesPerSecond = _config.MaxSpeedBytesPerSecond;
    }

    public void UpdateConfiguration(EngineConfiguration newConfig)
    {
        _config = newConfig;
        UpdateSpeedLimit(_config.MaxSpeedBytesPerSecond);
        _packageSemaphore = new SemaphoreSlim(_config.MaxConcurrentPackages, _config.MaxConcurrentPackages);
    }

    private async Task ProcessPackageAsync(DownloadPackage package, CancellationToken cancellationToken)
    {
        await _packageSemaphore.WaitAsync(cancellationToken);
        try
        {
            package.Status = PackageStatus.Downloading;
            PackageStatusChanged?.Invoke(this, package);

            var itemSemaphore = new SemaphoreSlim(_config.MaxConcurrentDownloadsPerPackage, _config.MaxConcurrentDownloadsPerPackage);
            var itemsToDownload = package.Items.Where(i => i.Status != DownloadItemStatus.Completed).ToList();

            var itemTasks = itemsToDownload.Select(async item =>
            {
                await itemSemaphore.WaitAsync(cancellationToken);
                try
                {
                    await ProcessItemAsync(package, item, cancellationToken);
                }
                finally
                {
                    itemSemaphore.Release();
                }
            });

            await Task.WhenAll(itemTasks);

            // Check if all completed
            if (package.Items.All(i => i.Status == DownloadItemStatus.Completed))
            {
                package.Status = PackageStatus.Completed;
                PackageStatusChanged?.Invoke(this, package);
                PackageCompleted?.Invoke(this, package);
                await _stateStore.FlushPendingSavesAsync();

                // Auto Extract if enabled
                if (_config.AutoExtract && _archiveExtractor != null)
                {
                    var isAnyArchive = package.Items.Any(i => _archiveExtractor.IsArchive(i.FileName));
                    if (isAnyArchive)
                    {
                        package.Status = PackageStatus.Extracting;
                        PackageStatusChanged?.Invoke(this, package);

                        bool extracted = await _archiveExtractor.ExtractPackageAsync(package, null, cancellationToken);
                        package.Status = extracted ? PackageStatus.Extracted : PackageStatus.Failed;
                        PackageStatusChanged?.Invoke(this, package);
                        await _stateStore.FlushPendingSavesAsync();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (package.Status == PackageStatus.Downloading)
            {
                package.Status = PackageStatus.Paused;
                PackageStatusChanged?.Invoke(this, package);
            }
        }
        catch (Exception)
        {
            package.Status = PackageStatus.Failed;
            PackageStatusChanged?.Invoke(this, package);
        }
        finally
        {
            _packageSemaphore.Release();
        }
    }

    private async Task ProcessItemAsync(DownloadPackage package, DownloadItem item, CancellationToken cancellationToken)
    {
        item.Status = DownloadItemStatus.Downloading;
        item.ErrorMessage = null;

        // If filename is generic or a bare token (missing extension), attempt a fast probe to resolve real name
        if (LinkGrabberService.IsGenericOrTokenFileName(item.FileName))
        {
            try
            {
                using var probeReq = new HttpRequestMessage(HttpMethod.Get, item.Url);
                probeReq.Headers.Range = new RangeHeaderValue(0, 0);
                using var probeResp = await _httpClient.SendAsync(probeReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var headerName = LinkGrabberService.TryExtractFileNameFromHeaders(probeResp);
                if (!string.IsNullOrWhiteSpace(headerName) && !LinkGrabberService.IsGenericOrTokenFileName(headerName))
                {
                    item.FileName = headerName;
                    if (LinkGrabberService.IsGenericOrTokenFileName(package.Name) || package.Name.StartsWith("Package_"))
                    {
                        package.Name = LinkGrabberService.DerivePackageNameStatic(headerName);
                        PackageStatusChanged?.Invoke(this, package);
                    }
                }
            }
            catch { }
        }

        var partFilePath = _fileStorage.GetPartFilePath(package.DestinationDirectory, item.FileName);
        var finalFilePath = _fileStorage.GetFinalFilePath(package.DestinationDirectory, item.FileName);

        SafeFileHandle? fileHandle = null;

        try
        {
            fileHandle = _fileStorage.OpenOrCreatePartFile(partFilePath, item.TotalBytes);

            // Setup chunks if needed
            PrepareChunks(item);

            // Download chunks in parallel
            var chunkTasks = item.Chunks.Where(c => !c.IsCompleted).Select(chunk =>
                DownloadChunkAsync(item, chunk, fileHandle, cancellationToken));

            await Task.WhenAll(chunkTasks);

            // Byte count check and finalize
            long totalDownloaded = item.Chunks.Sum(c => c.DownloadedBytes);
            item.DownloadedBytes = totalDownloaded;

            if (item.TotalBytes > 0 && totalDownloaded < item.TotalBytes)
            {
                throw new InvalidOperationException($"Incomplete download: expected {item.TotalBytes} bytes, got {totalDownloaded}");
            }

            fileHandle.Dispose();
            fileHandle = null;

            var finalFileName = !string.IsNullOrWhiteSpace(item.ServerFileName) && !LinkGrabberService.IsGenericOrTokenFileName(item.ServerFileName)
                ? item.ServerFileName
                : item.FileName;

            finalFilePath = _fileStorage.GetFinalFilePath(package.DestinationDirectory, finalFileName);

            _fileStorage.FinalizePartFile(partFilePath, finalFilePath);
            item.FileName = finalFileName;

            if (LinkGrabberService.IsGenericOrTokenFileName(package.Name) || package.Name.StartsWith("Package_"))
            {
                package.Name = LinkGrabberService.DerivePackageNameStatic(finalFileName);
                PackageStatusChanged?.Invoke(this, package);
            }

            item.Status = DownloadItemStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadItemStatus.Paused;
            throw;
        }
        catch (Exception ex)
        {
            item.Status = DownloadItemStatus.Failed;
            item.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            fileHandle?.Dispose();
        }
    }

    private void PrepareChunks(DownloadItem item)
    {
        if (item.Chunks.Count > 0)
            return;

        if (item.TotalBytes > 0 && item.SupportsRange)
        {
            int numChunks = Math.Clamp(_config.ChunksPerFile, 1, 32);
            if (item.TotalBytes < numChunks)
            {
                numChunks = Math.Max(1, (int)item.TotalBytes);
            }

            long chunkSize = item.TotalBytes / numChunks;
            for (int i = 0; i < numChunks; i++)
            {
                long start = i * chunkSize;
                long end = (i == numChunks - 1) ? (item.TotalBytes - 1) : ((i + 1) * chunkSize - 1);
                item.Chunks.Add(new DownloadChunk(i, start, end));
            }
        }
        else
        {
            // Non-range or unknown total size -> single chunk
            item.Chunks.Add(new DownloadChunk(0, 0, -1));
        }
    }

    private async Task DownloadChunkAsync(
        DownloadItem item,
        DownloadChunk chunk,
        SafeFileHandle fileHandle,
        CancellationToken cancellationToken)
    {
        if (chunk.IsCompleted)
            return;

        long startByte = chunk.CurrentByteOffset;
        long endByte = chunk.EndByte;

        if (endByte > 0 && startByte > endByte)
        {
            chunk.IsCompleted = true;
            return;
        }

        try
        {
            await _retryPipeline.ExecuteAsync(async ct =>
            {
                long currentOffset = chunk.CurrentByteOffset;
                using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
                if (endByte > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(currentOffset, endByte);
                }
                else if (currentOffset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(currentOffset, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                if (chunk.ChunkIndex == 0)
                {
                    var detectedName = LinkGrabberService.TryExtractFileNameFromHeaders(response);
                    if (!string.IsNullOrWhiteSpace(detectedName) && !LinkGrabberService.IsGenericOrTokenFileName(detectedName))
                    {
                        item.ServerFileName = detectedName;
                    }
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[32 * 1024];

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0)
                        break;

                    await _bandwidthLimiter.ConsumeAsync(read, ct);

                    await _fileStorage.WriteAsync(fileHandle, buffer.AsMemory(0, read), chunk.CurrentByteOffset, ct);

                    chunk.CurrentByteOffset += read;
                    Interlocked.Add(ref _bytesDownloadedSinceLastInterval, read);

                    // Update item's downloaded bytes
                    lock (item.Chunks)
                    {
                        item.DownloadedBytes = item.Chunks.Sum(c => c.DownloadedBytes);
                    }

                    _stateStore.RequestThrottledSave(Packages);
                }

                if (endByte > 0 && chunk.CurrentByteOffset >= endByte + 1)
                {
                    chunk.IsCompleted = true;
                }
                else if (endByte <= 0)
                {
                    chunk.IsCompleted = true;
                    item.TotalBytes = item.DownloadedBytes;
                }

                return response;
            }, cancellationToken);
        }
        catch (Exception)
        {
            item.RetryCount++;
            throw;
        }
    }

    private void OnProgressTimerTick(object? state)
    {
        long bytesInInterval = Interlocked.Exchange(ref _bytesDownloadedSinceLastInterval, 0);
        // Interval is 250ms, so speed in B/s is bytes * 4
        long currentSpeed = bytesInInterval * 4;

        // Exponential smoothing (EWMA): speed = 0.7 * last + 0.3 * current
        long prevSpeed = Interlocked.Read(ref _aggregateSpeed);
        long smoothedSpeed = prevSpeed == 0 ? currentSpeed : (long)(0.7 * prevSpeed + 0.3 * currentSpeed);
        Interlocked.Exchange(ref _aggregateSpeed, smoothedSpeed);

        var args = new EngineProgressEventArgs
        {
            AggregateSpeedBytesPerSecond = smoothedSpeed,
            ActiveTasksCount = ActiveTasksCount,
            TotalQueueCount = Packages.Count,
            Packages = Packages
        };

        ProgressUpdated?.Invoke(this, args);
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _progressTimer.Dispose();
        _globalCts.Cancel();

        foreach (var cts in _packageCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        await _stateStore.FlushPendingSavesAsync();
        _packageSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
