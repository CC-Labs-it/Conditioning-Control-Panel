using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuickRecalHotkeyTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static void Init()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }


    [Fact]
    public Task Invocation_PreservesRunningTracker_AndRefusesASecondDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow();
        var held = new TaskCompletionSource();
        int dialogs = 0;
        shell.ShowQuickRecal = (_, owner) => { Assert.Null(owner); dialogs++; return held.Task; };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            tracker.Calibration = new WebcamCalibrationData();
            Assert.True(await tracker.StartAsync(), tracker.LastError);
            var first = shell.OpenQuickRecalFromHotkeyAsync();
            Assert.Equal(1, dialogs);
            await shell.OpenQuickRecalFromHotkeyAsync();
            Assert.Equal(1, dialogs);
            held.SetResult();
            await first;
            Assert.True(tracker.IsRunning); // drift correction must not tear down the running session
        }
        finally
        {
            held.TrySetResult();
            await tracker.StopAsync();
            tracker.Calibration = oldCal;
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public Task Invocation_StopsItsOwnTrackerEvenWhenTheDialogFails() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow();
        int dialogs = 0;
        shell.ShowQuickRecal = (dialog, _) =>
        {
            dialogs++;
            dialog.Close();
            return Task.FromException(new InvalidOperationException("synthetic dialog failure"));
        };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            tracker.Calibration = new WebcamCalibrationData();
            Assert.False(tracker.IsRunning);
            await shell.OpenQuickRecalFromHotkeyAsync();
            Assert.Equal(1, dialogs);
            Assert.False(tracker.IsRunning);
        }
        finally
        {
            await tracker.StopAsync();
            tracker.Calibration = oldCal;
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void Panic_ClosesQuickRecalEvenWhenTrackingWasAlreadyStopped() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var dialog = new WebcamQuickRecalWindow();
        try
        {
            dialog.Show();
            Assert.True(dialog.IsVisible);
            PanicSurfaces.StopAll("quick-recal regression");
            Assert.False(dialog.IsVisible);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void Binding_DisarmsForSettingAndBothModifierBlindHookClashes() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow();
        var registration = new Registration();
        int attempts = 0;
        shell.RegisterQuickRecal = (_, _) => { attempts++; return registration = new Registration(); };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, true, "Escape", "");
            shell.ApplyQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            shell.ApplyQuickRecalHotkey();
            Assert.Equal(1, attempts);
            s.PanicKey = "G";
            shell.ApplyQuickRecalHotkey();
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
            s.PanicKeyEnabled = false;
            shell.ApplyQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            s.PauseKey = " g ";
            shell.ApplyQuickRecalHotkey();
            Assert.False(shell.QuickRecalHotkeyArmed);
            s.PauseKey = "";
            s.WebcamQuickRecalHotkeyEnabled = false;
            shell.ApplyQuickRecalHotkey();
            Assert.False(shell.QuickRecalHotkeyArmed);
            Assert.Equal(2, attempts);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public void RegistrationFailure_IsNonfatal_AndShutdownReleasesOnlyItsSlot() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow();
        var registration = new Registration();
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, true, "Escape", "");
            shell.RegisterQuickRecal = (_, _) => null;
            shell.ApplyQuickRecalHotkey();
            Assert.False(shell.QuickRecalHotkeyArmed);
            s.WebcamQuickRecalHotkeyEnabled = false;
            shell.ApplyQuickRecalHotkey();
            s.WebcamQuickRecalHotkeyEnabled = true;
            shell.RegisterQuickRecal = (_, _) => registration;
            shell.ApplyQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            shell.StopQuickRecalHotkey();
            shell.StopQuickRecalHotkey();
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public Task Invocation_RechecksLiveHookClash_AndNeverPromptsForConsent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var shell = new MainShellWindow();
        var registration = new Registration();
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        int dialogs = 0;
        shell.RegisterQuickRecal = (_, _) => registration;
        shell.ShowQuickRecal = (_, _) => { dialogs++; return Task.CompletedTask; };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, true, "Escape", "");
            tracker.Calibration = new WebcamCalibrationData();
            shell.ApplyQuickRecalHotkey();
            s.PanicKey = "G"; // settings can change without passing through the registration path
            await shell.OpenQuickRecalFromHotkeyAsync();
            Assert.True(registration.Disposed);
            s.PanicKey = "Escape";
            s.WebcamConsentGiven = false;
            await shell.OpenQuickRecalFromHotkeyAsync();
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = "old";
            await shell.OpenQuickRecalFromHotkeyAsync();
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            tracker.Calibration = null;
            await shell.OpenQuickRecalFromHotkeyAsync();
            Assert.Equal(0, dialogs);
            Assert.False(tracker.IsRunning);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });

    [Fact]
    public Task MinimizedShell_DoesNotOwnTheDialog_ShownShellDoes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow();
        var owners = new System.Collections.Generic.List<WindowBase?>();
        // The real show path; the effective Owner is read before the dialog is closed again.
        shell.ShowQuickRecal = (dialog, owner) =>
        {
            var shown = MainShellWindow.ShowQuickRecalDialog(dialog, owner);
            owners.Add(owner);
            owners.Add(dialog.Owner);
            dialog.Close();
            return shown;
        };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            tracker.Calibration = new WebcamCalibrationData();
            Assert.True(await tracker.StartAsync(), tracker.LastError);
            shell.Show();
            shell.WindowState = WindowState.Minimized;
            Assert.True(shell.IsVisible); // a minimized shell is still "visible"; only WindowState says otherwise
            await shell.OpenQuickRecalFromHotkeyAsync();
            shell.WindowState = WindowState.Normal;
            await shell.OpenQuickRecalFromHotkeyAsync();
            Assert.Equal(4, owners.Count);
            Assert.Null(owners[0]);
            Assert.Null(owners[1]);
            Assert.Same(shell, owners[2]);
            Assert.Same(shell, owners[3]);
        }
        finally
        {
            await tracker.StopAsync();
            tracker.Calibration = oldCal;
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void StartQuickRecalHotkey_ArmsOnlyWhereSupported_AndShellCloseReleasesIt() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow();
        var registration = new Registration();
        int attempts = 0;
        shell.RegisterQuickRecal = (_, _) => { attempts++; return registration; };
        var camera = new Registration();
        shell.RegisterCameraHotkey = (_, _, _, _) => camera;
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, true, "Escape", "");
            shell.Show();
            shell.QuickRecalPlatformSupported = false;
            shell.StartQuickRecalHotkey();
            Assert.Equal(0, attempts);
            shell.QuickRecalPlatformSupported = true;
            shell.StartQuickRecalHotkey();
            shell.StartQuickRecalHotkey(); // idempotent: one watch, one registration
            Assert.Equal(1, attempts);
            Assert.True(shell.QuickRecalHotkeyArmed);
            Assert.True(shell.CameraHotkeyArmed);
            shell.Close();
            Assert.True(registration.Disposed);
            Assert.True(camera.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public void X11Grabs_MatchKeycodeAndChordModifiers_IgnoringLockKeys()
    {
        Action recal = () => { }, camera = () => { };
        var grabs = new[] { (42, 4u | 8u, recal), (45, 4u | 8u, camera) };
        Assert.Same(recal, X11SummonChord.Match(grabs, 42, 4 | 8));
        Assert.Same(recal, X11SummonChord.Match(grabs, 42, 4 | 8 | 2 | 16)); // Caps + Num Lock on
        Assert.Same(camera, X11SummonChord.Match(grabs, 45, 4 | 8));
        Assert.Null(X11SummonChord.Match(grabs, 42, 4 | 8 | 1)); // Shift added: not this chord
        Assert.Null(X11SummonChord.Match(grabs, 43, 4 | 8));
    }

    [Fact]
    public void CameraHotkey_ArmsTheUsersChord_GlobalOffDisarms_BadKeyFallsBackToK() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var p = CoreSettings.Current.CompanionPrompt;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal);
        var shell = new MainShellWindow();
        var registration = new Registration();
        (ChordMods Mods, string Key)? armed = null;
        shell.RegisterCameraHotkey = (_, m, k, _) => { armed = (m, k); return registration = new Registration(); };
        try
        {
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal) = ("J", "Control,Shift", true);
            shell.ApplyCameraHotkey();
            Assert.Equal((ChordMods.Ctrl | ChordMods.Shift, "J"), armed);
            Assert.Equal("Ctrl+Shift+J", MainShellWindow.CameraShortcutChord);
            Assert.True(shell.CameraHotkeyArmed);
            p.CameraShortcutGlobal = false;
            shell.ApplyCameraHotkey();
            Assert.True(registration.Disposed);
            Assert.False(shell.CameraHotkeyArmed);
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal) = ("NotAKey", "", true);
            shell.ApplyCameraHotkey();
            Assert.Equal((ChordMods.Ctrl | ChordMods.Alt, "K"), armed);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            (p.CameraShortcutKey, p.CameraShortcutModifiers, p.CameraShortcutGlobal) = old;
        }
    });

    [Fact]
    public Task CameraHotkey_TogglesTheTracker_AndAStaleConsentAsksFirst() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow();
        int asks = 0;
        var toastHost = new StackPanel();
        ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(toastHost);
        bool grant = false;
        shell.ShowCameraConsent = _ =>
        {
            asks++;
            if (grant) (s.WebcamConsentGiven, s.WebcamConsentVersion) = (true, WebcamConsent.ConsentVersion);
            return Task.FromResult(grant);
        };
        try
        {
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            (s.WebcamConsentGiven, s.WebcamConsentVersion) = (false, "");
            await shell.ToggleWebcamFromHotkeyAsync();
            Assert.Equal(1, asks);
            Assert.False(tracker.IsRunning); // refused consent never starts the camera
            grant = true;
            await shell.ToggleWebcamFromHotkeyAsync();
            Assert.Equal(2, asks);
            Assert.True(tracker.IsRunning, tracker.LastError);
            void AssertToast(string key)
            {
                var toast = Assert.IsType<Border>(toastHost.Children[^1]);
                var grid = Assert.IsType<Grid>(toast.Child);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get(key), Assert.IsType<TextBlock>(grid.Children[0]).Text);
                Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<global::Avalonia.Threading.DispatcherTimer>(toast.Tag).Interval);
            }
            AssertToast("camera_shortcut_toast_started");
            await shell.ToggleWebcamFromHotkeyAsync();
            Assert.Equal(2, asks);
            Assert.False(tracker.IsRunning);
            AssertToast("camera_shortcut_toast_stopped");
        }
        finally
        {
            await tracker.StopAsync();
            shell.Close();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Theory]
    [InlineData(null)]
    [InlineData("wayland-0")]
    public void QuickRecalHint_NamesBothChords_OnlyWhileBothAreArmed(string? waylandDisplay) => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current;
        var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var shell = new MainShellWindow();
        shell.RegisterQuickRecal = (_, _) => new Registration();
        shell.RegisterCameraHotkey = (_, _, _, _) => null;
        static TextBlock Hint() => new WebcamQuickRecalWindow().FindControl<TextBlock>("TxtHotkeyHint")!;
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", waylandDisplay);
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, true, "Escape", "");
            shell.QuickRecalPlatformSupported = true;
            shell.StartQuickRecalHotkey();
            Assert.False(Hint().IsVisible); // camera chord refused: the two-chord line would lie
            shell.StopQuickRecalHotkey();

            var shell2 = new MainShellWindow();
            shell2.RegisterQuickRecal = (_, _) => new Registration();
            shell2.RegisterCameraHotkey = (_, _, _, _) => new Registration();
            try
            {
                shell2.StartQuickRecalHotkey();
                var hint = Hint();
                Assert.Equal(!OperatingSystem.IsLinux() || waylandDisplay == null, hint.IsVisible);
                if (hint.IsVisible)
                {
                    Assert.Contains("Ctrl+Alt+G", hint.Text);
                    Assert.Contains(MainShellWindow.CameraShortcutChord, hint.Text);
                }
            }
            finally { shell2.StopQuickRecalHotkey(); shell2.Close(); }
            Assert.False(Hint().IsVisible);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
            shell.StopQuickRecalHotkey();
            shell.Close();
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
    });

    [Fact]
    public Task CameraHotkey_InWindowFallback_RestoresShell_AndDoesNotStackConsent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = false;
        s.CompanionPrompt.CameraShortcutGlobal = false;
        s.CompanionPrompt.CameraShortcutKey = "K";
        s.CompanionPrompt.CameraShortcutModifiers = "Control,Alt";
        var shell = new MainShellWindow();
        shell.QuickRecalPlatformSupported = true;
        shell.RegisterQuickRecal = (_, _) => null;
        var held = new TaskCompletionSource<bool>();
        int asks = 0;
        shell.ShowCameraConsent = owner =>
        {
            Assert.Same(shell, owner);
            Assert.True(shell.IsVisible);
            Assert.NotEqual(WindowState.Minimized, shell.WindowState);
            asks++;
            return held.Task;
        };
        try
        {
            shell.StartQuickRecalHotkey();
            shell.WindowState = WindowState.Minimized;
            global::Avalonia.Input.KeyEventArgs Press(global::Avalonia.Input.KeyModifiers mods)
            {
                var e = new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
                    Key = global::Avalonia.Input.Key.K, KeyModifiers = mods };
                shell.RaiseEvent(e);
                return e;
            }
            Press(global::Avalonia.Input.KeyModifiers.Control);
            Assert.Equal(0, asks);
            Assert.True(Press(global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Alt).Handled);
            Assert.Equal(1, asks);
            await shell.ToggleWebcamFromHotkeyAsync();
            Assert.Equal(1, asks);
            held.SetResult(false);
            Assert.False(WebcamTracker.Instance.IsRunning);
        }
        finally { held.TrySetResult(false); shell.StopQuickRecalHotkey(); shell.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CameraHotkey_ClosingAfterEnable_DoesNotStartUntilDialogFinished(bool finish) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        CoreSettings.Current.WebcamConsentGiven = false;
        WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
        var tracker = WebcamTracker.Instance;
        var shell = new MainShellWindow();
        try
        {
            var pending = shell.ToggleWebcamFromHotkeyAsync();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<WebcamConsentDialog>());
            void Click(string name) => dialog.FindControl<Button>(name)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Click("BtnNext"); Click("BtnNext");
            foreach (var name in new[] { "ChkConsent1", "ChkConsent2", "ChkConsent3" }) dialog.FindControl<CheckBox>(name)!.IsChecked = true;
            dialog.FindControl<TextBox>("TxtConfirm")!.Text = "ENABLE";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.FindControl<Button>("BtnEnable")!.IsEnabled);
            Click("BtnEnable");
            Assert.True(dialog.ConsentGiven);
            Assert.True(WebcamConsent.IsCurrent(CoreSettings.Current));
            Assert.False(tracker.IsRunning);
            if (finish) Click("BtnSkipCal"); else dialog.Close();
            await pending;
            Assert.Equal(finish, tracker.IsRunning);
        }
        finally { await tracker.StopAsync(); shell.Close(); }
    });

    [Fact]
    public void X11HeldChord_FiresOnceUntilGenuineRelease()
    {
        int toggles = 0;
        var grabs = new[] { (45, 12u, (Action)(() => toggles++)) };
        var held = new HashSet<int>();
        void Send(int type, long time, (int, int, long) next = default) =>
            X11SummonChord.MatchKeyEvent(grabs, held, type, 45, 12, time, next)?.Invoke();
        Send(2, 100); Send(2, 200); Send(2, 300); // detectable repeat: repeated presses only
        Assert.Equal(1, toggles);
        Send(3, 400, (2, 45, 400)); Send(2, 400); // legacy synthetic release/press pair
        Assert.Equal(1, toggles);
        Send(3, 500, (2, 45, 501)); Send(2, 501); // genuine release followed by a new press
        Assert.Equal(2, toggles);
        Send(3, 600); Send(2, 700);
        Assert.Equal(3, toggles);
    }

}
