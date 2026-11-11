using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
        var cleared = 0;
        using var notifications = new WindowsNotifications((IntPtr)42, 7, (c, d) => { calls.Add((c, d)); return true; }, () => cleared++);
        Assert.True(notifications.Show("Title", "Body"));
        Assert.Single(calls);
        Assert.Equal(WindowsNotifications.Modify, calls[0].Command);
        Assert.Equal(16u, calls[0].Data.Flags); // NIF_INFO only: existing icon, callback and menu stay intact
        Assert.Equal(7u, calls[0].Data.Id);
        Assert.Equal("Title", calls[0].Data.InfoTitle);
        Assert.Equal("Body", calls[0].Data.Info);
        Assert.Equal((IntPtr)42, calls[0].Data.Window);
        Assert.True(notifications.Show(new string('a', 100), new string('b', 300)));
        Assert.Equal(63, calls[1].Data.InfoTitle.Length);
        Assert.Equal(255, calls[1].Data.Info.Length);
        Assert.True(notifications.Show(new string('a', 62) + "\U0001F600", "x"));
        Assert.Equal(new string('a', 62), calls[2].Data.InfoTitle); // a surrogate pair is never cut in half
        notifications.Dispose();
        notifications.Dispose();
        Assert.Equal(3, calls.Count); // never add or delete a second icon
        Assert.Equal(1, cleared);
    });

    [Theory]
    [InlineData(false)] // no native tray identity
    [InlineData(true)]  // Shell_NotifyIcon fails
    public void NativeFailureFallsBackAndCleansUp(bool hasIdentity) => AvaloniaTestDispatcher.Run(() =>
    {
        var calls = new List<uint>();
        var cleared = 0;
        using var notifications = new WindowsNotifications((IntPtr)42, hasIdentity ? 7u : 0u, (c, _) => { calls.Add(c); return false; }, () => cleared++);
        var delivered = notifications.Show("Title", "Body");
        Assert.False(delivered);
        Assert.Equal(OsNotifications.Route.Toast, OsNotifications.Decide(delivered, true));
        Assert.Equal(OsNotifications.Route.Drop, OsNotifications.Decide(delivered, false));
        Assert.Equal(hasIdentity ? 1 : 0, calls.Count);
        notifications.Dispose();
        Assert.Equal(0, cleared);
    });

    [Fact]
    public void MissingWindowNeverCallsNative() => AvaloniaTestDispatcher.Run(() =>
    {
        using var notifications = new WindowsNotifications(IntPtr.Zero, 7, (_, _) => throw new Exception(), () => throw new Exception());
        Assert.False(notifications.Show("Title", "Body"));
    });

    [Fact]
    public void PinnedAvaloniaExposesExpectedTrayIdentityFields()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.NotNull(typeof(TrayIcon).GetField("_impl", instance));
        var assembly = Assembly.Load("Avalonia.Win32");
        var tray = assembly.GetType("Avalonia.Win32.TrayIconImpl", true)!;
        Assert.Equal(typeof(int), tray.GetField("_uniqueId", instance)!.FieldType);
        Assert.Equal(typeof(bool), tray.GetField("_iconAdded", instance)!.FieldType);
        Assert.Equal(typeof(bool), tray.GetField("_disposedValue", instance)!.FieldType);
        var platform = assembly.GetType("Avalonia.Win32.Win32Platform", true)!;
        Assert.NotNull(platform.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic));
        Assert.Equal(typeof(IntPtr), platform.GetProperty("Handle", instance)!.PropertyType);
    }

    [Fact]
    public void DismissClearsTheBalloonAndReleasesTheReference() => AvaloniaTestDispatcher.Run(() =>
    {
        var field = typeof(OsNotifications).GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var cleared = 0;
        using var notifications = new WindowsNotifications((IntPtr)42, 7, (_, _) => true, () => cleared++);
        try
        {
            Assert.True(notifications.Show("Title", "Body"));
            field.SetValue(null, notifications);
            OsNotifications.DismissWindowsBalloon();
            Assert.Equal(1, cleared);
            Assert.Null(field.GetValue(null));
            OsNotifications.DismissWindowsBalloon();
            Assert.Equal(1, cleared);
        }
        finally { field.SetValue(null, previous); }
    });

    [Fact]
    public void CleanupFailureDoesNotBlockRestore() => AvaloniaTestDispatcher.Run(() =>
    {
        var field = typeof(OsNotifications).GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var called = false;
        var notifications = new WindowsNotifications((IntPtr)42, 7, (_, _) => true, () => { called = true; throw new InvalidOperationException(); });
        try
        {
            Assert.True(notifications.Show("Title", "Body"));
            field.SetValue(null, notifications);
            OsNotifications.DismissWindowsBalloon();
            Assert.True(called);
            Assert.Null(field.GetValue(null));
        }
        finally { field.SetValue(null, previous); }
    });

    [Fact]
    public void NoTrayUsesFallbackWithoutNativeCalls() => Assert.Null(WindowsNotifications.Create(null));

    [Fact]
    public void NonWindowsTrayUsesFallback() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        using var tray = new TrayIcon { IsVisible = true };
        Assert.Null(WindowsNotifications.Create(tray));
    });
}
