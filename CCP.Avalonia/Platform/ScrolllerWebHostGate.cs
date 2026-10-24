using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Scrolller gate on every web view this head makes (WPF: "ScrolllerWebGate in EVERY WebView2 host"). Every web view
/// is a <see cref="Views.Controls.WebHost"/> (the only place a NativeWebView is built; ScrolllerWebGateTests pins that),
/// and WebHost calls <see cref="Attach"/> on its engine and HOLDS its first navigation until the gate reports: Windows
/// (WebView2) gets <see cref="ScrolllerWebView2Gate"/>, Linux (WebKitGTK) gets <see cref="ScrolllerWebKitFilter"/>.
/// The ready callback runs once on the UI thread: true = gated, false = the gate could not be had (the host then
/// refuses every page that is not this head's own). Never throws.
/// </summary>
internal static class ScrolllerWebHostGate
{
    /// <summary>How often, and how many times, a Windows adapter is asked again for a core that does not exist yet.</summary>
    internal static readonly TimeSpan CoreRetry = TimeSpan.FromMilliseconds(100);
    internal const int CoreRetries = 50;

    public static void Attach(NativeWebView web, Action<bool> ready)
    {
        web.AdapterCreated += (_, e) =>
        {
            IPlatformHandle? handle = null;
            try { handle = e.TryGetPlatformHandle() ?? web.TryGetPlatformHandle(); } catch { }
            void Go() => OnAdapter(handle, () => { try { return web.TryGetPlatformHandle(); } catch { return null; } }, ready);
            if (Dispatcher.UIThread.CheckAccess()) Go(); else Dispatcher.UIThread.Post(Go);
        };
    }

    /// <summary>One adapter's handle onto its gate (UI thread: WebView2 objects are bound to it). A Windows core that is not
    /// there yet is asked for again (<paramref name="handleAgain"/>) until it is, or until the retries run out.</summary>
    internal static void OnAdapter(IPlatformHandle? handle, Func<IPlatformHandle?> handleAgain, Action<bool> ready)
    {
        try
        {
            switch (handle)
            {
                case IWindowsWebView2PlatformHandle when OperatingSystem.IsWindows():
                    AttachWindows(handle, handleAgain, ready, CoreRetries);
                    return;
                case IGtkWebViewPlatformHandle g:
                    ScrolllerWebKitFilter.Attach(g.WebKitWebView, ok => Dispatcher.UIThread.Post(() => ready(ok)));
                    return;
                default:
                    Log.Warning("ScrolllerWebHostGate: no Scrolller gate for a {Engine} web view; only this head's own pages load in it",
                        handle?.HandleDescriptor ?? "missing");
                    ready(false);
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("ScrolllerWebHostGate: attach failed: {E}", ex.Message);
            ready(false);
        }
    }

    private static void AttachWindows(IPlatformHandle? handle, Func<IPlatformHandle?> handleAgain, Action<bool> ready, int left)
    {
        var core = handle is IWindowsWebView2PlatformHandle w ? w.CoreWebView2 : IntPtr.Zero;
        if (core != IntPtr.Zero) { ready(AttachWebView2(core)); return; }
        if (left == CoreRetries) Log.Warning("ScrolllerWebHostGate: the adapter has no CoreWebView2 yet; waiting for it");
        if (left <= 0)
        {
            Log.Warning("ScrolllerWebHostGate: no CoreWebView2 after {N} tries; only this head's own pages load in this web view", CoreRetries);
            ready(false);
            return;
        }
        DispatcherTimer.RunOnce(() => AttachWindows(handleAgain(), handleAgain, ready, left - 1), CoreRetry);
    }

    /// <summary>Its own method, so the WebView2 SDK is only ever loaded on Windows.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool AttachWebView2(IntPtr core) => ScrolllerWebView2Gate.Attach(core);
}
