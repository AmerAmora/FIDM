using System.Net;
using System.Net.Http.Headers;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Storage;
using Xunit;

namespace NetDownloader.Tests;

public class Phase4Tests : IDisposable
{
    private readonly string _testDir;

    public Phase4Tests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NetDownloader_Phase4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BandwidthLimiter_Unlimited_ShouldReturnImmediately()
    {
        var limiter = new BandwidthLimiter(0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.ConsumeAsync(1024 * 1024);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 50);
    }

    [Fact]
    public async Task SegmentedDownload_ShouldDownloadAndReassembleInParallel()
    {
        // 16 KB data
        byte[] sourceData = new byte[16384];
        new Random(42).NextBytes(sourceData);

        var handler = new MockRangeHandler(sourceData);
        var httpClient = new HttpClient(handler);
        var storage = new FileStorageService();
        await using var store = new JsonStateStore(_testDir);

        var config = new EngineConfiguration
        {
            ChunksPerFile = 4,
            MaxConcurrentPackages = 1,
            MaxConcurrentDownloadsPerPackage = 1
        };

        await using var engine = new DownloadEngine(storage, store, httpClient: httpClient, configuration: config);

        var package = new DownloadPackage("TestSegmented", _testDir);
        var item = new DownloadItem("https://test.local/data.bin", "data.bin")
        {
            TotalBytes = sourceData.Length,
            SupportsRange = true
        };
        package.Items.Add(item);

        await engine.EnqueuePackageAsync(package);

        var tcs = new TaskCompletionSource<bool>();
        engine.PackageCompleted += (_, pkg) =>
        {
            if (pkg.Id == package.Id)
                tcs.TrySetResult(true);
        };

        await engine.ResumePackageAsync(package.Id);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(PackageStatus.Completed, package.Status);

        var finalPath = Path.Combine(_testDir, "data.bin");
        Assert.True(File.Exists(finalPath));
        var writtenData = await File.ReadAllBytesAsync(finalPath);
        Assert.Equal(sourceData, writtenData);
        Assert.Equal(4, item.Chunks.Count);
        Assert.All(item.Chunks, c => Assert.True(c.IsCompleted));
    }

    [Fact]
    public async Task PauseAndResume_ShouldResumeFromExistingOffsets()
    {
        byte[] sourceData = new byte[8192];
        new Random(123).NextBytes(sourceData);

        var handler = new MockRangeHandler(sourceData);
        var httpClient = new HttpClient(handler);
        var storage = new FileStorageService();
        await using var store = new JsonStateStore(_testDir);

        var config = new EngineConfiguration
        {
            ChunksPerFile = 2,
            MaxConcurrentPackages = 1
        };

        await using var engine = new DownloadEngine(storage, store, httpClient: httpClient, configuration: config);

        var package = new DownloadPackage("PauseResumeTest", _testDir);
        var item = new DownloadItem("https://test.local/resumable.bin", "resumable.bin")
        {
            TotalBytes = sourceData.Length,
            SupportsRange = true
        };
        // Pre-simulate chunk 0 already half done
        item.Chunks.Add(new DownloadChunk(0, 0, 4095) { CurrentByteOffset = 2048 });
        item.Chunks.Add(new DownloadChunk(1, 4096, 8191) { CurrentByteOffset = 4096 });
        item.DownloadedBytes = 2048;

        // Write first 2048 bytes to part file
        var partFile = storage.GetPartFilePath(_testDir, "resumable.bin");
        using (var h = storage.OpenOrCreatePartFile(partFile, sourceData.Length))
        {
            await storage.WriteAsync(h, sourceData.AsMemory(0, 2048), 0);
        }

        package.Items.Add(item);
        await engine.EnqueuePackageAsync(package);

        var tcs = new TaskCompletionSource<bool>();
        engine.PackageCompleted += (_, pkg) =>
        {
            if (pkg.Id == package.Id)
                tcs.TrySetResult(true);
        };

        await engine.ResumePackageAsync(package.Id);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(PackageStatus.Completed, package.Status);

        var finalPath = Path.Combine(_testDir, "resumable.bin");
        Assert.True(File.Exists(finalPath));
        var writtenData = await File.ReadAllBytesAsync(finalPath);
        Assert.Equal(sourceData, writtenData);
    }

    [Fact]
    public async Task PollyRetry_TransientHttpFailures_ShouldRecoverAndComplete()
    {
        byte[] sourceData = new byte[2048];
        new Random(77).NextBytes(sourceData);

        int failureCount = 0;
        var handler = new MockFailingRangeHandler(sourceData, maxFailures: 2, () => failureCount++);
        var httpClient = new HttpClient(handler);
        var storage = new FileStorageService();
        await using var store = new JsonStateStore(_testDir);

        var config = new EngineConfiguration { ChunksPerFile = 1 };
        await using var engine = new DownloadEngine(storage, store, httpClient: httpClient, configuration: config);

        var package = new DownloadPackage("RetryTest", _testDir);
        var item = new DownloadItem("https://test.local/retry.bin", "retry.bin")
        {
            TotalBytes = sourceData.Length,
            SupportsRange = true
        };
        package.Items.Add(item);

        await engine.EnqueuePackageAsync(package);

        var tcs = new TaskCompletionSource<bool>();
        engine.PackageCompleted += (_, pkg) =>
        {
            if (pkg.Id == package.Id)
                tcs.TrySetResult(true);
        };

        await engine.ResumePackageAsync(package.Id);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(8000));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(PackageStatus.Completed, package.Status);
        Assert.True(failureCount >= 2);
    }

    private class MockRangeHandler : HttpMessageHandler
    {
        private readonly byte[] _data;

        public MockRangeHandler(byte[] data)
        {
            _data = data;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            long start = 0;
            long end = _data.Length - 1;

            if (request.Headers.Range != null && request.Headers.Range.Ranges.Count > 0)
            {
                var r = request.Headers.Range.Ranges.First();
                start = r.From ?? 0;
                end = r.To ?? (_data.Length - 1);
            }

            end = Math.Min(end, _data.Length - 1);
            long length = Math.Max(0, end - start + 1);

            byte[] slice = new byte[length];
            Array.Copy(_data, start, slice, 0, length);

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice)
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _data.Length);
            response.Content.Headers.ContentLength = length;
            return Task.FromResult(response);
        }
    }

    private class MockFailingRangeHandler : HttpMessageHandler
    {
        private readonly byte[] _data;
        private readonly int _maxFailures;
        private readonly Action _onFailure;
        private int _attempts;

        public MockFailingRangeHandler(byte[] data, int maxFailures, Action onFailure)
        {
            _data = data;
            _maxFailures = maxFailures;
            _onFailure = onFailure;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) <= _maxFailures)
            {
                _onFailure();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_data)
            };
            response.Content.Headers.ContentLength = _data.Length;
            return Task.FromResult(response);
        }
    }
}
