using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Input;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WaylandCameraHotkeyTests
{
    [Fact]
    public void PreferredTrigger_UsesPortalSyntaxAndStoredKey()
    {
        Assert.Equal("CTRL+ALT+SHIFT+LOGO+j", PortalCameraHotkey.PreferredTrigger(
            ChordMods.Ctrl | ChordMods.Alt | ChordMods.Shift | ChordMods.Win, "J"));
        Assert.Equal("CTRL+1", PortalCameraHotkey.PreferredTrigger(ChordMods.Ctrl, "D1"));
    }

    [Fact]
    public void Activated_RoutesOnlyThisSessionAndSuppressesHeldRepeats()
    {
        int presses = 0;
        using var portal = new PortalCameraHotkey(() => presses++);
        portal.SetSessionForTests("/session/camera");
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        portal.Route("/session/other", "camera", true, start);
        portal.Route("/session/camera", "other", true, start);
        portal.Route("/session/camera", "camera", true, start);
        // Sustained repeats refresh the gap and do not toggle the camera.
        portal.Route("/session/camera", "camera", true, start.AddSeconds(1));
        portal.Route("/session/camera", "camera", true, start.AddSeconds(2));
        Assert.Equal(1, presses);
        // A lost Deactivated must not make all subsequent presses disappear.
        var afterGap = start.AddSeconds(2).Add(PanicKeyHold.RepeatGap).AddTicks(1);
        portal.Route("/session/camera", "camera", true, afterGap);
        Assert.Equal(2, presses);
        portal.Route("/session/camera", "camera", false, afterGap);
        portal.Route("/session/camera", "camera", true, afterGap);
        Assert.Equal(3, presses);
        portal.Dispose();
        portal.Route("/session/camera", "camera", false, afterGap);
        portal.Route("/session/camera", "camera", true, afterGap);
        Assert.Equal(3, presses);
        Assert.False(portal.IsBound);
    }

    [Fact]
    public async Task Sandbox_NeverOpensSessionBus()
    {
        var old = PortalCameraHotkey.OpenConnection;
        int opens = 0;
        PortalCameraHotkey.OpenConnection = () => { opens++; throw new InvalidOperationException(); };
        try
        {
            Assert.True(SandboxMarker.Active);
            using var portal = new PortalCameraHotkey(() => { });
            await portal.BindAsync(ChordMods.Ctrl, "K", "camera");
            Assert.Equal(0, opens);
            Assert.False(portal.IsBound);
        }
        finally { PortalCameraHotkey.OpenConnection = old; }
    }

    [Fact]
    public void Wayland_UsesPortalAndRebindsAndReleases() => AvaloniaTestDispatcher.Run(() =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var settings = CoreSettings.Current.CompanionPrompt;
        var old = (settings.CameraShortcutGlobal, settings.CameraShortcutKey, settings.CameraShortcutModifiers);
        var shell = new MainShellWindow();
        var sessions = new List<PortalCameraHotkey>();
        var chords = new List<(ChordMods, string)>();
        int x11 = 0;
        shell.RegisterCameraHotkey = (_, _, _, _) => { x11++; return null; };
        shell.RegisterPortalCamera = (mods, key, pressed) =>
        {
            chords.Add((mods, key));
            var portal = new PortalCameraHotkey(pressed);
            portal.SetSessionForTests("/session/camera");
            sessions.Add(portal);
            return portal;
        };
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
            (settings.CameraShortcutGlobal, settings.CameraShortcutKey, settings.CameraShortcutModifiers) = (true, "J", "Control,Shift");
            shell.ApplyCameraHotkey();
            Assert.True(shell.CameraHotkeyArmed);
            Assert.Equal((ChordMods.Ctrl | ChordMods.Shift, "J"), chords[0]);
            Assert.Equal(0, x11);
            shell.ApplyCameraHotkey();
            Assert.Single(sessions);
            settings.CameraShortcutKey = "K";
            shell.ApplyCameraHotkey();
            Assert.False(sessions[0].IsBound);
            Assert.True(sessions[1].IsBound);
            settings.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            Assert.False(sessions[1].IsBound);
            Assert.False(shell.CameraHotkeyArmed);
            settings.CameraShortcutGlobal = true;
            shell.ApplyCameraHotkey();
            Assert.True(sessions[2].IsBound);
            shell.StopQuickRecalHotkey();
            Assert.False(sessions[2].IsBound);
        }
        finally
        {
            shell.StopQuickRecalHotkey(); shell.Close();
            (settings.CameraShortcutGlobal, settings.CameraShortcutKey, settings.CameraShortcutModifiers) = old;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
        }
    });
    [Fact]
    public Task Activated_TogglesCameraWithTwoSecondToasts() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var settings = CoreSettings.Current;
        var old = (settings.CompanionPrompt.CameraShortcutGlobal, settings.WebcamConsentGiven,
            settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow();
        var host = new StackPanel();
        ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
        PortalCameraHotkey? portal = null;
        shell.RegisterPortalCamera = (_, _, pressed) =>
        {
            portal = new PortalCameraHotkey(pressed);
            portal.SetSessionForTests("/session/camera");
            return portal;
        };
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
            settings.CompanionPrompt.CameraShortcutGlobal = true;
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = (true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            shell.ApplyCameraHotkey();
            Assert.NotNull(portal);
            portal.Route("/session/camera", "camera", true);
            portal.Route("/session/camera", "camera", true);
            for (int i = 0; i < 100 && !tracker.IsRunning; i++) await Task.Delay(20);
            Assert.True(tracker.IsRunning, tracker.LastError);
            for (int i = 0; i < 100 && host.Children.Count < 1; i++) await Task.Delay(20);
            AssertToast("camera_shortcut_toast_started");
            portal.Route("/session/camera", "camera", false);
            portal.Route("/session/camera", "camera", true);
            for (int i = 0; i < 100 && (tracker.IsRunning || host.Children.Count < 2); i++) await Task.Delay(20);
            Assert.False(tracker.IsRunning);
            AssertToast("camera_shortcut_toast_stopped");
            void AssertToast(string key)
            {
                var toast = Assert.IsType<Border>(host.Children[^1]);
                var grid = Assert.IsType<Grid>(toast.Child);
                Assert.Equal(Loc.Get(key), Assert.IsType<TextBlock>(grid.Children[0]).Text);
                Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<global::Avalonia.Threading.DispatcherTimer>(toast.Tag).Interval);
            }
        }
        finally
        {
            shell.StopQuickRecalHotkey(); shell.Close();
            await tracker.StopAsync();
            (settings.CompanionPrompt.CameraShortcutGlobal, settings.WebcamConsentGiven,
                settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
        }
    });

    [Fact]
    public Task QueuedActivation_DoesNotToggleAfterGlobalIsDisabled() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var settings = CoreSettings.Current;
        var old = (settings.CompanionPrompt.CameraShortcutGlobal, settings.WebcamConsentGiven,
            settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow();
        PortalCameraHotkey? portal = null;
        shell.RegisterPortalCamera = (_, _, pressed) =>
        {
            portal = new PortalCameraHotkey(pressed);
            portal.SetSessionForTests("/session/camera");
            return portal;
        };
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
            settings.CompanionPrompt.CameraShortcutGlobal = true;
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion) = (true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            shell.ApplyCameraHotkey();
            Assert.NotNull(portal);
            portal.Route("/session/camera", "camera", true);
            settings.CompanionPrompt.CameraShortcutGlobal = false;
            await Task.Delay(1500);
            Assert.False(tracker.IsRunning);
        }
        finally
        {
            shell.StopQuickRecalHotkey(); shell.Close();
            await tracker.StopAsync();
            (settings.CompanionPrompt.CameraShortcutGlobal, settings.WebcamConsentGiven,
                settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
        }
    });

    private sealed class Registration : IDisposable { public void Dispose() { } }

    [Fact]
    public void Wayland_HidesHintEvenWhenCameraPortalAndX11RecalAreArmed() => AvaloniaTestDispatcher.Run(() =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var settings = CoreSettings.Current;
        var old = (settings.WebcamQuickRecalHotkeyEnabled, settings.PanicKeyEnabled, settings.PanicKey,
            settings.PauseKey, settings.CompanionPrompt.CameraShortcutGlobal);
        var shell = new MainShellWindow { QuickRecalPlatformSupported = true };
        shell.RegisterQuickRecal = (_, _) => new Registration();
        shell.RegisterPortalCamera = (_, _, pressed) =>
        {
            var portal = new PortalCameraHotkey(pressed);
            portal.SetSessionForTests("/session/camera");
            return portal;
        };
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
            (settings.WebcamQuickRecalHotkeyEnabled, settings.PanicKeyEnabled, settings.PanicKey,
                settings.PauseKey, settings.CompanionPrompt.CameraShortcutGlobal) = (true, true, "Escape", "", true);
            shell.StartQuickRecalHotkey();
            Assert.True(shell.CameraHotkeyArmed);
            Assert.True(shell.QuickRecalHotkeyArmed);
            Assert.Null(WebcamQuickRecalWindow.HotkeyHint());
        }
        finally
        {
            shell.StopQuickRecalHotkey(); shell.Close();
            (settings.WebcamQuickRecalHotkeyEnabled, settings.PanicKeyEnabled, settings.PanicKey,
                settings.PauseKey, settings.CompanionPrompt.CameraShortcutGlobal) = old;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
        }
    });

}
