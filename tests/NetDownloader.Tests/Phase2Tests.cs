using System.Net;
using System.Net.Http.Headers;
using NetDownloader.Core.Models;
using NetDownloader.Core.Services;
using Xunit;

namespace NetDownloader.Tests;

public class Phase2Tests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task ProbeUrlAsync_WithFragmentInUrl_ShouldExtractFragmentFileName()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var url = "https://fuckingfast.co/w674u09fbhw0#Bloodborne_--_fitgirl-repacks.site_--_.part1.rar";
        var item = await grabber.ProbeUrlAsync(url);

        Assert.Equal(DownloadItemStatus.Offline, item.Status);
        Assert.Equal("Bloodborne_--_fitgirl-repacks.site_--_.part1.rar", item.FileName);
        Assert.Contains("Cloudflare", item.ErrorMessage);
        Assert.Equal("Bloodborne_--_fitgirl-repacks.site_--_", grabber.DerivePackageName(item.FileName));
    }

    [Fact]
    public void ExtractUrls_ShouldExtractValidUrlsFromMixedText()
    {
        var grabber = new LinkGrabberService();
        var raw = @"
            Check out these links:
            https://example.com/Ubuntu_Studio_Backup.part1.rar.
            and also https://example.com/Ubuntu_Studio_Backup.part2.rar!
            http://files.org/data.zip
            invalid-url://not-http
            https://server.com/archive.7z.001)
        ";

        var urls = grabber.ExtractUrls(raw);
        Assert.Equal(4, urls.Count);
        Assert.Contains("https://example.com/Ubuntu_Studio_Backup.part1.rar", urls);
        Assert.Contains("https://example.com/Ubuntu_Studio_Backup.part2.rar", urls);
        Assert.Contains("http://files.org/data.zip", urls);
        Assert.Contains("https://server.com/archive.7z.001", urls);
    }

    [Theory]
    [InlineData("Ubuntu_Studio_Backup.part1.rar", "Ubuntu_Studio_Backup")]
    [InlineData("Ubuntu_Studio_Backup.part02.rar", "Ubuntu_Studio_Backup")]
    [InlineData("Ubuntu_Studio_Backup.part3.rar", "Ubuntu_Studio_Backup")]
    [InlineData("Game_Assets_Archive.part1.rar", "Game_Assets_Archive")]
    [InlineData("archive.r00", "archive")]
    [InlineData("archive.r01", "archive")]
    [InlineData("MyArchive.7z.001", "MyArchive")]
    [InlineData("MultiPart.zip.002", "MultiPart")]
    [InlineData("SplitFile.001", "SplitFile")]
    [InlineData("SingleDownload.iso", "SingleDownload")]
    public void DerivePackageName_ShouldGroupCorrectly(string fileName, string expectedPackageName)
    {
        var grabber = new LinkGrabberService();
        var package = grabber.DerivePackageName(fileName);
        Assert.Equal(expectedPackageName, package);
    }

    [Fact]
    public async Task ProbeUrlAsync_WithHeadRequest_ShouldResolveMetadataAndRanges()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Head, req.Method);
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new ByteArrayContent(Array.Empty<byte>());
            resp.Content.Headers.ContentLength = 1048576; // 1 MB
            resp.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"Custom_File_Name.part1.rar\""
            };
            resp.Headers.AcceptRanges.Add("bytes");
            return resp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://cdn.example.com/download?id=12345");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal("Custom_File_Name.part1.rar", item.FileName);
        Assert.Equal(1048576, item.TotalBytes);
        Assert.True(item.SupportsRange);
    }

    [Fact]
    public async Task ProbeUrlAsync_WhenHeadFails_ShouldFallbackToRangeGet()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.NotNull(req.Headers.Range);

            var resp = new HttpResponseMessage(HttpStatusCode.PartialContent);
            resp.Content = new ByteArrayContent(new byte[] { 0x42 });
            resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 5242880); // 5 MB
            return resp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://cdn.example.com/file_part2.zip");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal("file_part2.zip", item.FileName);
        Assert.Equal(5242880, item.TotalBytes);
        Assert.True(item.SupportsRange);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldGroupUrlsIntoPackages()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new ByteArrayContent(Array.Empty<byte>());
            resp.Content.Headers.ContentLength = 2000;
            resp.Headers.AcceptRanges.Add("bytes");
            return resp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var raw = @"
            https://server.com/Ubuntu_Studio_Backup.part1.rar
            https://server.com/Ubuntu_Studio_Backup.part2.rar
            https://server.com/Another_Package.zip
        ";

        var packages = await grabber.AnalyzeAsync(raw, "C:\\Downloads");

        Assert.Equal(2, packages.Count);
        var ubuntuPkg = packages.FirstOrDefault(p => p.Name == "Ubuntu_Studio_Backup");
        Assert.NotNull(ubuntuPkg);
        Assert.Equal(2, ubuntuPkg.Items.Count);

        var anotherPkg = packages.FirstOrDefault(p => p.Name == "Another_Package");
        Assert.NotNull(anotherPkg);
        Assert.Single(anotherPkg.Items);
    }

    [Fact]
    public async Task ProbeUrlAsync_WithRawContentDispositionHeader_ShouldExtractFileName()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new ByteArrayContent(Array.Empty<byte>());
            resp.Content.Headers.ContentLength = 500000;
            resp.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=\"Bloodborne_--_fitgirl-repacks.site_--_.part1.rar\"");
            return resp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://dl.fuckingfast.co/dl/FlbPeRNeZqWGBXKTXSN5LOd8GAC7VQKJyPgEOTjjAH");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal("Bloodborne_--_fitgirl-repacks.site_--_.part1.rar", item.FileName);
    }

    [Fact]
    public async Task ProbeUrlAsync_WhenHeadOmitsContentDispositionOnBareToken_ShouldFallbackToGetRangeAndExtractFileName()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                // Head succeeds with 200 OK but omits Content-Disposition
                var headResp = new HttpResponseMessage(HttpStatusCode.OK);
                headResp.Content = new ByteArrayContent(Array.Empty<byte>());
                headResp.Content.Headers.ContentLength = 500000;
                return headResp;
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            var getResp = new HttpResponseMessage(HttpStatusCode.PartialContent);
            getResp.Content = new ByteArrayContent(new byte[] { 0x50 });
            getResp.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=Bloodborne_--_fitgirl-repacks.site_--_.part3.rar");
            return getResp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://dl.fuckingfast.co/dl/FlbPeRNeZqWGBXKTXSN5LOd8GAC7VQKJyPgEOTjjAH");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal("Bloodborne_--_fitgirl-repacks.site_--_.part3.rar", item.FileName);
    }

    [Fact]
    public async Task ProbeUrlAsync_WhenHeadOmitsContentLength_ShouldFallbackToGetRangeAndResolveSize()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                // HEAD succeeds with 200 OK but omits Content-Length
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            var getResp = new HttpResponseMessage(HttpStatusCode.PartialContent);
            getResp.Content = new ByteArrayContent(new byte[] { 0x01 });
            getResp.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 524288000);
            return getResp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://dl.fuckingfast.co/dl/token123#God_of_War_III_Remastered_--_fitgirl-repacks.site_--_.part1.rar");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal("God_of_War_III_Remastered_--_fitgirl-repacks.site_--_.part1.rar", item.FileName);
        Assert.Equal(524288000, item.TotalBytes);
        Assert.True(item.SupportsRange);
    }

    [Fact]
    public async Task ProbeUrlAsync_WithRawContentRangeHeader_ShouldExtractTotalBytes()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var getResp = new HttpResponseMessage(HttpStatusCode.PartialContent);
            getResp.Content = new ByteArrayContent(new byte[] { 0x01 });
            getResp.Content.Headers.TryAddWithoutValidation("Content-Range", "bytes 0-0/1073741824");
            return getResp;
        });

        var httpClient = new HttpClient(handler);
        var grabber = new LinkGrabberService(httpClient);

        var item = await grabber.ProbeUrlAsync("https://dl.fuckingfast.co/dl/token123#archive.part2.rar");

        Assert.Equal(DownloadItemStatus.Online, item.Status);
        Assert.Equal(1073741824, item.TotalBytes);
        Assert.True(item.SupportsRange);
    }
}
