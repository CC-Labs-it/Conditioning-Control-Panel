using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using ConditioningControlPanel.Services.JustDrop;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF JustDropAssetHost.Attach/OnRequest/ServeAsync (:158-212): answers <c>https://justdrop-assets.ccp/*</c> on the
/// Just Drop window's WebView2 core from Core <see cref="JustDropAssetMap.Answer"/>. Avalonia.Controls.WebView has no
/// response seam, so the core comes from the adapter's platform handle (WebHost.ConfigureNative, once per engine).
/// WebKitGTK cannot answer a foreign https host: <see cref="Supported"/> is false there and the window lists nothing.
/// </summary>
internal static class JustDropAssetWebView2
{
    internal static bool Supported => OperatingSystem.IsWindows();

    internal static void Attach(IPlatformHandle? handle, JustDropAssetMap map)
    {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2 != IntPtr.Zero)
        {
            try { Windows(win.CoreWebView2, map); }
            catch (Exception ex) { Log.Warning("JustDropAssets: filter attach failed: {E}", ex.Message); }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SupportedOSPlatform("windows")]
    private static void Windows(IntPtr pointer, JustDropAssetMap map)
    {
        var core = CoreWebView2.CreateFromComICoreWebView2(pointer);
        core.AddWebResourceRequestedFilter(JustDropAssetMap.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                var uri = e.Request.Uri;
                // Every filter on this core raises this event (the Scrolller gate, the sign-in header): our host only.
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.Equals(u.Host, JustDropAssetMap.Host, StringComparison.OrdinalIgnoreCase)) return;
                var method = e.Request.Method;
                string? range = null;
                try { range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null; } catch { }
                var deferral = e.GetDeferral();
                _ = Serve(core, e, deferral, () => map.Answer(method, uri, range));
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: request hook failed: {E}", ex.Message); }
        };
    }

    [SupportedOSPlatform("windows")]
    private static async Task Serve(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral,
        Func<JustDropAssetMap.Response?> answer)
    {
        try
        {
            if (await Task.Run(answer) is { } r)
                e.Response = core.Environment.CreateWebResourceResponse(r.Body, r.Status, r.Reason, r.Headers);
        }
        catch (Exception ex) { Log.Debug("JustDropAssets: answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }
}
