using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ConditioningControlPanel.Services.Browser;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1504 / #1510 / #1513: the app died with UCEERR_RENDERTHREADFAILURE when the in-app
/// browser went fullscreen or was maximized, and an Intiface socket closed late showed up in
/// crash.log as a crash of its own. #1512: the Blink Trainer preview decoded pictures on the UI
/// thread inside the webcam's blink op.
/// </summary>
public sealed class BrowserFullscreenCrashTests
{
    [Fact]
    public void BrowserHost_GetsASoftwareTarget_BeforeItIsShown()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var window = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            try
            {
                BrowserHostSurface.UseSoftwareTarget(window);
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                var source = HwndSource.FromHwnd(hwnd);
                Assert.NotNull(source);
                Assert.Equal(System.Windows.Interop.RenderMode.SoftwareOnly, source!.CompositionTarget.RenderMode);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void BrowserHost_AlreadyBuilt_SwitchesAtOnce()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var window = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                BrowserHostSurface.UseSoftwareTarget(window);
                Assert.Equal(System.Windows.Interop.RenderMode.SoftwareOnly,
                    HwndSource.FromHwnd(hwnd)!.CompositionTarget.RenderMode);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void EveryWebViewHostWindow_UsesTheSoftwareTarget()
    {
        var browser = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow", "MainWindow.Browser.cs"));
        // The popout and the embedded-mode fullscreen host.
        Assert.Equal(2, Count(browser, "BrowserHostSurface.UseSoftwareTarget(_browserPopoutWindow)"));

        var deeper = File.ReadAllText(Path.Combine(SourceRoot(), "Views", "Deeper", "EnhancementPlayerWindow.xaml.cs"));
        Assert.Contains("BrowserHostSurface.UseSoftwareTarget(built)", deeper);
    }

    [Fact]
    public async Task CloseOnAnAbortedSocket_IsNotACrash()
    {
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = false });
        socket.Abort();

        var fault = await Assert.ThrowsAsync<WebSocketException>(
            () => socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None));

        Assert.True(BenignTaskFault.IsLateWebSocketClose(new AggregateException(fault)));
    }

    [Fact]
    public void OtherFaults_StillCountAsCrashes()
    {
        Assert.False(BenignTaskFault.IsLateWebSocketClose(null));
        Assert.False(BenignTaskFault.IsLateWebSocketClose(new AggregateException()));
        Assert.False(BenignTaskFault.IsLateWebSocketClose(new AggregateException(new InvalidOperationException("boom"))));
        // Right error code, but not thrown from a close.
        Assert.False(BenignTaskFault.IsLateWebSocketClose(
            new AggregateException(new WebSocketException(WebSocketError.InvalidState))));
    }

    [Fact]
    public async Task ALateClose_MixedWithARealFault_IsStillACrash()
    {
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = false });
        socket.Abort();
        var fault = await Assert.ThrowsAsync<WebSocketException>(
            () => socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None));

        Assert.False(BenignTaskFault.IsLateWebSocketClose(
            new AggregateException(fault, new InvalidOperationException("boom"))));
    }

    [Fact]
    public void BlinkTrainerPreview_DecodesOffTheUiThread()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow", "MainWindow.BlinkTrainer.cs"));
        int start = source.IndexOf("void ApplyBlinkTrainerLiveImage(", StringComparison.Ordinal);
        int end = source.IndexOf("BitmapImage? DecodeBlinkTrainerStill(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "live image swap / decode helper not found");
        var swap = source.Substring(start, end - start);

        Assert.Contains("await Task.Run(() => DecodeBlinkTrainerStill(path))", swap);
        // The swap itself never decodes on the UI thread.
        Assert.DoesNotContain("EndInit()", swap);
    }

    private static int Count(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
