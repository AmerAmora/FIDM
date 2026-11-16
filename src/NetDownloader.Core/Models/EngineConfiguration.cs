namespace NetDownloader.Core.Models;

public class EngineConfiguration
{
    public string DefaultDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "NetDownloader"
    );

    public int MaxConcurrentPackages { get; set; } = 3;
    public int MaxConcurrentDownloadsPerPackage { get; set; } = 5;
    public int ChunksPerFile { get; set; } = 8;
    public long MaxSpeedBytesPerSecond { get; set; } = 0; // 0 = unlimited
    public bool AutoExtract { get; set; } = true;
    public bool DeleteArchiveAfterExtract { get; set; } = false;
}
