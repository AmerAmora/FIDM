using NetDownloader.Core.Models;

namespace NetDownloader.Core.Services;

public interface ILinkGrabberService
{
    List<string> ExtractUrls(string content);
    Task<List<string>> ExtractUrlsFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<DownloadItem> ProbeUrlAsync(string url, CancellationToken cancellationToken = default);
    Task<List<DownloadPackage>> AnalyzeAsync(string rawText, string defaultDestinationDirectory, IProgress<LinkProbeProgress>? progress = null, CancellationToken cancellationToken = default);
    string DerivePackageName(string fileName);
}
