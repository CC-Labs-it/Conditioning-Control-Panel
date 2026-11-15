using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

// Run only on a dedicated X server with a reparenting WM, never a user's desktop.
public sealed class X11OverlayRaiseNativeTests
{
    private const string X11 = "libX11.so.6";
    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr d);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(X11)] private static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr parent, int x, int y, uint w, uint h, uint border, nuint borderColor, nuint background);
    [DllImport(X11)] private static extern int XChangeWindowAttributes(IntPtr d, IntPtr w, nuint mask, IntPtr attrs);
    [DllImport(X11)] private static extern IntPtr XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport(X11)] private static extern int XChangeProperty(IntPtr d, IntPtr w, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport(X11)] private static extern int XReparentWindow(IntPtr d, IntPtr w, IntPtr parent, int x, int y);
    [DllImport(X11)] private static extern int XLowerWindow(IntPtr d, IntPtr w);
    [DllImport(X11)] private static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(X11)] private static extern int XDestroyWindow(IntPtr d, IntPtr w);
    [DllImport(X11)] private static extern int XSync(IntPtr d, bool discard);
    [DllImport(X11)] private static extern int XQueryTree(IntPtr d, IntPtr w, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport(X11)] private static extern int XFree(IntPtr p);

    private static IntPtr Parent(IntPtr d, IntPtr w)
    {
        Assert.NotEqual(0, XQueryTree(d, w, out _, out var parent, out var children, out _));
        if (children != IntPtr.Zero) XFree(children);
        return parent;
    }
    private static IntPtr Frame(IntPtr d, IntPtr w, IntPtr root)
    {
        for (int i = 0; i < 32; i++)
        {
            var parent = Parent(d, w);
            if (parent == root) return w;
            Assert.NotEqual(IntPtr.Zero, parent);
            w = parent;
        }
        throw new InvalidOperationException("X11 parent chain exceeds 32 windows");
    }
    private static int RootIndex(IntPtr d, IntPtr root, IntPtr w)
    {
        XSync(d, false);
        Assert.NotEqual(0, XQueryTree(d, root, out _, out _, out var children, out var count));
        try { return Enumerable.Range(0, (int)count).FirstOrDefault(i => Marshal.ReadIntPtr(children, i * IntPtr.Size) == w, -1); }
        finally { if (children != IntPtr.Zero) XFree(children); }
    }
    private static bool WaitFor(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5)) { if (predicate()) return true; Thread.Sleep(20); }
        return false;
    }
    private static IntPtr MakeOverrideRedirect(IntPtr d, IntPtr root)
    {
        var w = XCreateSimpleWindow(d, root, 0, 0, 100, 100, 0, 0, 0);
        var attrs = Marshal.AllocHGlobal(112);
        try
        {
            for (int i = 0; i < 112; i += IntPtr.Size) Marshal.WriteIntPtr(attrs, i, IntPtr.Zero);
            Marshal.WriteInt32(attrs, 88, 1);
            XChangeWindowAttributes(d, w, 1u << 9, attrs);
        }
        finally { Marshal.FreeHGlobal(attrs); }
        XMapWindow(d, w); XSync(d, false);
        return w;
    }

    [Fact]
    public void RaisesReparentedManagedFrameAndOverrideRedirectWindow()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && IntPtr.Size == 8 && Environment.GetEnvironmentVariable("CCP_TEST_REPARENTING_X11") == "1",
            "Requires an isolated X11 display with a reparenting window manager.");
        var d = XOpenDisplay(IntPtr.Zero); Assert.NotEqual(IntPtr.Zero, d);
        var root = XDefaultRootWindow(d);
        var client = XCreateSimpleWindow(d, root, 0, 0, 120, 120, 0, 0, 0);
        var other = IntPtr.Zero; var unmanaged = IntPtr.Zero; var manualFrame = IntPtr.Zero;
        try
        {
            // Brain Drain and its topmost siblings request the above layer before mapping.
            void MarkTopmost(IntPtr w) => XChangeProperty(d, w, XInternAtom(d, "_NET_WM_STATE", false), new IntPtr(4), 32, 0,
                new[] { XInternAtom(d, "_NET_WM_STATE_ABOVE", false) }, 1);
            MarkTopmost(client);
            XMapWindow(d, client); XSync(d, false);
            Assert.True(WaitFor(() => Parent(d, client) != root), "WM did not reparent the managed client");
            var frame = Frame(d, client, root);
            other = XCreateSimpleWindow(d, root, 0, 0, 100, 100, 0, 0, 0);
            MarkTopmost(other); XMapWindow(d, other); XSync(d, false);
            Assert.True(WaitFor(() => Parent(d, other) != root), "WM did not reparent the sibling");
            var otherFrame = Frame(d, other, root);
            Assert.True(WaitFor(() => RootIndex(d, root, otherFrame) > RootIndex(d, root, frame)), "sibling must start above the managed frame");
            Assert.True(X11Overlay.RaiseX11(client));
            Assert.True(WaitFor(() => RootIndex(d, root, frame) > RootIndex(d, root, otherFrame)), "managed frame was not raised");
            unmanaged = MakeOverrideRedirect(d, root);
            XLowerWindow(d, unmanaged); XSync(d, false);
            Assert.True(RootIndex(d, root, unmanaged) < RootIndex(d, root, otherFrame));
            Assert.True(X11Overlay.RaiseX11(unmanaged));
            Assert.True(WaitFor(() => RootIndex(d, root, unmanaged) > RootIndex(d, root, otherFrame)), "override-redirect window was not raised");

            // A reparenting WM can leave its frame outside redirected configure requests.
            // Model that native shape using an override-redirect frame and a nested client.
            manualFrame = MakeOverrideRedirect(d, root);
            var nested = XCreateSimpleWindow(d, root, 0, 0, 80, 80, 0, 0, 0);
            XReparentWindow(d, nested, manualFrame, 0, 0); XMapWindow(d, nested); XSync(d, false);
            Assert.Equal(manualFrame, Parent(d, nested));
            Assert.True(X11Overlay.RaiseX11(unmanaged));
            Assert.True(RootIndex(d, root, manualFrame) < RootIndex(d, root, unmanaged));
            Assert.True(X11Overlay.RaiseX11(nested));
            Assert.True(WaitFor(() => RootIndex(d, root, manualFrame) > RootIndex(d, root, unmanaged)), "root frame was not raised");
        }
        finally
        {
            if (manualFrame != IntPtr.Zero) XDestroyWindow(d, manualFrame);
            if (unmanaged != IntPtr.Zero) XDestroyWindow(d, unmanaged);
            if (other != IntPtr.Zero) XDestroyWindow(d, other);
            XDestroyWindow(d, client); XSync(d, false); XCloseDisplay(d);
        }
    }
}
