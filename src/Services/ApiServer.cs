using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MediumMetrics.Services;

/// <summary>
/// Hosts <see cref="ApiRequestHandler"/> over a loopback-only HttpListener. Binds to
/// localhost / 127.0.0.1 so the only <i>remote</i> ingress is a tunnel the user starts
/// (OPENAI_CUSTOM_GPT.md §10); same-PC processes can call it directly. Runs a
/// background accept loop for the app's lifetime and is strictly read-only.
/// </summary>
public sealed class ApiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ApiRequestHandler _handler;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public ApiServer(ApiRequestHandler handler, int port)
    {
        _handler = handler;
        _port = port;
    }

    public bool IsRunning => _listener?.IsListening == true;

    public void Start()
    {
        if (IsRunning) return;

        _listener = CreateListener(bothLoopback: true);
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // The 127.0.0.1 prefix can require a urlacl on some machines; the localhost
            // prefix is always allowed for a non-admin process. Fall back to localhost-only.
            _listener = CreateListener(bothLoopback: false);
            _listener.Start();
        }

        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_listener, _cts.Token);
        Log.Info($"Local API listening on http://localhost:{_port}/v1 (loopback only).");
    }

    private HttpListener CreateListener(bool bothLoopback)
    {
        var l = new HttpListener();
        l.Prefixes.Add($"http://localhost:{_port}/");
        if (bothLoopback) l.Prefixes.Add($"http://127.0.0.1:{_port}/");
        return l;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) { break; }   // listener stopped
            catch (ObjectDisposedException) { break; } // listener closed
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                Log.Error("API accept loop error", ex);
                continue;
            }

            _ = Task.Run(() => HandleContextSafe(ctx));
        }
    }

    private void HandleContextSafe(HttpListenerContext ctx)
    {
        try
        {
            var result = _handler.Handle(ToApiRequest(ctx.Request));
            WriteResult(ctx.Response, result);
        }
        catch (Exception ex)
        {
            Log.Error("API request failed", ex);
            try
            {
                WriteResult(ctx.Response, new ApiResult(500,
                    new ErrorDto(new ErrorBody("internal_error", "Unexpected server error."))));
            }
            catch { /* response stream already gone */ }
        }
    }

    private static ApiRequest ToApiRequest(HttpListenerRequest request)
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in request.QueryString)
            if (key is not null) query[key] = request.QueryString[key];

        return new ApiRequest(
            request.HttpMethod,
            request.Url?.AbsolutePath ?? "/",
            query,
            request.Headers["Authorization"]);
    }

    private static void WriteResult(HttpListenerResponse response, ApiResult result)
    {
        response.StatusCode = result.Status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";

        var bytes = result.Body is null
            ? Array.Empty<byte>()
            : JsonSerializer.SerializeToUtf8Bytes(result.Body, JsonOpts);
        response.ContentLength64 = bytes.Length;

        using var output = response.OutputStream;
        output.Write(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch (Exception ex) { Log.Error("Error stopping API", ex); }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose() => Stop();
}
