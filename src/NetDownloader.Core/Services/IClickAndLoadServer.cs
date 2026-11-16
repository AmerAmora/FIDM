using NetDownloader.Core.Models;

namespace NetDownloader.Core.Services;

public class ClickAndLoadLinksEventArgs : EventArgs
{
    public string? PackageName { get; set; }
    public List<string> Urls { get; set; } = new();
    public string? Password { get; set; }
    public string? DestinationDirectory { get; set; }
}

public interface IClickAndLoadServer : IAsyncDisposable
{
    int Port { get; }
    bool IsRunning { get; }

    event EventHandler<ClickAndLoadLinksEventArgs>? LinksReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
