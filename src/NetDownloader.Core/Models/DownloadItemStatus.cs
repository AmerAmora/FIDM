namespace NetDownloader.Core.Models;

public enum DownloadItemStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Extracting,
    Failed,
    Online,
    Offline
}
