using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CountBubbleStackingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewWindowsBubbleHeadsTheWholeTopmostBand(bool hasOtherOverlay)
    {
        // WPF Windows/BubbleCountWindow.xaml.cs:1729: new bubbles raise above all topmost windows.
        var owner = new IntPtr(1);
        var otherOverlay = new IntPtr(2);
        var first = new IntPtr(3);
        var second = new IntPtr(4);
        var order = new List<IntPtr> { owner };
        if (hasOtherOverlay) order.Insert(0, otherOverlay);
        void Spawn(IntPtr bubble)
        {
            order.Insert(0, bubble); // Avalonia Topmost=true on Show, before the explicit native call.
            CountBubble.StackWindow(OverlayBackend.Win32,
                () => { order.Remove(bubble); order.Insert(0, bubble); },
                () => Win32Overlay.RestackAbove(bubble, owner,
                    h => order.IndexOf(h) is var i && i > 0 ? order[i - 1] : IntPtr.Zero,
                    _ => true, (h, after, flags) =>
                    {
                        order.Remove(h);
                        order.Insert(after == new IntPtr(-1) ? 0 : order.IndexOf(after) + 1, h);
                        return true;
                    }));
        }
        Spawn(first);
        Assert.Equal(hasOtherOverlay ? new[] { first, otherOverlay, owner } : new[] { first, owner }, order);
        Spawn(second);
        Assert.Equal(hasOtherOverlay ? new[] { second, first, otherOverlay, owner } : new[] { second, first, owner }, order);
    }
}
