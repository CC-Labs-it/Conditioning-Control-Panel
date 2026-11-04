using System;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CameraHotkeyTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Registration_Rebinds_ReservesRecal_AndCleansUp() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
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
            Assert.Equal(calls == 1 ? "K" : "F5", key);
            Assert.Equal(calls == 1 ? 3u : 4u, mods);
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
            Assert.False(shell.CameraHotkeyArmed);
            Assert.Equal(2, calls);
            prompt.CameraShortcutKey = "F5";
            prompt.CameraShortcutModifiers = "Shift";
            shell.ApplyCameraHotkey();
            var third = last!;
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
}
