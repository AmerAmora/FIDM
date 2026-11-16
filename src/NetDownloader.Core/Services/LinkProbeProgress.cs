using NetDownloader.Core.Models;

namespace NetDownloader.Core.Services;

public class LinkProbeProgress
{
    public int TotalUrls { get; set; }
    public int ProbedUrls { get; set; }
    public string CurrentUrl { get; set; } = string.Empty;
    public DownloadItem? Item { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public double Percentage => TotalUrls > 0 ? (double)ProbedUrls / TotalUrls * 100.0 : 0.0;
}
