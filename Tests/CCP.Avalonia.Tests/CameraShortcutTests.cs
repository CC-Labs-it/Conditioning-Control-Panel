using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CameraShortcutTests
{
    private static void Init()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
    }

    private sealed class Registration : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Binding_RebindsAndDisarmsWithoutLosingTheInWindowShortcut() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal);
        var shell = new MainShellWindow();
        var registrations = new System.Collections.Generic.List<Registration>();
        var chords = new System.Collections.Generic.List<(Key, KeyModifiers)>();
        int toggles = 0;
        shell.ToggleCameraForTest = () => { toggles++; return Task.CompletedTask; };
        shell.RegisterCameraHotkey = (_, key, mods, _) => { chords.Add((key, mods)); var r = new Registration(); registrations.Add(r); return r; };
        try
        {
            (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal) = ("K", "Control,Alt", true);
            shell.StartCameraHotkey();
            Assert.Equal((Key.K, KeyModifiers.Control | KeyModifiers.Alt), chords[0]);
            shell.ApplyCapturedCameraShortcut(Key.F7, KeyModifiers.Shift | KeyModifiers.Meta, true, false);
            Assert.Equal("F7", s.CameraShortcutKey);
            Assert.Equal("Shift,Windows", s.CameraShortcutModifiers);
            Assert.True(registrations[0].Disposed);
            Assert.Equal((Key.F7, KeyModifiers.Shift | KeyModifiers.Meta), chords[1]);
            Assert.Equal("Shift+Win+F7", MainShellWindow.FormatCameraShortcut());
            s.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            Assert.True(registrations[1].Disposed);
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(1, toggles);
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(1, toggles); // a held key does not immediately undo the first toggle
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(2, toggles);
            Assert.Equal(2, chords.Count);
            shell.ApplyCapturedCameraShortcut(Key.None, KeyModifiers.None, false, true);
            Assert.Equal("K", s.CameraShortcutKey);
            Assert.Equal("Control,Alt", s.CameraShortcutModifiers);
            Assert.Equal("Ctrl+Alt+K", MainShellWindow.FormatCameraShortcut());
        }
        finally
        {
            shell.StopCameraHotkey(); shell.Close();
            (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal) = old;
        }
    });

    [Fact]
    public Task GlobalPress_IsDeliveredAndAnOldRegistrationCannotFireAfterDisable() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal);
        var shell = new MainShellWindow();
        Action? press = null;
        int toggles = 0;
        shell.RegisterCameraHotkey = (_, _, _, callback) => { press = callback; return new Registration(); };
        shell.ToggleCameraForTest = () => { toggles++; return Task.CompletedTask; };
        try
        {
            (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal) = ("K", "Control,Alt", true);
            shell.StartCameraHotkey();
            Assert.NotNull(press);
            press!();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(1, toggles);
            press();
            s.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(1, toggles);
        }
        finally
        {
            shell.StopCameraHotkey(); shell.Close();
            (s.CameraShortcutKey, s.CameraShortcutModifiers, s.CameraShortcutGlobal) = old;
        }
    });

    [Fact]
    public Task TrackerToggle_StopsRunningCamera_AndRefusedConsentNeverStartsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow();
        var page = shell.FindControl<BlinkTrainerTabView>("BlinkTrainerTab")!;
        int prompts = 0;
        page.ShowWebcamConsent = _ => { prompts++; return Task.CompletedTask; };
        try
        {
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            Assert.True(await tracker.StartAsync(), tracker.LastError);
            await shell.ToggleCameraFromHotkeyAsync();
            Assert.False(tracker.IsRunning);
            s.WebcamConsentGiven = false;
            await shell.ToggleCameraFromHotkeyAsync();
            Assert.Equal(1, prompts);
            Assert.False(tracker.IsRunning);
            page.ShowWebcamConsent = _ => { s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion; return Task.CompletedTask; };
            await shell.ToggleCameraFromHotkeyAsync();
            Assert.True(tracker.IsRunning);
        }
        finally
        {
            await tracker.StopAsync(); shell.StopCameraHotkey(); shell.Close();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void ClashWithPanicKey_DoesNotRegisterOrToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var p = s.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey);
        var shell = new MainShellWindow();
        int registrations = 0, toggles = 0;
        shell.RegisterCameraHotkey = (_, _, _, _) => { registrations++; return new Registration(); };
        shell.ToggleCameraForTest = () => { toggles++; return Task.CompletedTask; };
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey) = ("K", "Control,Alt", true, true, "K");
            shell.StartCameraHotkey();
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
            Assert.Equal(0, registrations);
            Assert.Equal(0, toggles);
        }
        finally
        {
            shell.StopCameraHotkey(); shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey) = old;
        }
    });
}
