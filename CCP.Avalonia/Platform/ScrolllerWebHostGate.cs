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
/// and WebHost calls <see cref="Attach"/> on its engine, so a new host cannot ship without the gate. Once the engine's
/// adapter exists: Windows (WebView2) gets <see cref="ScrolllerWebView2Gate"/>, Linux (WebKitGTK) gets
/// <see cref="ScrolllerWebKitFilter"/>. Never throws.
/// </summary>
internal static class ScrolllerWebHostGate
{
    public static void Attach(NativeWebView web)
    {
        web.AdapterCreated += (_, e) =>
        {
            IPlatformHandle? handle = null;
            try { handle = e.TryGetPlatformHandle() ?? web.TryGetPlatformHandle(); } catch { }
            if (Dispatcher.UIThread.CheckAccess()) OnAdapter(handle);
            else Dispatcher.UIThread.Post(() => OnAdapter(handle));
        };
    }

    /// <summary>One adapter's handle onto its gate (UI thread: WebView2 objects are bound to it).</summary>
    internal static void OnAdapter(IPlatformHandle? handle)
    {
        try
        {
            switch (handle)
            {
                case IWindowsWebView2PlatformHandle w when OperatingSystem.IsWindows():
                    AttachWebView2(w.CoreWebView2);
                    break;
                case IGtkWebViewPlatformHandle g:
                    ScrolllerWebKitFilter.Attach(g.WebKitWebView);
                    break;
                case null:
                    break;
                default:
                    Log.Warning("ScrolllerWebHostGate: no Scrolller gate for a {Engine} web view", handle.HandleDescriptor ?? handle.GetType().Name);
                    break;
            }
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebHostGate: attach failed: {E}", ex.Message); }
    }

    /// <summary>Its own method, so the WebView2 SDK is only ever loaded on Windows.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AttachWebView2(IntPtr core) => ScrolllerWebView2Gate.Attach(core);
}
