using System;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ForegroundGrabTests
{
    [Fact]
    public void Plan_selects_owned_direct_and_borrow_paths()
    {
        var target = new IntPtr(1);
        Assert.Equal(ForegroundGrab.Step.AlreadyOwned, ForegroundGrab.Plan(target, target, 2, 3));
        Assert.Equal(ForegroundGrab.Step.Direct, ForegroundGrab.Plan(IntPtr.Zero, target, 0, 3));
        Assert.Equal(ForegroundGrab.Step.Direct, ForegroundGrab.Plan(new IntPtr(2), target, 3, 3));
        Assert.Equal(ForegroundGrab.Step.BorrowInput, ForegroundGrab.Plan(new IntPtr(2), target, 2, 3));
    }

    [Fact]
    public void Recheck_retries_only_a_live_missing_foreground()
    {
        var h = new IntPtr(1);
        int takes = 0, focuses = 0;
        void Check(bool wanted, bool owns, IntPtr handle) => ForegroundGrab.Recheck(
            ForegroundGrab.Generation, () => wanted, () => handle, _ => owns,
            _ => { takes++; return true; }, () => focuses++, "test");
        Check(false, false, h);
        Check(true, true, h);
        Check(true, false, IntPtr.Zero);
        Assert.Equal(0, takes);
        Check(true, false, h);
        Assert.Equal(1, takes);
        Assert.Equal(1, focuses);
    }

    [Fact]
    public void Panic_surface_cancels_queued_retry()
    {
        var field = typeof(ForegroundGrab).GetField("_generation", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = ForegroundGrab.Generation;
        try
        {
            int takes = 0, focuses = 0;
            int pending = ForegroundGrab.Generation;
            PanicSurfaces.All.Single(s => s.Id == "foreground-grab").Stop(null);
            ForegroundGrab.Recheck(pending, () => true, () => new IntPtr(1), _ => false,
                _ => { takes++; return true; }, () => focuses++, "test");
            Assert.Equal(0, takes);
            Assert.Equal(0, focuses);
        }
        finally { field.SetValue(null, saved); }
    }
}
