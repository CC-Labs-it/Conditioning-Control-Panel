using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Webcam;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CameraHotkeyTests
{
    // Match the suite's headless font mapping on Linux, including PremiumSpark's display face.
    private static global::Avalonia.Media.FontManagerOptions HeadlessFonts()
    {
        var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
        var mappings = new Dictionary<string, global::Avalonia.Media.FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        return fonts;
    }

    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Registration_Rebinds_AndCleansUp() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(HeadlessFonts()).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var prompt = settings.CompanionPrompt;
        var old = (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers, prompt.CameraShortcutGlobal, settings.WebcamQuickRecalHotkeyEnabled);
        var shell = new MainShellWindow { CameraPlatformSupported = true };
        Registration? last = null;
        int calls = 0;
        shell.RegisterCamera = (_, key, mods, _) =>
        {
            calls++;
            Assert.Equal(calls == 1 ? "K" : calls == 3 ? "G" : "F5", key);
            Assert.Equal(calls == 1 || calls == 3 ? 3u : 4u, mods);
            return last = new Registration();
        };
        try
        {
            (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers, prompt.CameraShortcutGlobal, settings.WebcamQuickRecalHotkeyEnabled) = ("K", "Control,Alt", true, true);
            shell.StartCameraHotkey();
            Assert.True(shell.CameraHotkeyArmed);
            shell.ApplyCameraHotkey();
            Assert.Equal(1, calls);
            var first = last!;
            (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers) = ("F5", "Shift");
            shell.ApplyCameraHotkey();
            Assert.True(first.Disposed);
            Assert.Equal(2, calls);
            var second = last!;
            (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers) = ("G", "Control,Alt");
            shell.ApplyCameraHotkey();
            Assert.True(second.Disposed);
            Assert.True(shell.CameraHotkeyArmed);
            Assert.Equal(3, calls);
            var collision = last!;
            prompt.CameraShortcutKey = "F5";
            prompt.CameraShortcutModifiers = "Shift";
            shell.ApplyCameraHotkey();
            var third = last!;
            Assert.True(collision.Disposed);
            shell.StopCameraHotkey();
            Assert.True(third.Disposed);
            Assert.False(shell.CameraHotkeyArmed);
        }
        finally
        {
            shell.StopCameraHotkey();
            shell.Close();
            (prompt.CameraShortcutKey, prompt.CameraShortcutModifiers, prompt.CameraShortcutGlobal, settings.WebcamQuickRecalHotkeyEnabled) = old;
        }
    });
    [Fact]
    public void CameraWinsCollision_WhenQuickRecalRegistrationFails() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(HeadlessFonts()).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var p = s.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
            s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow { CameraPlatformSupported = true, QuickRecalPlatformSupported = true };
        int quickAttempts = 0;
        var camera = new Registration();
        shell.RegisterCamera = (_, key, mods, _) => { Assert.Equal("G", key); Assert.Equal(3u, mods); return camera; };
        shell.RegisterQuickRecal = (_, _) => { quickAttempts++; return null; };
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) =
                ("G", "Control,Alt", true, true, true, "Escape", "");
            shell.StartCameraHotkey();
            shell.StartQuickRecalHotkey();
            Assert.True(shell.CameraHotkeyArmed);
            Assert.False(shell.QuickRecalHotkeyArmed);
            Assert.Equal(1, quickAttempts);
            Assert.False(camera.Disposed);
        }
        finally
        {
            shell.StopCameraHotkey(); shell.StopQuickRecalHotkey(); shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public void RebindingToRecal_KeepsItsRegistrationAndCameraFails() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(HeadlessFonts()).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var p = s.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
            s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow { CameraPlatformSupported = true, QuickRecalPlatformSupported = true };
        var quick = new Registration();
        shell.RegisterQuickRecal = (_, _) => quick;
        shell.RegisterCamera = (_, key, _, _) => key == "G" && !quick.Disposed ? null : new Registration();
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) =
                ("K", "Control,Alt", true, true, true, "Escape", "");
            shell.StartCameraHotkey();
            shell.StartQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            p.CameraShortcutKey = "G";
            shell.ApplyCameraHotkey();
            Assert.False(quick.Disposed);
            Assert.True(shell.QuickRecalHotkeyArmed);
            Assert.False(shell.CameraHotkeyArmed);
        }
        finally
        {
            shell.StopCameraHotkey(); shell.StopQuickRecalHotkey(); shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public Task CallbackAndFallback_ToggleTrackerAndAnnounce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(HeadlessFonts()).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var p = s.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
            s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow { CameraPlatformSupported = true };
        Action? pressed = null;
        bool acceptRegistration = true;
        shell.RegisterCamera = (_, _, _, action) => { pressed = action; return acceptRegistration ? new Registration() : null; };
        var messages = new List<string>();
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamConsentGiven, s.WebcamConsentVersion) = ("F5", "Shift", true, true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            shell.Show();
            var tab = shell.FindControl<BlinkTrainerTabView>("BlinkTrainerTab")!;
            tab.AnnounceCamera = messages.Add;
            shell.StartCameraHotkey();
            async Task AwaitCount(int count)
            {
                for (int i = 0; i < 200 && messages.Count < count; i++) await Task.Delay(25);
                Assert.Equal(count, messages.Count);
            }
            pressed!();
            await AwaitCount(1);
            Assert.True(tracker.IsRunning);
            pressed!();
            await AwaitCount(2);
            Assert.False(tracker.IsRunning);
            Assert.Equal(new[] { "camera_shortcut_toast_started", "camera_shortcut_toast_stopped" }, messages);

            p.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            Assert.False(shell.CameraHotkeyArmed);
            shell.KeyPress(Key.F5, RawInputModifiers.Shift, PhysicalKey.F5, "");
            await AwaitCount(3);
            Assert.True(tracker.IsRunning);
            shell.KeyPress(Key.F5, RawInputModifiers.Shift, PhysicalKey.F5, "");
            await AwaitCount(4);
            Assert.False(tracker.IsRunning);

            acceptRegistration = false;
            p.CameraShortcutGlobal = true;
            shell.ApplyCameraHotkey();
            Assert.False(shell.CameraHotkeyArmed);
            shell.KeyPress(Key.F5, RawInputModifiers.Shift, PhysicalKey.F5, "");
            await AwaitCount(5);
            Assert.True(tracker.IsRunning);
            shell.KeyPress(Key.F5, RawInputModifiers.Shift, PhysicalKey.F5, "");
            await AwaitCount(6);
            Assert.False(tracker.IsRunning);

            // Ctrl+K belongs to the camera before the palette, both without a global
            // registration and when the OS rejects that registration.
            p.CameraShortcutKey = "K";
            p.CameraShortcutModifiers = "Control";
            p.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            shell.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            await AwaitCount(7);
            Assert.True(tracker.IsRunning);
            Assert.False(SettingsPaletteWindow.IsOpen);
            shell.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            await AwaitCount(8);
            Assert.False(tracker.IsRunning);
            p.CameraShortcutGlobal = true;
            shell.ApplyCameraHotkey();
            Assert.False(shell.CameraHotkeyArmed);
            shell.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            await AwaitCount(9);
            Assert.True(tracker.IsRunning);
            Assert.False(SettingsPaletteWindow.IsOpen);
            shell.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            await AwaitCount(10);
            Assert.False(tracker.IsRunning);
        }
        finally
        {
            await tracker.StopAsync();
            shell.StopCameraHotkey(); shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal,
                s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

}
