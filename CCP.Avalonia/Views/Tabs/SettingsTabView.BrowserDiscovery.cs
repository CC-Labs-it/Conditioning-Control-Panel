using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private readonly EnhancementFetcher _browserDiscoveryFetcher = new();
    // WPF binds discovery on App.DeeperHost (App.xaml.cs:3520), apart from the library bridge's own host
    // (BrowserEnhancementBridge.cs:21), so a library re-evaluation never touches a discovered enhancement.
    private readonly EnhancementHostService _browserDiscoveryHost = new();
    private WebHostVideoTimeSource? _browserDiscoverySource;
    private CancellationTokenSource? _browserDiscoveryCancellation;
    private bool _browserDiscoveryPanicStopped;
    internal EnhancementHostService BrowserDiscoveryHost => _browserDiscoveryHost;

    internal void StopBrowserDiscoveryForPanic()
    {
        _browserDiscoveryPanicStopped = true;
        StopBrowserDiscovery();
        StopBrowserEnhancement();
    }

    /// <summary>Navigation start, panic, detach and engine stop: cancel and unbind the discovered enhancement.</summary>
    internal void StopBrowserDiscovery()
    {
        CancelBrowserDiscovery();
        UnloadBrowserDiscovery();
    }

    private void UnloadBrowserDiscovery()
    {
        _browserDiscoveryHost.Unload();
        _browserDiscoverySource?.Dispose();
        _browserDiscoverySource = null;
    }
    internal Func<string, CancellationToken, Task<Enhancement?>>? BrowserDiscoveryFetch;
    internal Func<CancellationToken, Task>? BrowserDiscoveryDelay;

    private void CancelBrowserDiscovery()
    {
        _browserDiscoveryCancellation?.Cancel();
        _browserDiscoveryCancellation?.Dispose();
        _browserDiscoveryCancellation = null;
        DeeperBrowserBadge.IsVisible = false;
        DeeperBrowserBadge.Tag = null;
    }

    internal static string? BrowserDiscoveryReference(string? page, string? description)
    {
        if (!Uri.TryCreate(page, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !UrlSafety.HostMatches(uri, DeeperConfig.MetadataScrapeAllowlist) ||
            !uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase)) return null;
        var match = Regex.Match(description ?? "", @"ccp:(?<url>https?://\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["url"].Value.TrimEnd(',', '.', ')', ']') : null;
    }

    /// <summary>
    /// The discovery script returns <c>JSON.stringify([text])</c>. WebKitGTK hands back that JSON as-is;
    /// WebView2 wraps it in one more JSON string literal. Only a raw result starting with <c>"</c> is
    /// unwrapped, so the page text itself (which may start with a quote) is decoded exactly once.
    /// </summary>
    internal static string? ParseBrowserDiscoveryResult(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var json = raw.TrimStart();
        if (json.StartsWith('"')) json = JsonSerializer.Deserialize<string>(json);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<string[]>(json)?.FirstOrDefault();
    }

    // WPF BrowserAutoDiscovery.cs:85-145.
    internal async Task DiscoverBrowserEnhancementAsync(Uri page)
    {
        if (_browserDiscoveryPanicStopped) return;
        StopBrowserDiscovery();
        var cancellation = _browserDiscoveryCancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        try
        {
            if (BrowserDiscoveryReference(page.ToString(), "ccp:https://example.com") == null) return;
            await (BrowserDiscoveryDelay?.Invoke(ct) ?? Task.Delay(700, ct));
            ct.ThrowIfCancellationRequested();
            const string script = "(function(){var s=['meta[name=description]','meta[property=\"og:description\"]','.video-description','#video-description','div.description','div[class*=description]'],o=[];for(var i=0;i<s.length;i++){var e=document.querySelector(s[i]);if(e){var v=e.tagName==='META'?e.getAttribute('content'):e.innerText;if(v)o.push(v);}}return JSON.stringify([o.join('\\n')]);})()";
            var raw = await BrowserWebHost.InvokeScriptAsync(script);
            ct.ThrowIfCancellationRequested();
            var description = ParseBrowserDiscoveryResult(raw);
            var reference = BrowserDiscoveryReference(page.ToString(), description);
            if (reference == null) return;
            var enhancement = await (BrowserDiscoveryFetch?.Invoke(reference, ct) ?? _browserDiscoveryFetcher.FetchAsync(reference, ct));
            ct.ThrowIfCancellationRequested();
            if (enhancement == null || !BrowserEnhancementDispatcher.Supports(enhancement)) return;
            UnloadBrowserDiscovery();
            if (!_browserDiscoveryHost.LoadFromMemory(enhancement, page.ToString())) return;
            var source = _browserDiscoverySource = new WebHostVideoTimeSource(BrowserWebHost);
            var webcam = new BrowserEnhancementWebcam();
            if (!_browserDiscoveryHost.Bind(source, source.Attach, source.Detach, new BrowserEnhancementDispatcher(), webcam))
            {
                webcam.Dispose();
                StopBrowserDiscovery();
                return;
            }
            var name = string.IsNullOrEmpty(enhancement.Metadata?.Name) ? "(untitled)" : enhancement.Metadata.Name;
            TxtDeeperBrowserBadge.Text = name;
            DeeperBrowserBadge.Tag = $"{name}\n{page}";
            DeeperBrowserBadge.IsVisible = true;
            if (EnhancementCapabilities.NeedsWebcam(enhancement) && !WebcamTracker.Instance.IsRunning &&
                !WebcamTracker.Instance.IsStarting && TopLevel.GetTopLevel(this) is Window owner &&
                await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("browser_webcam_prompt_title"), Loc.Get("browser_webcam_prompt_body")) &&
                !ct.IsCancellationRequested)
                BtnWebcamTracking_Click(this, new RoutedEventArgs());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("Browser discovery failed: {Error}", ex.Message); }
    }
}
