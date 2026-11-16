using System.Net;
using System.Text;
using System.Text.Json;
using NetDownloader.Core.Services;
using Xunit;

namespace NetDownloader.Tests;

public class ClickAndLoadTests
{
    [Fact]
    public async Task ClickAndLoadServer_ShouldHandleDetectionAndAddEndpoints()
    {
        // Use a test port to avoid conflict with standard 9666 in tests
        int testPort = 19666;
        await using var server = new ClickAndLoadServer(testPort);
        await server.StartAsync();

        Assert.True(server.IsRunning);

        using var client = new HttpClient();

        // 1. Detection endpoint
        var jdCheckResp = await client.GetAsync($"http://127.0.0.1:{testPort}/jdcheck.js");
        Assert.Equal(HttpStatusCode.OK, jdCheckResp.StatusCode);
        var jsContent = await jdCheckResp.Content.ReadAsStringAsync();
        Assert.Contains("jdownloader=true", jsContent);

        // 2. Add via form POST
        ClickAndLoadLinksEventArgs? receivedFormArgs = null;
        server.LinksReceived += (_, args) => receivedFormArgs = args;

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("package", "FormPackage"),
            new KeyValuePair<string, string>("urls", "https://cdn.example.com/archive.part1.rar\nhttps://cdn.example.com/archive.part2.rar"),
            new KeyValuePair<string, string>("passwords", "secret123")
        });

        var addResp = await client.PostAsync($"http://127.0.0.1:{testPort}/flash/add", formData);
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);

        Assert.NotNull(receivedFormArgs);
        Assert.Equal("FormPackage", receivedFormArgs.PackageName);
        Assert.Equal(2, receivedFormArgs.Urls.Count);
        Assert.Equal("secret123", receivedFormArgs.Password);

        // 3. Add via JSON POST
        ClickAndLoadLinksEventArgs? receivedJsonArgs = null;
        server.LinksReceived += (_, args) => receivedJsonArgs = args;

        var json = JsonSerializer.Serialize(new
        {
            packageName = "JsonPackage",
            urls = new[] { "https://cdn.example.com/game.part1.rar", "https://cdn.example.com/game.part2.rar" }
        });

        var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
        var jsonResp = await client.PostAsync($"http://127.0.0.1:{testPort}/api/add", jsonContent);
        Assert.Equal(HttpStatusCode.OK, jsonResp.StatusCode);

        Assert.NotNull(receivedJsonArgs);
        Assert.Equal("JsonPackage", receivedJsonArgs.PackageName);
        Assert.Equal(2, receivedJsonArgs.Urls.Count);

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }
}
