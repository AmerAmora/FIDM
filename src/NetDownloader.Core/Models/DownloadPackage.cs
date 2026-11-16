namespace NetDownloader.Core.Models;

public class DownloadPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string DestinationDirectory { get; set; } = string.Empty;
    public string? ExtractionPassword { get; set; }
    public PackageStatus Status { get; set; } = PackageStatus.Queued;
    public List<DownloadItem> Items { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long TotalBytes => Items.Sum(i => i.TotalBytes > 0 ? i.TotalBytes : 0);
    public long DownloadedBytes => Items.Sum(i => i.DownloadedBytes);
    public double ProgressPercentage => TotalBytes > 0
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0)
        : (Status is PackageStatus.Completed or PackageStatus.Extracted ? 100.0 : 0.0);

    public DownloadPackage()
    {
    }

    public DownloadPackage(string name, string destinationDirectory)
    {
        Name = name;
        DestinationDirectory = destinationDirectory;
    }
}
