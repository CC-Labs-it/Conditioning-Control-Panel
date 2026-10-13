using System;
using System.Collections.Generic;
using Xunit;
using OverlayService = ConditioningControlPanel.Services.OverlayService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The WPF half of the multi-monitor video fix (#1016): the overlay z-order anchor lives on the WPF
/// OverlayService. The pure per-surface rules moved to CCP.Core with VideoSurfaceHealth and are
/// tested in Tests/CCP.Core.Tests/MultiMonitorVideoSurfaceTests.cs.
/// </summary>
public class OverlayVideoAnchorTests
{
    // ---- multi-monitor z-order anchor (deliverable 3, #1016) ----

    private static readonly IntPtr Mon1 = new IntPtr(0x1001);
    private static readonly IntPtr Mon2 = new IntPtr(0x1002);
    private static readonly IntPtr Video1 = new IntPtr(0xA001);
    private static readonly IntPtr Video2 = new IntPtr(0xA002);
    private static readonly IntPtr Overlay2 = new IntPtr(0xB002);

    private static List<(IntPtr Hwnd, IntPtr Monitor)> TwoScreenVideo() =>
        new() { (Video1, Mon1), (Video2, Mon2) };

    [Fact]
    public void OverlayOnSecondMonitor_AnchorsToThatMonitorsVideoWindow()
    {
        // #1016 itself: anchoring to the primary left the overlay ABOVE monitor 2's video window,
        // which is the "pink filter covers the video on my second screen" report.
        Assert.Equal(Video2, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Overlay2, targetMonitor: Mon2, fallbackHwnd: Video1));
    }

    [Fact]
    public void OverlayOnPrimaryMonitor_StillAnchorsToThePrimaryVideoWindow()
    {
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: new IntPtr(0xB001), targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void OverlayOnAMonitorWithNoVideoWindow_FallsBackToThePrimary()
    {
        // Nothing overlaps there, so the choice is cosmetic - but it must be a real window, not zero.
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: new IntPtr(0xB003), targetMonitor: new IntPtr(0x1003),
            fallbackHwnd: Video1));
    }

    [Fact]
    public void NoVideoPlaying_YieldsNoAnchor()
    {
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            new List<(IntPtr, IntPtr)>(), targetHwnd: Overlay2, targetMonitor: Mon2,
            fallbackHwnd: IntPtr.Zero));
    }

    [Fact]
    public void AVideoWindowIsNeverAnchoredToAnotherVideoWindow()
    {
        // Ordering a video window below its sibling would bury one of the screens outright.
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Video2, targetMonitor: Mon2, fallbackHwnd: Video1));
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Video1, targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void AWindowIsNeverAnchoredToItself()
    {
        // SetWindowPos(h, h, ...) would drop the pin entirely.
        Assert.Equal(IntPtr.Zero, OverlayService.ResolveVideoAnchor(
            new List<(IntPtr, IntPtr)>(), targetHwnd: Video1, targetMonitor: Mon1, fallbackHwnd: Video1));
    }

    [Fact]
    public void UnknownMonitorHandle_FallsBackRatherThanReturningZero()
    {
        // MonitorFromWindow can hand back NULL during a display-topology change; the sweep must
        // still pin the overlay below the video instead of skipping the #497 rule.
        Assert.Equal(Video1, OverlayService.ResolveVideoAnchor(
            TwoScreenVideo(), targetHwnd: Overlay2, targetMonitor: IntPtr.Zero, fallbackHwnd: Video1));
    }
}
