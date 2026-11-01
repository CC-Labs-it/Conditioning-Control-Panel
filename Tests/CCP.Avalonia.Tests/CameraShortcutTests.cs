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
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Models;
using System.Runtime.InteropServices;
using System.Threading;
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
            Assert.Equal("Shift+Win+F7", MainShellWindow.FormatCameraShortcut()); shell.ApplyCapturedCameraShortcut(Key.F7, KeyModifiers.Shift | KeyModifiers.Meta, true, false); Assert.True(registrations[1].Disposed);
            s.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            Assert.True(registrations[2].Disposed);
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(1, toggles);
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(1, toggles); // a held key does not immediately undo the first toggle
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7, KeyModifiers = KeyModifiers.Shift | KeyModifiers.Meta });
            Assert.Equal(2, toggles);
            Assert.Equal(3, chords.Count);
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
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(1, toggles);
            s.CameraShortcutGlobal = true;
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
    public Task ReplacedPrompt_RebindsAndRejectsStaleOrUnsafePresses() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init(); var s = CoreSettings.Current; var old = (s.CompanionPrompt, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow(); var registrations = new System.Collections.Generic.List<Registration>();
        var callbacks = new System.Collections.Generic.List<Action>(); var keys = new System.Collections.Generic.List<Key>(); int toggles = 0;
        shell.ToggleCameraForTest = () => { toggles++; return Task.CompletedTask; };
        shell.RegisterCameraHotkey = (_, k, _, a) => { keys.Add(k); callbacks.Add(a); var r = new Registration(); registrations.Add(r); return r; };
        try
        {
            s.CompanionPrompt = new CompanionPromptSettings(); s.PanicKeyEnabled = true; s.PanicKey = "Escape"; s.PauseKey = "";
            shell.StartCameraHotkey();
            s.CompanionPrompt = new CompanionPromptSettings { CameraShortcutKey = "F7", CameraShortcutGlobal = true };
            callbacks[0](); await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(0, toggles); await Task.Delay(2100); Assert.True(registrations[0].Disposed); Assert.Equal(Key.F7, keys[1]);
            s.CompanionPrompt = new CompanionPromptSettings { CameraShortcutKey = "Oem3", CameraShortcutGlobal = true };
            await Task.Delay(2100); Assert.Equal(3, registrations.Count); Assert.Equal(Key.Oem3, keys[2]);
            s.PanicKey = "OemTilde"; callbacks[2]();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(0, toggles); await Task.Delay(2100); Assert.True(registrations[2].Disposed);
            s.PanicKey = "Escape"; await Task.Delay(2100); Assert.Equal(4, registrations.Count);
            s.CompanionPrompt.CameraShortcutKey = "F8"; await Task.Delay(2100); Assert.Equal(Key.F8, keys[^1]);
        }
        finally { shell.StopCameraHotkey(); shell.Close(); (s.CompanionPrompt, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old; }
    });

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong symbol);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint code, bool down, ulong delay);

    [Theory]
    [InlineData("OemBackslash", "less")] [InlineData("Oem102", "less")]
    [InlineData("BrowserBack", "XF86Back")] [InlineData("BrowserForward", "XF86Forward")] [InlineData("BrowserRefresh", "XF86Reload")]
    [InlineData("BrowserStop", "XF86Stop")] [InlineData("BrowserSearch", "XF86Search")] [InlineData("BrowserFavorites", "XF86Favorites")] [InlineData("BrowserHome", "XF86HomePage")]
    [InlineData("VolumeMute", "XF86AudioMute")] [InlineData("VolumeDown", "XF86AudioLowerVolume")] [InlineData("VolumeUp", "XF86AudioRaiseVolume")]
    [InlineData("MediaNextTrack", "XF86AudioNext")] [InlineData("MediaPreviousTrack", "XF86AudioPrev")] [InlineData("MediaStop", "XF86AudioStop")] [InlineData("MediaPlayPause", "XF86AudioPlay")]
    [InlineData("LaunchMail", "XF86Mail")] [InlineData("SelectMedia", "XF86AudioMedia")] [InlineData("LaunchApplication1", "XF86Launch1")] [InlineData("LaunchApplication2", "XF86Launch2")] [InlineData("Apps", "Menu")] [InlineData("Sleep", "XF86Sleep")] [InlineData("Separator", "KP_Separator")]
    public void X11CapturedKeyMapsAndGrabs(string key, string symbol)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.Equal(symbol, X11PanicKey.XKeyName(key)); Assert.NotEqual(0UL, X11PanicKey.KeysymOf(key));
        if (Environment.GetEnvironmentVariable("CCP_TEST_X11") != "1" || key is not ("OemBackslash" or "BrowserBack")) return;
        var d = XOpenDisplay(IntPtr.Zero); Assert.NotEqual(IntPtr.Zero, d); using var pressed = new ManualResetEventSlim();
        var code = XKeysymToKeycode(d, X11PanicKey.KeysymOf(key)); var ctrl = XKeysymToKeycode(d, X11PanicKey.KeysymOf("Control_L"));
        try
        {
            Assert.NotEqual(0, code); Assert.NotEqual(0, ctrl); Assert.True(X11SummonChord.Arm("camera", ChordMods.Ctrl, key, pressed.Set));
            XTestFakeKeyEvent(d, ctrl, true, 0); XTestFakeKeyEvent(d, code, true, 0); XSync(d, false); Assert.True(pressed.Wait(TimeSpan.FromSeconds(3)));
        }
        finally { XTestFakeKeyEvent(d, code, false, 0); XTestFakeKeyEvent(d, ctrl, false, 0); XSync(d, false); X11SummonChord.Disarm("camera"); XCloseDisplay(d); }
    }

    [Theory]
    [InlineData("K", "K", true)]
    [InlineData("Oem3", "OemTilde", true)]
    [InlineData("OemTilde", "Oem3", false)]
    [InlineData("Return", "Enter", true)]
    public void ClashWithPanicKey_DoesNotRegisterOrToggle(string camera, string bound, bool panic) => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var p = s.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow();
        int registrations = 0, toggles = 0;
        shell.RegisterCameraHotkey = (_, _, _, _) => { registrations++; return new Registration(); };
        shell.ToggleCameraForTest = () => { toggles++; return Task.CompletedTask; };
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (camera, "Control,Alt", true, panic, panic ? bound : "Escape", panic ? "" : bound);
            shell.StartCameraHotkey();
            Assert.True(HotkeyNames.TryParse(camera, out var key));
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
            Assert.Equal(0, registrations);
            Assert.Equal(0, toggles);
        }
        finally
        {
            shell.StopCameraHotkey(); shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });
}
