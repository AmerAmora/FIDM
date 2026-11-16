using NetDownloader.Core.Models;

namespace NetDownloader.Core.Extraction;

public interface IArchiveExtractor
{
    bool IsArchive(string fileName);
    Task<bool> ExtractPackageAsync(DownloadPackage package, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
