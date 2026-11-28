using System;
using System.Threading.Tasks;
using Avalonia.Platform;
using ConditioningControlPanel.Services.Video.Browser;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

// WPF ChaosWebViewHost.cs:1581-1600 (#938): hosted local pages send their audio to the output device the
// user picked. Set HostedPageSink before the first navigation; remote pages never get it. On Windows the
// same hosts always get the microphone handler (WPF GoonHostService.cs:577-660, Goon voice notes).
public partial class WebHost
{
    internal bool HostedPageSink { get; set; }
    /// <summary>Test seam; a null script means the microphone handler alone.</summary>
    internal Func<string?, Task<bool>>? SinkScriptInstaller { get; set; }
    internal static Func<bool> IsWindowsHost = OperatingSystem.IsWindows;
    private Task<bool>? _sinkInstall;

    /// <summary>Once per engine: the routing script only with a chosen device that has a label
    /// (BrowserSinkLabel.Resolve); on Windows the microphone handler even without one.</summary>
    private Task<bool>? SinkInstall()
    {
        if (!HostedPageSink) return null;
        var script = BrowserSinkLabel.Resolve() is { } label ? BrowserSinkLabel.RoutingScript(label) : null;
        if (script == null && !IsWindowsHost()) return null;
        return _sinkInstall ??= InstallSinkAsync(script);
    }

    private async Task<bool> InstallSinkAsync(string? script)
    {
        try
        {
            if (SinkScriptInstaller is { } install) return await install(script);
            if (OperatingSystem.IsWindows()) { await InstallWindowsSink(script); return true; }
            // WebKitGTK exposes no setSinkId, so the script stays inert there and never runs the mic probe.
            return script != null && await Platform.WebViewCarrier.InstallAsync(_web?.TryGetPlatformHandle(), script);
        }
        catch (Exception ex) { Log.Debug("WebHost: sink routing install failed: {E}", ex.Message); return false; }
    }

    // WPF OnPermissionRequested (ChaosWebViewHost.cs:1374-1389, GoonHostService.cs:631-657): the microphone
    // only for this app's own local pages; any other origin is denied; other permissions are left alone.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task InstallWindowsSink(string? script)
    {
        if (_web?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle win || win.CoreWebView2 == IntPtr.Zero)
            throw new InvalidOperationException("WebView2 core unavailable");
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(win.CoreWebView2);
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind != Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone) return;
            var allow = Platform.WebAssetServer.AllowsSharedMicrophone(e.Uri);
            e.State = allow
                ? Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow
                : Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            e.Handled = true;
            // WPF GoonHostService.cs:648-653: an answer banked in the profile would outlive this policy.
            if (allow) try { e.SavesInProfile = false; } catch { /* older runtime */ }
        };
        // WPF ClearBankedMicDenial (GoonHostService.cs:612-627): a saved deny is served without asking us.
        // Fire and forget, as WPF: the repair must not hold the first navigation.
        if (Platform.WebAssetServer.SharedOrigin is { } origin)
            _ = core.Profile.SetPermissionStateAsync(Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone,
                    origin, Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow)
                .ContinueWith(t => Log.Debug("WebHost: mic permission state write failed: {E}", t.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
        if (script != null) await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }
}
