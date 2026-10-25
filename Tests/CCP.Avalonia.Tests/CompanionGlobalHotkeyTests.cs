using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionGlobalHotkeyTests
{
    [Fact]
    public void SlotsRebindIndependentlyFailureKeepsOtherSlotAndDisposeUnregisters() => AvaloniaTestDispatcher.Run(() =>
    {
        var registrations = new List<(int Id, uint Mods, uint Key)>();
        var removed = new List<int>();
        var accept = true;
        using var service = new Win32CompanionHotkeys((IntPtr)1,
            (_, id, mods, key) => { registrations.Add((id, mods, key)); return accept; },
            (_, id) => removed.Add(id));
        var chat = 0; var camera = 0;
        Assert.True(service.Set(Win32CompanionHotkeys.Chat, true, Key.T, KeyModifiers.Control, () => chat++));
        Assert.True(service.Set(Win32CompanionHotkeys.Camera, true, Key.K, KeyModifiers.Control | KeyModifiers.Alt, () => camera++));
        Assert.Equal((Win32CompanionHotkeys.Chat, 0x4002u, 0x54u), registrations[0]);
        Assert.Equal((Win32CompanionHotkeys.Camera, 0x4003u, 0x4Bu), registrations[1]);
        Assert.True(service.Dispatch(Win32CompanionHotkeys.Chat));
        Assert.Equal(0, chat); // native callback posts, never calls the UI directly
        Dispatcher.UIThread.RunJobs(); Assert.Equal(1, chat);
        accept = false;
        Assert.False(service.Set(Win32CompanionHotkeys.Chat, true, Key.J, KeyModifiers.Shift, () => chat += 10));
        Assert.False(service.Dispatch(Win32CompanionHotkeys.Chat));
        Assert.True(service.Dispatch(Win32CompanionHotkeys.Camera));
        Dispatcher.UIThread.RunJobs(); Assert.Equal(1, camera);
        Assert.False(service.Set(Win32CompanionHotkeys.Camera, false, Key.K, KeyModifiers.Alt, () => camera++));
        Assert.False(service.Dispatch(Win32CompanionHotkeys.Camera));
        Assert.Equal(new[] { Win32CompanionHotkeys.Chat, Win32CompanionHotkeys.Camera }, removed);
        accept = true;
        service.Set(Win32CompanionHotkeys.Chat, true, Key.T, KeyModifiers.Meta | KeyModifiers.Shift, () => chat++);
        Assert.Equal(0x400Cu, registrations[^1].Mods);
        service.Dispose(); service.Dispose();
        Assert.Equal(3, removed.Count);
        Assert.False(service.Dispatch(Win32CompanionHotkeys.Chat));
    });

    [Fact]
    public void IdleCadenceStartsAtOpenReReadsSettingsAndRestartAppliesImmediately() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var interval = CoreSettings.Current.IdleGiggleIntervalSeconds;
        var tube = new AvatarTubeWindow(null);
        try
        {
            CoreSettings.Current.IdleGiggleIntervalSeconds = 120;
            tube.Show();
            Assert.Equal(TimeSpan.FromSeconds(120), tube.IdleInterval);
            CoreSettings.Current.IdleGiggleIntervalSeconds = 20;
            tube.RestartIdleTimer();
            Assert.Equal(TimeSpan.FromSeconds(20), tube.IdleInterval);
            CoreSettings.Current.IdleGiggleIntervalSeconds = 300;
            tube.OnIdleTick(null, EventArgs.Empty);
            Assert.Equal(TimeSpan.FromSeconds(300), tube.IdleInterval);
        }
        finally { tube.Close(); CoreSettings.Current.IdleGiggleIntervalSeconds = interval; }
    });
}
