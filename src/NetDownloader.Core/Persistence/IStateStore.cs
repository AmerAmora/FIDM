using NetDownloader.Core.Models;

namespace NetDownloader.Core.Persistence;

public interface IStateStore : IAsyncDisposable
{
    string BaseDirectory { get; }
    Task SavePackagesAsync(IReadOnlyList<DownloadPackage> packages, CancellationToken cancellationToken = default);
    Task<List<DownloadPackage>> LoadPackagesAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(EngineConfiguration configuration, CancellationToken cancellationToken = default);
    Task<EngineConfiguration> LoadSettingsAsync(CancellationToken cancellationToken = default);
    void RequestThrottledSave(IReadOnlyList<DownloadPackage> packages);
    Task FlushPendingSavesAsync();
}
