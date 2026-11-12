using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF host: ConditioningControlPanel/Services/Remix/JackpotRemixBuilder.cs:142, 240.
internal sealed partial class JackpotRemixBuilder : IDisposable
{
    private readonly string _webRoot, _output;
    private readonly Func<string> _assetsRoot;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly Func<string, CancellationToken, Task<string?>>? _exchange;
    internal sealed class FailureState { internal int Count; }
    private readonly FailureState _failures;
    internal bool Disabled => Volatile.Read(ref _failures.Count) >= 2;

    internal JackpotRemixBuilder(string webRoot, Func<string> assetsRoot, string output, TimeSpan? timeout = null,
        Func<string, CancellationToken, Task<string?>>? exchange = null, FailureState? failures = null)
    {
        _webRoot = webRoot; _assetsRoot = assetsRoot; _output = output;
        _timeout = timeout ?? TimeSpan.FromSeconds(120); _exchange = exchange; _failures = failures ?? new FailureState();
    }

    internal async Task<string?> BuildAsync(IReadOnlyList<string> paths, int seed, CancellationToken ct)
    {
        if (_closing.IsCancellationRequested || Disabled) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _closing.Token);
        deadline.CancelAfter(_timeout);
        var held = false;
        try
        {
            await _one.WaitAsync(deadline.Token).ConfigureAwait(false); held = true;
            if (Disabled || _closing.IsCancellationRequested) return null;
            var assetsRoot = _assetsRoot();
            var sources = Sources(assetsRoot, paths);
            if (sources.Count == 0) return null;
            using var server = new WebAssetServer(_webRoot) { AssetsRoot = () => assetsRoot };
            var media = sources.Select(p => new { url = server.AssetUrl(Path.GetRelativePath(assetsRoot, p)), name = Path.GetFileName(p) });
            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { type = "build", id, media, seed, seconds = 3, scale = 1 });
            var reply = _exchange != null ? await _exchange(request, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false)
                : await RunPageAsync(server, request, id, deadline.Token).ConfigureAwait(false);
            if (reply == null) { Interlocked.Increment(ref _failures.Count); return null; }
            using var json = JsonDocument.Parse(reply);
            var root = json.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "built" ||
                !root.TryGetProperty("id", out var gotId) || gotId.GetString() != id ||
                !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("base64", out var data)) return null;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "0" : "0";
            return WriteOutput(_output, code, data.GetString() ?? "", deadline.Token);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { return null; }
        finally { if (held) _one.Release(); }
    }

    private static async Task<string?> RunPageAsync(WebAssetServer server, string request, string id, CancellationToken ct)
    {
        Window? window = null;
        WebHost? web = null;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                web = new WebHost();
                if (!web.HasEngine) { ready.TrySetResult(false); return; }
                var page = new Uri(server.Url("remix/jackpot.html"));
                web.AllowNavigation = u => u.IsAbsoluteUri && u.Scheme == page.Scheme && u.Authority == page.Authority && u.AbsolutePath == page.AbsolutePath;
                web.WebMessage += message =>
                {
                    try
                    {
                        using var parsed = JsonDocument.Parse(message);
                        var obj = parsed.RootElement;
                        if (!obj.TryGetProperty("type", out var t)) return;
                        if (t.GetString() == "ready") ready.TrySetResult(true);
                        if (t.GetString() == "built" && obj.TryGetProperty("id", out var i) && i.GetString() == id) result.TrySetResult(message);
                    }
                    catch (Exception) { }
                };
                web.BrowserProcessFailed += () => { ready.TrySetResult(false); result.TrySetResult(null); };
                web.NavigationFinished += (_, success) => { if (!success) ready.TrySetResult(false); };
                window = new Window { Width = 2, Height = 2, Position = new PixelPoint(-32000, -32000),
                    ShowInTaskbar = false, ShowActivated = false, CanResize = false, SystemDecorations = WindowDecorations.None, Content = web };
                window.Closed += (_, _) => { ready.TrySetResult(false); result.TrySetResult(null); };
                window.Show();
                web.Navigate(page);
            });
            if (!await ready.Task.WaitAsync(ct).ConfigureAwait(false)) return null;
            await Dispatcher.UIThread.InvokeAsync(() => _ = web!.InvokeScriptAsync("window.__ccpRemixPush(" + request + ");"));
            return await result.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
        finally
        {
            if (window != null)
            {
                // Closing the native view terminates its workers even if the page did not answer cancel.
                await Dispatcher.UIThread.InvokeAsync(() => window.Close());
            }
        }
    }

    public void Dispose() => _closing.Cancel();
}
