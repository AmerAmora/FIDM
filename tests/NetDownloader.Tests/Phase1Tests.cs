using NetDownloader.Core.Models;
using Xunit;

namespace NetDownloader.Tests;

public class Phase1Tests
{
    [Fact]
    public void DomainModels_Initialization_ShouldWorkAsExpected()
    {
        var package = new DownloadPackage("Ubuntu_Studio_Backup", "C:\\Downloads");
        Assert.Equal("Ubuntu_Studio_Backup", package.Name);
        Assert.Equal("C:\\Downloads", package.DestinationDirectory);
        Assert.Equal(PackageStatus.Queued, package.Status);

        var item = new DownloadItem("https://example.com/Ubuntu_Studio_Backup.part1.rar", "Ubuntu_Studio_Backup.part1.rar")
        {
            TotalBytes = 1000,
            DownloadedBytes = 500,
            SupportsRange = true
        };
        package.Items.Add(item);

        Assert.Equal(1000, package.TotalBytes);
        Assert.Equal(500, package.DownloadedBytes);
        Assert.Equal(50.0, package.ProgressPercentage);

        var chunk = new DownloadChunk(0, 0, 499)
        {
            CurrentByteOffset = 250
        };
        Assert.Equal(500, chunk.TotalBytes);
        Assert.Equal(250, chunk.DownloadedBytes);
        Assert.Equal(50.0, chunk.ProgressPercentage);

        var config = new EngineConfiguration();
        Assert.Equal(3, config.MaxConcurrentPackages);
        Assert.Equal(5, config.MaxConcurrentDownloadsPerPackage);
        Assert.Equal(8, config.ChunksPerFile);
        Assert.Equal(0, config.MaxSpeedBytesPerSecond);
        Assert.True(config.AutoExtract);
        Assert.False(config.DeleteArchiveAfterExtract);
    }
}
