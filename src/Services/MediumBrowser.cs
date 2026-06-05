using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MediumMetrics.Services;

/// <summary>Result of a browser-side fetch: HTTP status and raw response body.</summary>
public readonly record struct FetchResult(int Status, string Body);

/// <summary>
/// A hidden, off-screen WebView2 that issues same-origin <c>fetch()</c> calls from
/// a medium.com page. This is required because Medium is behind Cloudflare's
/// managed challenge: a plain HttpClient is blocked (403), but the real browser
/// has already passed the challenge during sign-in and carries the clearance
/// cookie. Shares the login's WebView2 profile so cookies/clearance are reused.
///
/// All members must be used on the UI thread (WebView2 is dispatcher-bound).
/// </summary>
public sealed class MediumBrowser : IDisposable
{
    private const string OriginPage = "https://medium.com/me/stats";

    private readonly string _userDataFolder;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FetchResult>> _pending = new();
    private Window? _host;
    private WebView2? _web;
    private bool _ready;
    private int _seq;

    public MediumBrowser(string userDataFolder) => _userDataFolder = userDataFolder;

    private async Task EnsureReadyAsync()
    {
        if (_ready) return;
        await _initGate.WaitAsync();
        try
        {
            if (_ready) return;
            Log.Info("MediumBrowser: initializing hidden WebView2.");
            _web = new WebView2();
            _host = new Window
            {
                Width = 700,
                Height = 500,
                Left = -32000,   // off-screen so it renders (no background throttling) but isn't seen
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                Title = "MediumBrowser",
                Content = _web,
            };
            _host.Show();

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;

            // Install a network interceptor (fetch + XHR) so we can capture the
            // GraphQL traffic the stats SPA makes. Runs before any page script.
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InterceptorScript);

            // Land on a real medium.com page so fetches are same-origin and clearance applies.
            await NavigateAsync(OriginPage);
            _ready = true;
            Log.Info("MediumBrowser: ready.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    private Task<bool> NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _web!.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult(e.IsSuccess);
        }
        _web!.CoreWebView2.NavigationCompleted += Handler;
        _web.CoreWebView2.Navigate(url);
        return tcs.Task;
    }

    /// <summary>GET <paramref name="url"/> from the medium.com page context.</summary>
    public Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
        => RequestAsync(url, "GET", null, ct);

    /// <summary>POST a JSON body to <paramref name="url"/> from the page context (used for GraphQL).</summary>
    public Task<FetchResult> PostJsonAsync(string url, string jsonBody, CancellationToken ct = default)
        => RequestAsync(url, "POST", jsonBody, ct);

    /// <summary>
    /// Runs an in-page fetch and marshals the result back via postMessage (so the
    /// async fetch promise is actually awaited). Same-origin, so cookies and
    /// Cloudflare clearance are sent automatically.
    /// </summary>
    private async Task<FetchResult> RequestAsync(string url, string method, string? body, CancellationToken ct)
    {
        await EnsureReadyAsync();

        var id = $"r{Interlocked.Increment(ref _seq)}";
        var tcs = new TaskCompletionSource<FetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await _web!.CoreWebView2.ExecuteScriptAsync(BuildRequestScript(id, url, method, body));

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), ct));
            if (done != tcs.Task)
                throw new MediumStatsException($"Timed out fetching {url}.");
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Navigates to the stats page and returns the GraphQL traffic it makes, as a
    /// JSON array of {url, request, response}. Used to discover/verify the queries.
    /// </summary>
    public async Task<string> CaptureStatsAsync(CancellationToken ct = default)
    {
        await EnsureReadyAsync();
        await _web!.CoreWebView2.ExecuteScriptAsync("window.__mm_captures = []");
        await NavigateAsync("https://medium.com/me/stats");
        // Let the SPA render and fire its lazy GraphQL requests.
        await Task.Delay(TimeSpan.FromSeconds(8), ct);
        var json = await _web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__mm_captures || [])");
        return JsonSerializer.Deserialize<string>(json) ?? "[]";
    }

    // Captures fetch + XHR traffic whose URL contains "graphql" into window.__mm_captures.
    private const string InterceptorScript = @"(function(){
  if (window.__mm_installed) return; window.__mm_installed = true;
  window.__mm_captures = [];
  function rec(url, request, response){
    try { if (url && url.indexOf('graphql') !== -1)
      window.__mm_captures.push({ url: url, request: request, response: response }); } catch (e) {}
  }
  var of = window.fetch;
  window.fetch = function(input, init){
    var url = (typeof input === 'string') ? input : (input && input.url);
    var req = (init && init.body) ? init.body : null;
    var pr = of.apply(this, arguments);
    try { pr.then(function(r){ r.clone().text().then(function(t){ rec(url, req, t); }).catch(function(){}); }).catch(function(){}); } catch (e) {}
    return pr;
  };
  var ox = window.XMLHttpRequest.prototype.open, os = window.XMLHttpRequest.prototype.send;
  window.XMLHttpRequest.prototype.open = function(m, u){ this.__mm_url = u; return ox.apply(this, arguments); };
  window.XMLHttpRequest.prototype.send = function(b){
    var self = this;
    this.addEventListener('load', function(){ try { rec(self.__mm_url, b, self.responseText); } catch (e) {} });
    return os.apply(this, arguments);
  };
})();";

    private static string BuildRequestScript(string id, string url, string method, string? body)
    {
        var jid = JsonSerializer.Serialize(id);
        var jurl = JsonSerializer.Serialize(url);
        var jmethod = JsonSerializer.Serialize(method);
        var jbody = body is null ? "undefined" : JsonSerializer.Serialize(body);
        // Posts {id, ok, status, body} back to the host. Same-origin fetch carries
        // cookies + Cloudflare clearance automatically (credentials: 'include').
        return $@"(function(){{
  var opts = {{ method: {jmethod}, credentials: 'include', headers: {{ 'accept': 'application/json' }} }};
  var b = {jbody};
  if (b !== undefined) {{ opts.body = b; opts.headers['content-type'] = 'application/json'; }}
  fetch({jurl}, opts)
    .then(function(r){{ return r.text().then(function(t){{ return {{ s: r.status, b: t }}; }}); }})
    .then(function(o){{ window.chrome.webview.postMessage(JSON.stringify({{ id: {jid}, ok: true, status: o.s, body: o.b }})); }})
    .catch(function(e){{ window.chrome.webview.postMessage(JSON.stringify({{ id: {jid}, ok: false, error: String(e) }})); }});
}})();";
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch { return; }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return;
            var id = idEl.GetString();
            if (id is null || !_pending.TryGetValue(id, out var tcs)) return;

            if (root.GetProperty("ok").GetBoolean())
                tcs.TrySetResult(new FetchResult(root.GetProperty("status").GetInt32(),
                                                 root.GetProperty("body").GetString() ?? ""));
            else
                tcs.TrySetException(new MediumStatsException(
                    "In-page fetch failed: " + root.GetProperty("error").GetString()));
        }
        catch
        {
            // Not one of our messages — ignore.
        }
    }

    public void Dispose()
    {
        try
        {
            if (_web?.CoreWebView2 is not null)
                _web.CoreWebView2.WebMessageReceived -= OnWebMessage;
            _web?.Dispose();
            _host?.Close();
        }
        catch
        {
            // Best-effort teardown.
        }
    }
}
