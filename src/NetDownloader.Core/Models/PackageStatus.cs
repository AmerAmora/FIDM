namespace NetDownloader.Core.Models;

public enum PackageStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Extracting,
    Extracted,
    Failed
}
