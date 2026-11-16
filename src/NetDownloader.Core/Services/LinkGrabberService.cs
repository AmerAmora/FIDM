using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using NetDownloader.Core.Models;

namespace NetDownloader.Core.Services;

public partial class LinkGrabberService : ILinkGrabberService
{
    private readonly HttpClient _httpClient;

    [GeneratedRegex(@"https?://[^\s""'<>\(\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?<base>.+?)\.part\d+(\.[a-zA-Z0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PartPatternRegex();

    [GeneratedRegex(@"^(?<base>.+?)\.(?:r\d{2}|s\d{2}|z\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeRxxPatternRegex();

    [GeneratedRegex(@"^(?<base>.+?)(?:\.[a-zA-Z0-9]+)?\.\d{3,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex SplitNumPatternRegex();

    public LinkGrabberService() : this(null)
    {
    }

    public LinkGrabberService(HttpClient? httpClient)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    public List<string> ExtractUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<string>();

        var matches = UrlRegex().Matches(content);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var url = CleanUrl(match.Value);
            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
                (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                urls.Add(uriResult.AbsoluteUri);
            }
        }

        return urls.ToList();
    }

    public async Task<List<string>> ExtractUrlsFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new List<string>();

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ExtractUrls(content);
    }

    public string DerivePackageName(string fileName)
    {
        return DerivePackageNameStatic(fileName);
    }

    public static string DerivePackageNameStatic(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Package_" + Guid.NewGuid().ToString("N")[..6];

        var nameOnly = Path.GetFileName(fileName.Trim());

        var matchPart = PartPatternRegex().Match(nameOnly);
        if (matchPart.Success && matchPart.Groups["base"].Success)
            return matchPart.Groups["base"].Value;

        var matchRxx = VolumeRxxPatternRegex().Match(nameOnly);
        if (matchRxx.Success && matchRxx.Groups["base"].Success)
            return matchRxx.Groups["base"].Value;

        var matchNum = SplitNumPatternRegex().Match(nameOnly);
        if (matchNum.Success && matchNum.Groups["base"].Success)
            return matchNum.Groups["base"].Value;

        var withoutExt = Path.GetFileNameWithoutExtension(nameOnly);
        return string.IsNullOrWhiteSpace(withoutExt) ? nameOnly : withoutExt;
    }

    public static bool IsGenericOrTokenFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        if (name.StartsWith("download_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Package_", StringComparison.OrdinalIgnoreCase))
            return true;

        // Bare hash token without extension (e.g. FlbPeRNeZqWGBXKTXSN5LOd8GAC7VQK...)
        if (!Path.HasExtension(name))
            return true;

        // Bare token longer than 25 chars without dots
        if (name.Length > 25 && !name.Contains('.'))
            return true;

        return false;
    }

    public static string? TryExtractFileNameFromHeaders(HttpResponseMessage response)
    {
        // 1. Try typed header
        try
        {
            if (response.Content.Headers.ContentDisposition != null)
            {
                var candidate = response.Content.Headers.ContentDisposition.FileNameStar 
                                ?? response.Content.Headers.ContentDisposition.FileName;
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    var clean = CleanFileNameCandidate(candidate);
                    if (!string.IsNullOrWhiteSpace(clean))
                        return clean;
                }
            }
        }
        catch { }

        // 2. Try raw Content-Disposition header
        IEnumerable<string>? values = null;
        if (response.Content.Headers.TryGetValues("Content-Disposition", out var cVals))
        {
            values = cVals;
        }
        else if (response.Headers.TryGetValues("Content-Disposition", out var hVals))
        {
            values = hVals;
        }

        if (values != null)
        {
            foreach (var header in values)
            {
                // Try filename*=UTF-8''... (RFC 5987)
                var starMatch = Regex.Match(header, @"filename\*\s*=\s*(?:[^\'']*\''[^\'']*\''|UTF-8\'\'|utf-8\'\')?([^;\r\n]+)", RegexOptions.IgnoreCase);
                if (starMatch.Success)
                {
                    var clean = CleanFileNameCandidate(starMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(clean))
                        return clean;
                }

                // Try filename="..." or filename=...
                var fnMatch = Regex.Match(header, @"filename\s*=\s*(?:""([^""]+)""|'([^']+)'|([^;\s\r\n]+))", RegexOptions.IgnoreCase);
                if (fnMatch.Success)
                {
                    var val = fnMatch.Groups[1].Success ? fnMatch.Groups[1].Value 
                            : fnMatch.Groups[2].Success ? fnMatch.Groups[2].Value 
                            : fnMatch.Groups[3].Value;
                    var clean = CleanFileNameCandidate(val);
                    if (!string.IsNullOrWhiteSpace(clean))
                        return clean;
                }
            }
        }

        return null;
    }

    private static string CleanFileNameCandidate(string raw)
    {
        var unescaped = Uri.UnescapeDataString(raw.Trim('\"', '\'', ' '));
        return SanitizeFileName(unescaped);
    }

    public async Task<DownloadItem> ProbeUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        var item = new DownloadItem
        {
            Url = url,
            FileName = ResolveFileNameFromUrl(url),
            Status = DownloadItemStatus.Queued
        };

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            HttpResponseMessage? headResponse = null;
            bool headSucceeded = false;

            try
            {
                headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (headResponse.IsSuccessStatusCode)
                {
                    headSucceeded = true;
                }
            }
            catch
            {
                headSucceeded = false;
            }

            if (headSucceeded && headResponse != null)
            {
                using (headResponse)
                {
                    ApplyResponseMetadata(headResponse, item);
                }

                // If HEAD didn't yield a filename with extension (e.g. CDNs that omit Content-Disposition on HEAD),
                // fall back to a lightweight GET Range: bytes=0-0 to get the real Content-Disposition
                if (IsGenericOrTokenFileName(item.FileName))
                {
                    await TryProbeWithGetRangeAsync(url, item, cancellationToken);
                }

                item.Status = DownloadItemStatus.Online;
                return item;
            }

            headResponse?.Dispose();

            // Fallback to GET with Range: bytes=0-0
            await TryProbeWithGetRangeAsync(url, item, cancellationToken);
        }
        catch (Exception ex)
        {
            item.Status = DownloadItemStatus.Offline;
            item.ErrorMessage = ex.Message;
        }

        return item;
    }

    private async Task TryProbeWithGetRangeAsync(string url, DownloadItem item, CancellationToken cancellationToken)
    {
        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
            getRequest.Headers.Range = new RangeHeaderValue(0, 0);

            using var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == HttpStatusCode.PartialContent)
            {
                ApplyResponseMetadata(getResponse, item);
                item.Status = DownloadItemStatus.Online;
            }
            else
            {
                item.Status = DownloadItemStatus.Offline;
                if (getResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    item.ErrorMessage = "HTTP 403 Forbidden: Host has Cloudflare Anti-Bot Turnstile protection or requires direct download URL";
                }
                else
                {
                    item.ErrorMessage = $"HTTP {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}";
                }
            }
        }
        catch (Exception ex)
        {
            item.Status = DownloadItemStatus.Offline;
            item.ErrorMessage = ex.Message;
        }
    }

    public async Task<List<DownloadPackage>> AnalyzeAsync(
        string rawTextOrFilePath,
        string defaultDestinationDirectory,
        IProgress<LinkProbeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<string> urls;
        if (File.Exists(rawTextOrFilePath))
        {
            urls = await ExtractUrlsFromFileAsync(rawTextOrFilePath, cancellationToken);
        }
        else
        {
            urls = ExtractUrls(rawTextOrFilePath);
        }

        if (urls.Count == 0)
            return new List<DownloadPackage>();

        var probedItems = new List<DownloadItem>();
        int total = urls.Count;
        int completedCount = 0;
        var locker = new object();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount * 2),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(urls, parallelOptions, async (url, ct) =>
        {
            var item = await ProbeUrlAsync(url, ct);

            lock (locker)
            {
                probedItems.Add(item);
                completedCount++;
                progress?.Report(new LinkProbeProgress
                {
                    TotalUrls = total,
                    ProbedUrls = completedCount,
                    CurrentUrl = url,
                    Item = item,
                    IsSuccess = item.Status == DownloadItemStatus.Online,
                    ErrorMessage = item.ErrorMessage
                });
            }
        });

        // Group into packages
        var packages = new List<DownloadPackage>();
        var grouped = probedItems.GroupBy(item => DerivePackageName(item.FileName));

        foreach (var group in grouped)
        {
            var package = new DownloadPackage(group.Key, defaultDestinationDirectory)
            {
                Status = PackageStatus.Queued,
                Items = group.OrderBy(i => i.FileName).ToList()
            };
            packages.Add(package);
        }

        return packages;
    }

    private void ApplyResponseMetadata(HttpResponseMessage response, DownloadItem item)
    {
        var finalUri = response.RequestMessage?.RequestUri?.AbsoluteUri ?? item.Url;

        // Filename extraction from response headers
        var headerFileName = TryExtractFileNameFromHeaders(response);
        if (!string.IsNullOrWhiteSpace(headerFileName))
        {
            item.FileName = headerFileName;
        }
        else if (IsGenericOrTokenFileName(item.FileName))
        {
            var fromUrl = ResolveFileNameFromUrl(item.Url);
            if (!IsGenericOrTokenFileName(fromUrl))
            {
                item.FileName = fromUrl;
            }
            else
            {
                var fromFinal = ResolveFileNameFromUrl(finalUri);
                if (!IsGenericOrTokenFileName(fromFinal))
                {
                    item.FileName = fromFinal;
                }
            }
        }

        // Range support
        bool supportsRange = false;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            supportsRange = true;
        }
        else if (response.Headers.AcceptRanges.Any(v => string.Equals(v, "bytes", StringComparison.OrdinalIgnoreCase)))
        {
            supportsRange = true;
        }

        item.SupportsRange = supportsRange;

        // Total bytes
        long totalBytes = -1;
        if (response.Content.Headers.ContentRange?.Length != null)
        {
            totalBytes = response.Content.Headers.ContentRange.Length.Value;
        }
        else if (response.Content.Headers.ContentLength != null && response.StatusCode != HttpStatusCode.PartialContent)
        {
            totalBytes = response.Content.Headers.ContentLength.Value;
        }

        if (totalBytes > 0 || item.TotalBytes <= 0)
        {
            item.TotalBytes = totalBytes;
        }
    }

    private static string CleanUrl(string rawUrl)
    {
        var trimmed = rawUrl.Trim();
        char[] trailingPunctuation = { '.', ',', ';', ':', '!', ')', ']', '}', '>' };
        return trimmed.TrimEnd(trailingPunctuation);
    }

    private static string ResolveFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);

            // 1. Check URL fragment for filename (common in FitGirl / DDL links e.g. #Bloodborne_--_fitgirl-repacks.site_--_.part1.rar)
            if (!string.IsNullOrWhiteSpace(uri.Fragment))
            {
                var fragment = Uri.UnescapeDataString(uri.Fragment.TrimStart('#', '/', ' '));
                if (!string.IsNullOrWhiteSpace(fragment) && (fragment.Contains('.') || fragment.Contains("part", StringComparison.OrdinalIgnoreCase)))
                {
                    return SanitizeFileName(fragment);
                }
            }

            // 2. Check last segment of URL path
            var lastSegment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                var unescaped = Uri.UnescapeDataString(lastSegment.Trim('/'));
                if (!string.IsNullOrWhiteSpace(unescaped))
                {
                    return SanitizeFileName(unescaped);
                }
            }
        }
        catch
        {
            // fallback
        }

        return "download_" + Guid.NewGuid().ToString("N")[..8];
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
