using System;
using System.Net;
using System.Threading;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Platform;

public sealed partial class WebAssetServer
{
    private static readonly object ScrolllerLock = new();
    private static CancellationTokenSource _scrolllerLife = new();
    private static long _scrolllerEpoch;

    internal string ScrolllerUrl(Uri uri)
    {
        lock (ScrolllerLock)
            return Url("ccp.scrolller", "epoch=" + _scrolllerEpoch + "&url=" + Uri.EscapeDataString(uri.AbsoluteUri));
    }

    internal static void StopScrolller()
    {
        lock (ScrolllerLock)
        {
            _scrolllerEpoch++;
            _scrolllerLife.Cancel();
            _scrolllerLife.Dispose();
            _scrolllerLife = new CancellationTokenSource();
        }
    }

    private static bool ScrolllerRequestAlive(HttpListenerContext ctx)
    {
        lock (ScrolllerLock)
            return ScrolllerPolicy.Enabled && long.TryParse(ctx.Request.QueryString["epoch"], out var epoch)
                && epoch == _scrolllerEpoch;
    }

    private static string? ResolveScrolller(HttpListenerContext ctx)
    {
        CancellationToken ct;
        lock (ScrolllerLock)
        {
            if (!long.TryParse(ctx.Request.QueryString["epoch"], out var epoch) || epoch != _scrolllerEpoch) return null;
            ct = _scrolllerLife.Token;
        }
        // Serve already runs on a bounded worker. No UI wait and no generic forward proxy:
        // Core accepts only Scrolller media, never arbitrary hosts, API calls or local files.
        var path = ScrolllerNavigationGate.ResolveAsync(ctx.Request.QueryString["url"] ?? "", ct).GetAwaiter().GetResult();
        return ct.IsCancellationRequested || !ScrolllerPolicy.Enabled ? null : path;
    }
}
