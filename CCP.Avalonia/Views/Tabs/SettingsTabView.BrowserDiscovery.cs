using System;
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
    private CancellationTokenSource? _browserDiscoveryCancellation;
    private bool _browserDiscoveryPanicStopped;

    internal void StopBrowserDiscoveryForPanic()
    {
        _browserDiscoveryPanicStopped = true;
        StopBrowserEnhancement();
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

    // WPF BrowserAutoDiscovery.cs:85-145. The script returns JSON on both browser adapters.
    internal async Task DiscoverBrowserEnhancementAsync(Uri page)
    {
        if (_browserDiscoveryPanicStopped) return;
        CancelBrowserDiscovery();
        var cancellation = _browserDiscoveryCancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        try
        {
            if (BrowserDiscoveryReference(page.ToString(), "ccp:https://example.com") == null) return;
            await (BrowserDiscoveryDelay?.Invoke(ct) ?? Task.Delay(700, ct));
            ct.ThrowIfCancellationRequested();
            const string script = "(function(){var s=['meta[name=description]','meta[property=\"og:description\"]','.video-description','#video-description','div.description','div[class*=description]'],o=[];for(var i=0;i<s.length;i++){var e=document.querySelector(s[i]);if(e){var v=e.tagName==='META'?e.getAttribute('content'):e.innerText;if(v)o.push(v);}}return JSON.stringify(o.join('\\n'));})()";
            var raw = await BrowserWebHost.InvokeScriptAsync(script);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(raw)) return;
            string? description = JsonSerializer.Deserialize<string>(raw);
            // WebView2 wraps the JSON string returned by the script once more.
            if (description != null && description.StartsWith('"')) description = JsonSerializer.Deserialize<string>(description);
            var reference = BrowserDiscoveryReference(page.ToString(), description);
            if (reference == null) return;
            var enhancement = await (BrowserDiscoveryFetch?.Invoke(reference, ct) ?? _browserDiscoveryFetcher.FetchAsync(reference, ct));
            ct.ThrowIfCancellationRequested();
            if (enhancement == null || !BrowserEnhancementDispatcher.Supports(enhancement)) return;
            _browserEnhanceHost.Unload();
            _browserEnhanceSource?.Dispose();
            _browserEnhanceSource = null;
            if (!_browserEnhanceHost.LoadFromMemory(enhancement, page.ToString())) return;
            var source = _browserEnhanceSource = new WebHostVideoTimeSource(BrowserWebHost);
            var webcam = new BrowserEnhancementWebcam();
            if (!_browserEnhanceHost.Bind(source, source.Attach, source.Detach, new BrowserEnhancementDispatcher(), webcam))
            {
                webcam.Dispose();
                StopBrowserEnhancement();
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
