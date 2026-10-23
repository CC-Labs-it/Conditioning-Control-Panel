using System;
using System.IO;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The away watcher on this head (WPF ScrolllerAwayWatcher): the Windows window messages it hooks drive Core's away
/// rule, and the app starts and stops it with the other Scrolller start-up calls.
/// </summary>
[Collection(ScrolllerRelayCollection.Name)]   // ScrolllerPolicy is process-wide
public sealed class ScrolllerAwayHostTests : IDisposable
{
    public void Dispose() => ScrolllerAwayHost.Tracker.Reset();

    private static void Display(int state)
    {
        // POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; DWORD Data; }
        var p = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(ScrolllerAwayHost.Win32.ConsoleDisplayState, p, false);
            Marshal.WriteInt32(p, 16, 4);
            Marshal.WriteInt32(p, 20, state);
            ScrolllerAwayHost.Win32.Handle(ScrolllerAwayHost.Win32.WM_POWERBROADCAST, (IntPtr)ScrolllerAwayHost.Win32.PBT_POWERSETTINGCHANGE, p);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    [Fact]
    public void Session_lock_display_off_and_suspend_set_away()
    {
        ScrolllerAwayHost.Win32.Handle(ScrolllerAwayHost.Win32.WM_WTSSESSION_CHANGE, (IntPtr)ScrolllerAwayHost.Win32.WTS_SESSION_LOCK, IntPtr.Zero);
        Assert.True(ScrolllerPolicy.Away);
        ScrolllerAwayHost.Win32.Handle(ScrolllerAwayHost.Win32.WM_WTSSESSION_CHANGE, (IntPtr)ScrolllerAwayHost.Win32.WTS_SESSION_UNLOCK, IntPtr.Zero);
        Assert.False(ScrolllerPolicy.Away);

        Display(ScrolllerAwayRule.DisplayDimmed);
        Assert.False(ScrolllerPolicy.Away);
        Display(ScrolllerAwayRule.DisplayOff);
        Assert.True(ScrolllerPolicy.Away);
        Display(ScrolllerAwayRule.DisplayOn);
        Assert.False(ScrolllerPolicy.Away);

        ScrolllerAwayHost.Win32.Handle(ScrolllerAwayHost.Win32.WM_POWERBROADCAST, (IntPtr)ScrolllerAwayHost.Win32.PBT_APMSUSPEND, IntPtr.Zero);
        Assert.True(ScrolllerPolicy.Away);
        ScrolllerAwayHost.Win32.Handle(ScrolllerAwayHost.Win32.WM_POWERBROADCAST, (IntPtr)ScrolllerAwayHost.Win32.PBT_APMRESUMEAUTOMATIC, IntPtr.Zero);
        Assert.False(ScrolllerPolicy.Away);
    }

    [Fact]
    public void On_windows_the_message_window_registers_both_notifications()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            ScrolllerAwayHost.Win32.Start();
            Assert.True(ScrolllerAwayHost.Win32.Hooked);
        }
        finally { ScrolllerAwayHost.Win32.Stop(); }
        Assert.False(ScrolllerAwayHost.Win32.Hooked);
    }

    [Fact]
    public void The_app_starts_and_stops_the_watcher_beside_the_other_scrolller_calls()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        var app = File.ReadAllText(Path.Combine(dir!.FullName, "CCP.Avalonia", "App.axaml.cs"));
        Assert.Matches(@"ScrolllerHead\.Start\(\);\s*\r?\n\s*Platform\.ScrolllerAwayHost\.Start\(\);", app);
        Assert.Matches(@"ScrolllerHead\.Stop\(\);[^\n]*\r?\n\s*try \{ Platform\.ScrolllerAwayHost\.Stop\(\); \}", app);
    }
}
