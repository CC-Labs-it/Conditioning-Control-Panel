using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
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
            shell.Close();
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
}
