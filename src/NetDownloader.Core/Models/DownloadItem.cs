namespace NetDownloader.Core.Models;

public class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ServerFileName { get; set; }
    public long TotalBytes { get; set; } = -1;
    public long DownloadedBytes { get; set; }
    public bool SupportsRange { get; set; }
    public DownloadItemStatus Status { get; set; } = DownloadItemStatus.Queued;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public List<DownloadChunk> Chunks { get; set; } = new();

    public double ProgressPercentage => TotalBytes > 0 
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0) 
        : (Status == DownloadItemStatus.Completed ? 100.0 : 0.0);

    public DownloadItem()
    {
    }

    public DownloadItem(string url, string fileName)
    {
        Url = url;
        FileName = fileName;
    }
}
