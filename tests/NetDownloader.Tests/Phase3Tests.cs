using System.Text;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Storage;
using Xunit;

namespace NetDownloader.Tests;

public class Phase3Tests : IDisposable
{
    private readonly string _testDir;

    public Phase3Tests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NetDownloader_Phase3_" + Guid.NewGuid().ToString("N"));
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
    public async Task StateStore_SaveAndLoadPackages_ShouldPreserveChunkOffsetsAndState()
    {
        await using var store = new JsonStateStore(_testDir);

        var package = new DownloadPackage("TestPackage", _testDir)
        {
            Status = PackageStatus.Downloading,
            ExtractionPassword = "secret-password"
        };

        var item = new DownloadItem("https://example.com/part1.rar", "part1.rar")
        {
            TotalBytes = 10000,
            DownloadedBytes = 5000,
            SupportsRange = true,
            Status = DownloadItemStatus.Downloading,
            Chunks = new List<DownloadChunk>
            {
                new DownloadChunk(0, 0, 4999) { CurrentByteOffset = 2500, IsCompleted = false },
                new DownloadChunk(1, 5000, 9999) { CurrentByteOffset = 10000, IsCompleted = true }
            }
        };

        package.Items.Add(item);

        await store.SavePackagesAsync(new[] { package });

        var loaded = await store.LoadPackagesAsync();

        Assert.Single(loaded);
        var loadedPkg = loaded[0];
        Assert.Equal("TestPackage", loadedPkg.Name);
        Assert.Equal("secret-password", loadedPkg.ExtractionPassword);
        Assert.Equal(PackageStatus.Downloading, loadedPkg.Status);

        Assert.Single(loadedPkg.Items);
        var loadedItem = loadedPkg.Items[0];
        Assert.Equal("part1.rar", loadedItem.FileName);
        Assert.Equal(10000, loadedItem.TotalBytes);
        Assert.Equal(2, loadedItem.Chunks.Count);

        Assert.Equal(2500, loadedItem.Chunks[0].CurrentByteOffset);
        Assert.False(loadedItem.Chunks[0].IsCompleted);

        Assert.Equal(10000, loadedItem.Chunks[1].CurrentByteOffset);
        Assert.True(loadedItem.Chunks[1].IsCompleted);
    }

    [Fact]
    public async Task StateStore_ThrottledSaveAndFlush_ShouldPersistState()
    {
        await using var store = new JsonStateStore(_testDir);

        var package = new DownloadPackage("ThrottledPackage", _testDir)
        {
            Status = PackageStatus.Downloading
        };

        store.RequestThrottledSave(new[] { package });
        await store.FlushPendingSavesAsync();

        var loaded = await store.LoadPackagesAsync();
        Assert.Single(loaded);
        Assert.Equal("ThrottledPackage", loaded[0].Name);
    }

    [Fact]
    public async Task StateStore_SettingsPersistence_ShouldSaveAndLoad()
    {
        await using var store = new JsonStateStore(_testDir);

        var config = new EngineConfiguration
        {
            MaxConcurrentPackages = 7,
            MaxConcurrentDownloadsPerPackage = 10,
            ChunksPerFile = 16,
            MaxSpeedBytesPerSecond = 1024 * 1024 * 15,
            AutoExtract = false,
            DeleteArchiveAfterExtract = true
        };

        await store.SaveSettingsAsync(config);

        var loaded = await store.LoadSettingsAsync();
        Assert.Equal(7, loaded.MaxConcurrentPackages);
        Assert.Equal(10, loaded.MaxConcurrentDownloadsPerPackage);
        Assert.Equal(16, loaded.ChunksPerFile);
        Assert.Equal(1024 * 1024 * 15, loaded.MaxSpeedBytesPerSecond);
        Assert.False(loaded.AutoExtract);
        Assert.True(loaded.DeleteArchiveAfterExtract);
    }

    [Fact]
    public async Task FileStorageService_ConcurrentRandomAccessWrites_ShouldWriteAccurately()
    {
        var storage = new FileStorageService();
        var partFilePath = storage.GetPartFilePath(_testDir, "testfile.bin");
        var finalFilePath = storage.GetFinalFilePath(_testDir, "testfile.bin");

        const int numChunks = 8;
        const int chunkSize = 4096;
        const long totalSize = numChunks * chunkSize;

        // Preallocate
        using (var handle = storage.OpenOrCreatePartFile(partFilePath, totalSize))
        {
            Assert.Equal(totalSize, RandomAccess.GetLength(handle));

            // Concurrently write 8 chunks
            var tasks = Enumerable.Range(0, numChunks).Select(async i =>
            {
                long offset = i * chunkSize;
                byte[] pattern = new byte[chunkSize];
                Array.Fill(pattern, (byte)(i + 1));

                await storage.WriteAsync(handle, pattern, offset);
            });

            await Task.WhenAll(tasks);
        }

        // Finalize
        storage.FinalizePartFile(partFilePath, finalFilePath);

        Assert.False(File.Exists(partFilePath));
        Assert.True(File.Exists(finalFilePath));
        Assert.Equal(totalSize, new FileInfo(finalFilePath).Length);

        // Verify contents
        var fileBytes = await File.ReadAllBytesAsync(finalFilePath);
        for (int i = 0; i < numChunks; i++)
        {
            int offset = i * chunkSize;
            byte expected = (byte)(i + 1);
            for (int b = 0; b < chunkSize; b++)
            {
                Assert.Equal(expected, fileBytes[offset + b]);
            }
        }
    }
}
