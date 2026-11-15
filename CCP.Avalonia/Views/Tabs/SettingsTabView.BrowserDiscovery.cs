using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    // WPF BrowserAutoDiscovery.cs:84-220: description references are scoped to the current page.
    private static readonly Regex BrowserCcpRef = new(@"ccp:(?<url>https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string BrowserDescriptionScript = """
        (() => { try {
          const sels = ['meta[name=description]', 'meta[property="og:description"]', '.video-description',
                        '#video-description', 'div.description', 'div[class*=description]'];
          return sels.map(s => document.querySelector(s)).filter(Boolean)
            .map(e => e.tagName === 'META' ? e.getAttribute('content') : e.innerText).filter(Boolean).join('\n');
        } catch (_) { return ''; } })();
        """;
    private readonly EnhancementHostService _discoveryHost = new();
    private EnhancementFetcher? _discoveryFetcher;
    private WebHostVideoTimeSource? _discoverySource;
    private CancellationTokenSource? _discoveryCancellation;
    private string? _browserWebcamPromptUrl;

    // Instance seams allow headless tests to drive the same navigation path without a network or camera.
    internal Func<string, CancellationToken, Task<Enhancement?>>? DiscoveryFetch;
    internal Func<CancellationToken, Task>? DiscoveryDelay;
    internal Func<bool>? DiscoveryTrackerRunning;
    internal Func<Task<bool>>? DiscoveryPrompt;
    internal Action? DiscoveryEnableWebcam;

    private void HookBrowserDiscovery()
    {
        BrowserWebHost.NavigationStarting += StopBrowserDiscovery;
        BrowserWebHost.NavigationFinished += (url, success) => _ = DiscoverBrowserAsync(url, success);
        DetachedFromVisualTree += (_, _) => StopBrowserDiscovery();
    }

    internal static bool IsDiscoveryPage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && UrlSafety.HostMatches(uri, DeeperConfig.MetadataScrapeAllowlist)
        && uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase);

    internal static string? FindBrowserCcpRef(string description)
    {
        var match = BrowserCcpRef.Match(description);
        return match.Success ? match.Groups["url"].Value.TrimEnd(',', '.', ')', ']') : null;
    }

    internal void StopBrowserDiscovery()
    {
        _discoveryCancellation?.Cancel();
        _discoveryCancellation?.Dispose();
        _discoveryCancellation = null;
        _discoveryHost.Unload();
        _discoverySource?.Dispose();
        _discoverySource = null;
        DeeperBrowserBadge.IsVisible = false;
        DeeperBrowserBadge.Tag = null;
        _browserWebcamPromptUrl = null;
    }

    internal async Task DiscoverBrowserAsync(Uri url, bool success)
    {
        StopBrowserDiscovery();
        if (!success || !IsDiscoveryPage(url.ToString())) return;
        var cts = _discoveryCancellation = new CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            await (DiscoveryDelay?.Invoke(ct) ?? Task.Delay(700, ct));
            var raw = await BrowserWebHost.InvokeScriptAsync(BrowserDescriptionScript);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(raw) || raw == "null") return;
            string description;
            try { description = JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch (JsonException) { description = raw; }
            var reference = FindBrowserCcpRef(description);
            if (reference == null) return;
            var enhancement = DiscoveryFetch != null ? await DiscoveryFetch(reference, ct)
                : await (_discoveryFetcher ??= new EnhancementFetcher()).FetchAsync(reference, ct);
            ct.ThrowIfCancellationRequested();
            if (enhancement == null || !BrowserEnhancementDispatcher.Supports(enhancement)) return;
            StopBrowserEnhancement();
            if (!_discoveryHost.LoadFromMemory(enhancement, url.ToString())) return;
            var source = _discoverySource = new WebHostVideoTimeSource(BrowserWebHost);
            var webcam = new BrowserEnhancementWebcam();
            if (!_discoveryHost.Bind(source, source.Attach, source.Detach, new BrowserEnhancementDispatcher(), webcam))
            {
                webcam.Dispose();
                StopBrowserDiscovery();
                return;
            }
            var name = string.IsNullOrEmpty(enhancement.Metadata?.Name) ? "(untitled)" : enhancement.Metadata.Name;
            TxtDeeperBrowserBadge.Text = name;
            DeeperBrowserBadge.Tag = $"{name}\n{url}";
            DeeperBrowserBadge.IsVisible = true;
            await PromptBrowserWebcamAsync(url.ToString(), enhancement, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug(ex, "Browser discovery failed"); }
    }

    internal async Task PromptBrowserWebcamAsync(string url, Enhancement enhancement, CancellationToken ct)
    {
        if (!EnhancementCapabilities.NeedsWebcam(enhancement)
            || (DiscoveryTrackerRunning?.Invoke() ?? Platform.WebcamTracker.Instance.IsRunning)
            || string.Equals(_browserWebcamPromptUrl, url, StringComparison.OrdinalIgnoreCase)) return;
        if (DiscoveryPrompt == null && TopLevel.GetTopLevel(this) is not Window) return;
        _browserWebcamPromptUrl = url;
        var yes = DiscoveryPrompt != null ? await DiscoveryPrompt()
            : await Dialogs.MessageDialog.ConfirmAsync((Window)TopLevel.GetTopLevel(this)!,
                Loc.Get("browser_webcam_prompt_title"), Loc.Get("browser_webcam_prompt_body"));
        if (!yes || ct.IsCancellationRequested) return;
        if (DiscoveryEnableWebcam != null) DiscoveryEnableWebcam();
        else BtnWebcamTracking_Click(this, new RoutedEventArgs());
    }
}
