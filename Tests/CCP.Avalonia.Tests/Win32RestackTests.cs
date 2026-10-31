using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Win32RestackTests
{
    // Models user32's top-to-bottom order and insert-AFTER contract, not an X11 Above request.
    [Fact]
    public void TwoFlipsRestoreOrderWithoutActivationGeometryOrOwnerChanges()
    {
        var a = new IntPtr(1);
        var b = new IntPtr(2);
        var other = new IntPtr(3);
        var order = new List<IntPtr> { other, a, b };
        var calls = 0;
        IntPtr Previous(IntPtr h) => order.IndexOf(h) is var i && i > 0 ? order[i - 1] : IntPtr.Zero;
        bool Position(IntPtr h, IntPtr after, uint flags)
        {
            calls++;
            Assert.Equal(0x213u, flags); // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER
            order.Remove(h);
            order.Insert(after == new IntPtr(-1) ? 0 : order.IndexOf(after) + 1, h);
            return true;
        }
        Assert.True(Win32Overlay.RestackAbove(b, a, Previous, _ => true, Position));
        Assert.Equal(new[] { other, b, a }, order);
        Assert.True(Win32Overlay.RestackAbove(a, b, Previous, _ => true, Position));
        Assert.Equal(new[] { other, a, b }, order);
        Assert.Equal(2, calls);
        Assert.True(Win32Overlay.RestackAbove(a, b, Previous, _ => true, Position));
        Assert.Equal(2, calls); // already adjacent, no native churn
    }

    [Fact]
    public void FirstSiblingUsesHwndTopmostAndPropagatesNativeFailure()
    {
        var called = false;
        Assert.False(Win32Overlay.RestackAbove(new IntPtr(2), new IntPtr(1), _ => IntPtr.Zero, _ => true,
            (h, after, flags) => { called = true; Assert.Equal(new IntPtr(-1), after); return false; }));
        Assert.True(called);
    }

    [Theory]
    [InlineData(3)] // unrelated topmost predecessor
    [InlineData(2)] // the moving window is already adjacent but in the wrong band
    [InlineData(0)] // no predecessor
    public void NormalSiblingDoesNotPromoteWindowIntoTopmostBand(int predecessor)
    {
        var called = false;
        Assert.True(Win32Overlay.RestackAbove(new IntPtr(2), new IntPtr(1), _ => new IntPtr(predecessor),
            h => h != new IntPtr(1), (h, after, flags) =>
            {
                called = true;
                Assert.Equal(new IntPtr(-2), after); // HWND_NOTOPMOST
                return true;
            }));
        Assert.True(called);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FirstNormalSiblingEndsDirectlyBelowTube(bool tubeTopmost, bool otherTopmost)
    {
        var main = new IntPtr(1);
        var tube = new IntPtr(2);
        var other = new IntPtr(3);
        var order = new List<IntPtr>();
        var topmost = new HashSet<IntPtr>();
        if (otherTopmost) { order.Add(other); topmost.Add(other); }
        if (tubeTopmost) { order.Add(tube); topmost.Add(tube); }
        order.Add(main);
        if (!tubeTopmost) order.Add(tube);
        var calls = 0;
        Assert.True(Win32Overlay.RestackAbove(tube, main,
            h => order.IndexOf(h) is var i && i > 0 ? order[i - 1] : IntPtr.Zero,
            topmost.Contains, (h, after, flags) =>
            {
                calls++;
                Assert.Equal(0x213u, flags);
                // user32 explicitly leaves an already non-topmost window in place for NOTOPMOST.
                if (after == new IntPtr(-2) && !topmost.Contains(h)) return true;
                Assert.True(after == IntPtr.Zero || after == new IntPtr(-2));
                topmost.Remove(h);
                order.Remove(h);
                order.Insert(order.FindIndex(w => !topmost.Contains(w)), h);
                return true;
            }));
        Assert.Equal(1, calls);
        Assert.False(topmost.Contains(tube));
        Assert.Equal(otherTopmost ? new[] { other, tube, main } : new[] { tube, main }, order);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void InvalidOrSelfHandlesNeverReachNativeCalls(int window, int sibling)
        => Assert.False(Win32Overlay.RestackAbove(new IntPtr(window), new IntPtr(sibling),
            _ => throw new Exception("Unexpected native read"),
            _ => throw new Exception("Unexpected style read"),
            (_, _, _) => throw new Exception("Unexpected native write")));
}
