using NetDownloader.Core.Models;

namespace NetDownloader.Core.Engine;

public interface IDownloadEngine : IAsyncDisposable
{
    EngineConfiguration Configuration { get; }
    IReadOnlyList<DownloadPackage> Packages { get; }
    long AggregateSpeedBytesPerSecond { get; }
    int ActiveTasksCount { get; }

    event EventHandler<EngineProgressEventArgs>? ProgressUpdated;
    event EventHandler<DownloadPackage>? PackageCompleted;
    event EventHandler<DownloadPackage>? PackageStatusChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task EnqueuePackageAsync(DownloadPackage package, CancellationToken cancellationToken = default);
    Task EnqueuePackagesAsync(IEnumerable<DownloadPackage> packages, CancellationToken cancellationToken = default);
    Task StartAllAsync(CancellationToken cancellationToken = default);
    Task PauseAllAsync(CancellationToken cancellationToken = default);
    Task PausePackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task ResumePackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task RemovePackageAsync(Guid packageId, bool deleteFiles = false, CancellationToken cancellationToken = default);
    void UpdateSpeedLimit(long maxSpeedBytesPerSecond);
    void UpdateConfiguration(EngineConfiguration newConfig);
}
