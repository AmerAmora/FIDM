using NetDownloader.Core.Models;

namespace NetDownloader.Core.Engine;

public class EngineProgressEventArgs : EventArgs
{
    public long AggregateSpeedBytesPerSecond { get; set; }
    public int ActiveTasksCount { get; set; }
    public int TotalQueueCount { get; set; }
    public IReadOnlyList<DownloadPackage> Packages { get; set; } = Array.Empty<DownloadPackage>();
}
