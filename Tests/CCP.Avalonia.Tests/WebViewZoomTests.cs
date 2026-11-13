using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebViewZoomTests
{
    private sealed class GtkHandle : IGtkWebViewPlatformHandle
    {
        public IntPtr WebKitWebView => new(17);
        public IntPtr Handle => WebKitWebView;
        public string HandleDescriptor => "gtk";
    }
    private sealed class WpeHandle : ILinuxWpePlatformHandle
    {
        public IntPtr WebKitWebView => new(19);
        public IntPtr WpeViewBackend => new(20);
        public IntPtr Handle => WebKitWebView;
        public string HandleDescriptor => "wpe";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxUsesTheCorrectWebKitView(bool wpe)
    {
        IPlatformHandle handle = wpe ? new WpeHandle() : new GtkHandle();
        Assert.Equal((new IntPtr(wpe ? 19 : 17), wpe), WebViewZoom.LinuxView(handle));
        Assert.True(WebViewZoom.IsReady(handle));
        Assert.False(WebViewZoom.IsReady(null));
    }

    [Fact]
    public void WindowsUsesTheControllerZoomSetter()
    {
        double value = 0;
        WebViewZoom.WinSetFn set = (IntPtr _, double zoom) => { value = zoom; return 0; };
        var table = Marshal.AllocHGlobal(9 * IntPtr.Size);
        var controller = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(table, 8 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(set));
            Marshal.WriteIntPtr(controller, table);
            WebViewZoom.SetWindows(controller, 0.5);
            Assert.Equal(0.5, value);
        }
        finally { Marshal.FreeHGlobal(controller); Marshal.FreeHGlobal(table); GC.KeepAlive(set); }
    }
}
