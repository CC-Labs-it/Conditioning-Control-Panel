using System;
using System.Collections.Generic;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WindowsNotificationsTests
{
    [Fact]
    public void BalloonAddsUpdatesAndCleansUp() => AvaloniaTestDispatcher.Run(() =>
    {
        var calls = new List<(uint Command, WindowsNotifications.NotifyIconData Data)>();
        using var notifications = new WindowsNotifications((IntPtr)42, (IntPtr)7, (c, d) => { calls.Add((c, d)); return true; });
        Assert.True(notifications.Show("Title", "Body"));
        Assert.Equal(WindowsNotifications.Add, calls[0].Command);
        Assert.Equal(WindowsNotifications.Modify, calls[1].Command);
        Assert.Equal("Title", calls[1].Data.InfoTitle);
        Assert.Equal("Body", calls[1].Data.Info);
        Assert.Equal((IntPtr)42, calls[1].Data.Window);
        Assert.True(notifications.Show(new string('a', 100), new string('b', 300)));
        Assert.Equal(63, calls[2].Data.InfoTitle.Length);
        Assert.Equal(255, calls[2].Data.Info.Length);
        notifications.Dispose();
        notifications.Dispose();
        Assert.Equal(4, calls.Count);
        Assert.Equal(WindowsNotifications.Delete, calls[3].Command);
    });

    [Theory]
    [InlineData(WindowsNotifications.Add)]
    [InlineData(WindowsNotifications.Modify)]
    public void NativeFailureFallsBackAndCleansUp(uint fail) => AvaloniaTestDispatcher.Run(() =>
    {
        var calls = new List<uint>();
        using var notifications = new WindowsNotifications((IntPtr)42, (IntPtr)7, (c, _) => { calls.Add(c); return c != fail; });
        var delivered = notifications.Show("Title", "Body");
        Assert.False(delivered);
        Assert.Equal(OsNotifications.Route.Toast, OsNotifications.Decide(delivered, true));
        Assert.Equal(OsNotifications.Route.Drop, OsNotifications.Decide(delivered, false));
        Assert.Equal(fail == WindowsNotifications.Modify, calls.Contains(WindowsNotifications.Delete));
    });

    [Fact]
    public void MissingWindowNeverCallsNative() => AvaloniaTestDispatcher.Run(() =>
    {
        using var notifications = new WindowsNotifications(IntPtr.Zero, (IntPtr)7, (_, _) => throw new Exception());
        Assert.False(notifications.Show("Title", "Body"));
    });
}
