using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    private static readonly List<WeakReference<WebHost>> ScrolllerHosts = new();
    private bool _scrolllerNavigation;
    private int _scrolllerStopVersion;
    private Uri? _scrolllerOriginal, _scrolllerLocal;

    private void TrackScrolllerHost()
    {
        ScrolllerHosts.RemoveAll(w => !w.TryGetTarget(out _));
        ScrolllerHosts.Add(new(this));
    }

    internal bool GateScrolllerNavigation(Uri uri)
    {
        if (!ScrolllerNavigationGate.AllowsNavigation(uri)) return false;
        _scrolllerNavigation = ScrolllerNavigationGate.IsMedia(uri);
        if (!_scrolllerNavigation) return true;
        // The engine callback cancels raw media navigations and replaces them with the local URL.
        return true;
    }

    private Uri ScrolllerTarget(Uri uri)
    {
        if (!ScrolllerNavigationGate.IsMedia(uri)) return uri;
        _scrolllerOriginal = uri;
        return _scrolllerLocal = new Uri(WebAssetServer.Shared.ScrolllerUrl(uri));
    }

    internal static void StampScrolllerHeaders(Uri uri, WebViewWebRequestHeaders headers)
    {
        if (!ScrolllerNavigationGate.IsScrolller(uri)) return;
        headers.TrySet("User-Agent", ScrolllerSource.HonestUserAgent);
        if (headers.TryGetValue("Referer", out var referer) && !IsOurReferer(referer))
            headers.TrySet("Referer", ScrolllerNavigationGate.OurReferer);
    }

    internal static bool IsOurReferer(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals("cclabs.app", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".cclabs.app", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("ccp.", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase);
    }

    internal static void StopScrolllerForPanic()
    {
        WebAssetServer.StopScrolller();
        foreach (var weak in ScrolllerHosts)
            if (weak.TryGetTarget(out var host) && (host._scrolllerNavigation
                || host.Source is { } source && ScrolllerNavigationGate.IsMedia(source)))
            {
                host._scrolllerStopVersion++;
                host._scrolllerNavigation = false;
                host._scrolllerOriginal = host._scrolllerLocal = null;
                host._web?.Stop();
                host.Source = null;
            }
    }
}
