using System.Text;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Extraction;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Storage;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using Xunit;

namespace NetDownloader.Tests;

public class Phase5Tests : IDisposable
{
    private readonly string _testDir;

    public Phase5Tests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NetDownloader_Phase5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("Ubuntu_Studio_Backup.part1.rar", true)]
    [InlineData("Ubuntu_Studio_Backup.part02.rar", true)]
    [InlineData("archive.r00", true)]
    [InlineData("archive.r01", true)]
    [InlineData("split.7z.001", true)]
    [InlineData("data.zip", true)]
    [InlineData("file.txt", false)]
    [InlineData("movie.mp4", false)]
    [InlineData("setup.exe", false)]
    public void IsArchive_ShouldIdentifyArchivesCorrectly(string fileName, bool expected)
    {
        var extractor = new ArchiveExtractor(new FileStorageService());
        Assert.Equal(expected, extractor.IsArchive(fileName));
    }

    [Fact]
    public void FindPrimaryVolume_ShouldDetectFirstVolume()
    {
        var extractor = new ArchiveExtractor(new FileStorageService());

        var filesRar = new[]
        {
            Path.Combine(_testDir, "Ubuntu.part2.rar"),
            Path.Combine(_testDir, "Ubuntu.part1.rar"),
            Path.Combine(_testDir, "Ubuntu.part3.rar")
        };
        var primaryRar = extractor.FindPrimaryVolume(filesRar);
        Assert.Equal(Path.Combine(_testDir, "Ubuntu.part1.rar"), primaryRar);

        var filesSplit = new[]
        {
            Path.Combine(_testDir, "data.002"),
            Path.Combine(_testDir, "data.001"),
            Path.Combine(_testDir, "data.003")
        };
        var primarySplit = extractor.FindPrimaryVolume(filesSplit);
        Assert.Equal(Path.Combine(_testDir, "data.001"), primarySplit);
    }

    private class MockArchiveExtractor : IArchiveExtractor
    {
        public int ExtractionTriggerCount { get; private set; }
        public DownloadPackage? ExtractedPackage { get; private set; }

        public bool IsArchive(string fileName) => true;

        public Task<bool> ExtractPackageAsync(DownloadPackage package, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            ExtractionTriggerCount++;
            ExtractedPackage = package;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task DownloadEngine_ExtractionTriggersOnlyWhenFinalPartCompletes()
    {
        var storage = new FileStorageService();
        await using var store = new JsonStateStore(_testDir);
        var mockExtractor = new MockArchiveExtractor();

        var config = new EngineConfiguration
        {
            AutoExtract = true,
            ChunksPerFile = 1,
            MaxConcurrentPackages = 1,
            MaxConcurrentDownloadsPerPackage = 1
        };

        // Create dummy part data
        byte[] dummyData = Encoding.UTF8.GetBytes("Archive part data");
        var handler = new SimpleHandler(dummyData);
        var httpClient = new HttpClient(handler);

        await using var engine = new DownloadEngine(storage, store, archiveExtractor: mockExtractor, httpClient: httpClient, configuration: config);

        var package = new DownloadPackage("MultiPartExtractTest", _testDir);
        var item1 = new DownloadItem("https://test.local/part1.rar", "archive.part1.rar")
        {
            TotalBytes = dummyData.Length,
            SupportsRange = false
        };
        var item2 = new DownloadItem("https://test.local/part2.rar", "archive.part2.rar")
        {
            TotalBytes = dummyData.Length,
            SupportsRange = false
        };

        package.Items.Add(item1);
        package.Items.Add(item2);

        await engine.EnqueuePackageAsync(package);

        // Before starting
        Assert.Equal(0, mockExtractor.ExtractionTriggerCount);

        var tcs = new TaskCompletionSource<bool>();
        engine.PackageStatusChanged += (_, pkg) =>
        {
            if (pkg.Id == package.Id && pkg.Status is PackageStatus.Extracted or PackageStatus.Failed)
                tcs.TrySetResult(true);
        };

        await engine.ResumePackageAsync(package.Id);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Same(tcs.Task, completed);

        // Verify extraction triggered exactly once, when both parts completed
        Assert.Equal(1, mockExtractor.ExtractionTriggerCount);
        Assert.Same(package, mockExtractor.ExtractedPackage);
        Assert.Equal(PackageStatus.Extracted, package.Status);
    }

    [Fact]
    public async Task ArchiveExtractor_ExtractZipArchive_ShouldExtractFiles()
    {
        var storage = new FileStorageService();
        var extractor = new ArchiveExtractor(storage);

        // Create a real zip archive
        var zipPath = Path.Combine(_testDir, "test_archive.zip");
        using (var archive = ZipArchive.CreateArchive())
        {
            using var textStream = new MemoryStream(Encoding.UTF8.GetBytes("Hello Avalonia NetDownloader!"));
            archive.AddEntry("greeting.txt", textStream, textStream.Length);
            archive.SaveTo(zipPath, CompressionType.Deflate);
        }

        var package = new DownloadPackage("TestZip", _testDir);
        package.Items.Add(new DownloadItem("https://example.com/test_archive.zip", "test_archive.zip")
        {
            Status = DownloadItemStatus.Completed
        });

        var progressList = new List<double>();
        var progress = new Progress<double>(p => progressList.Add(p));

        bool success = await extractor.ExtractPackageAsync(package, progress);

        Assert.True(success);
        var extractedFilePath = Path.Combine(_testDir, "greeting.txt");
        Assert.True(File.Exists(extractedFilePath));
        var content = await File.ReadAllTextAsync(extractedFilePath);
        Assert.Equal("Hello Avalonia NetDownloader!", content);
    }

    private class SimpleHandler : HttpMessageHandler
    {
        private readonly byte[] _data;

        public SimpleHandler(byte[] data)
        {
            _data = data;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_data)
            };
            return Task.FromResult(resp);
        }
    }
}
