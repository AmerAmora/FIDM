using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace NetDownloader.Core.Services;

public class ClickAndLoadServer : IClickAndLoadServer
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isDisposed;

    public int Port { get; private set; } = 9666;
    public bool IsRunning => _listener?.IsListening ?? false;

    public event EventHandler<ClickAndLoadLinksEventArgs>? LinksReceived;

    public ClickAndLoadServer() : this(9666)
    {
    }

    public ClickAndLoadServer(int preferredPort)
    {
        Port = preferredPort;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();

        int[] portsToTry = { Port, 9667, 9668, 9669 };
        foreach (var port in portsToTry.Distinct())
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Start();
                _listener = l;
                Port = port;
                break;
            }
            catch
            {
                // try next port
            }
        }

        if (_listener == null || !_listener.IsListening)
        {
            return Task.CompletedTask;
        }

        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue listener loop
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Apply CORS headers to all responses
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");

        if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath.ToLowerInvariant() ?? "";

        try
        {
            // 1. Detection endpoints
            if (path is "/" or "/flash" or "/flash/" or "/flash/version" or "/jdcheck.js")
            {
                var js = "var jdownloader=true;\nvar netdownloader=true;";
                byte[] buffer = Encoding.UTF8.GetBytes(js);
                response.ContentType = "application/javascript";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
                response.Close();
                return;
            }

            // 2. Add Links endpoints: /flash/add or /api/add
            if (path is "/flash/add" or "/api/add" or "/flash/addcrypted2")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync();

                var eventArgs = new ClickAndLoadLinksEventArgs();

                if (request.ContentType != null && request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    ParseJsonPayload(body, eventArgs);
                }
                else
                {
                    ParseFormPayload(body, eventArgs);
                }

                if (eventArgs.Urls.Count > 0)
                {
                    LinksReceived?.Invoke(this, eventArgs);
                }

                var resultJson = JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Received {eventArgs.Urls.Count} links",
                    count = eventArgs.Urls.Count
                });

                byte[] buffer = Encoding.UTF8.GetBytes(resultJson);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
                response.Close();
                return;
            }

            // Unknown endpoint
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errorBuffer = Encoding.UTF8.GetBytes(ex.Message);
                await response.OutputStream.WriteAsync(errorBuffer);
                response.Close();
            }
            catch { }
        }
    }

    private static void ParseFormPayload(string body, ClickAndLoadLinksEventArgs eventArgs)
    {
        var query = HttpUtility.ParseQueryString(body);

        var rawUrls = query["urls"] ?? query["links"] ?? "";
        if (!string.IsNullOrWhiteSpace(rawUrls))
        {
            var split = rawUrls.Split(new[] { "\r\n", "\r", "\n", " ", "," }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in split)
            {
                var trimmed = s.Trim();
                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    eventArgs.Urls.Add(trimmed);
                }
            }
        }

        eventArgs.PackageName = query["package"] ?? query["packageName"] ?? query["name"];
        eventArgs.Password = query["passwords"] ?? query["password"];
        eventArgs.DestinationDirectory = query["dir"] ?? query["destination"];
    }

    private static void ParseJsonPayload(string body, ClickAndLoadLinksEventArgs eventArgs)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("packageName", out var pkgProp))
                eventArgs.PackageName = pkgProp.GetString();
            else if (root.TryGetProperty("package", out var pProp))
                eventArgs.PackageName = pProp.GetString();

            if (root.TryGetProperty("password", out var pwProp))
                eventArgs.Password = pwProp.GetString();

            if (root.TryGetProperty("destinationDirectory", out var dirProp))
                eventArgs.DestinationDirectory = dirProp.GetString();

            if (root.TryGetProperty("urls", out var urlsProp))
            {
                if (urlsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in urlsProp.EnumerateArray())
                    {
                        var url = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            eventArgs.Urls.Add(url.Trim());
                    }
                }
                else if (urlsProp.ValueKind == JsonValueKind.String)
                {
                    var raw = urlsProp.GetString() ?? "";
                    var split = raw.Split(new[] { "\r\n", "\r", "\n", " ", "," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in split)
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                            eventArgs.Urls.Add(s.Trim());
                    }
                }
            }
        }
        catch { }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }

        if (_listenTask != null)
        {
            try { await _listenTask; } catch { }
        }

        _listener = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await StopAsync();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
