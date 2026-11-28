using System;
using System.Threading.Tasks;
using Avalonia.Platform;
using ConditioningControlPanel.Services.Video.Browser;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

// WPF ChaosWebViewHost.cs:1581-1600 (#938): hosted local pages send their audio to the output device the
// user picked. Set HostedPageSink before the first navigation; remote pages never get it.
public partial class WebHost
{
    internal bool HostedPageSink { get; set; }
    internal Func<string, Task<bool>>? SinkScriptInstaller { get; set; }
    private Task<bool>? _sinkInstall;

    /// <summary>Once per engine, only with a chosen device that has a label (BrowserSinkLabel.Resolve).</summary>
    private Task<bool>? SinkInstall()
    {
        if (!HostedPageSink || BrowserSinkLabel.Resolve() is not { } label) return null;
        return _sinkInstall ??= InstallSinkAsync(BrowserSinkLabel.RoutingScript(label));
    }

    private async Task<bool> InstallSinkAsync(string script)
    {
        try
        {
            if (SinkScriptInstaller is { } install) return await install(script);
            if (OperatingSystem.IsWindows()) { await InstallWindowsSink(script); return true; }
            // WebKitGTK exposes no setSinkId, so the script stays inert there and never runs the mic probe.
            return await Platform.WebViewCarrier.InstallAsync(_web?.TryGetPlatformHandle(), script);
        }
        catch (Exception ex) { Log.Debug("WebHost: sink routing install failed: {E}", ex.Message); return false; }
    }

    // WPF OnPermissionRequested (ChaosWebViewHost.cs:1374-1389): the microphone, for the label probe, only
    // for this app's own local pages; any other origin is denied; other permissions are left alone.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task InstallWindowsSink(string script)
    {
        if (_web?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle win || win.CoreWebView2 == IntPtr.Zero)
            throw new InvalidOperationException("WebView2 core unavailable");
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(win.CoreWebView2);
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind != Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone) return;
            e.State = Platform.WebAssetServer.AllowsSharedMicrophone(e.Uri)
                ? Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow
                : Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            e.Handled = true;
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }
    // DevTools gesture execution is provided by WebView2.
    internal Func<string, Task>? FullscreenGestureInvoker { get; set; }
    internal bool CanRetryFullscreenGesture => FullscreenGestureInvoker != null ||
        (OperatingSystem.IsWindows() && _web?.TryGetPlatformHandle() is IWindowsWebView2PlatformHandle w && w.CoreWebView2 != IntPtr.Zero);

    internal bool? TakeoverFullscreenFallbackOverride { get; set; }
    internal bool CanUseTakeoverFullscreenFallback => TakeoverFullscreenFallbackOverride ??
        (OperatingSystem.IsLinux() && _web?.TryGetPlatformHandle() is IGtkWebViewPlatformHandle or ILinuxWpePlatformHandle);

    internal Task RetryFullscreenWithGesture(string parameters)
    {
        if (FullscreenGestureInvoker is { } invoke) return invoke(parameters);
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        return RetryFullscreenWindows(parameters);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Task RetryFullscreenWindows(string parameters)
    {
        if (_web?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle win || win.CoreWebView2 == IntPtr.Zero)
            return Task.CompletedTask;
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(win.CoreWebView2);
        return core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
    }
}
