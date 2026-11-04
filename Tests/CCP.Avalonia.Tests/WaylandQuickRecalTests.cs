using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WaylandQuickRecalTests
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

    [Theory]
    [InlineData("Ctrl+Alt+G", true)]
    [InlineData("Control + Alt + g", true)]
    [InlineData("Alt+Ctrl+G", true), InlineData("کنترل+دگرساز+G", true), InlineData("کنترل+دگرساز+K", false), InlineData("Ctrl+\u202aAlt+\u202aG", true), InlineData("Ctrl+\u202aAlt+\u202aK", false)]
    [InlineData("Strg+Alt+G", true), InlineData("Kontrol+Seçenek+G", true), InlineData("Kontrol+Seçenek+K", false)]
    [InlineData("Steuerung+Alt+G", true)]
    [InlineData("Strg+Alt+K", false)]
    [InlineData("Ctrl+Alt+K", false)]
    [InlineData("G", false)]
    [InlineData("", false)]
    [InlineData("Ctrl+Alt+Shift+G", false)]
    public void OnlyTheRecognizableWpfChordIsAccepted(string trigger, bool expected) =>
        Assert.Equal(expected, PortalQuickRecalHotkey.IsSafeTrigger(trigger));

    [Fact]
    public async Task SandboxDoesNotOpenThePortalConnection()
    {
        Assert.True(SandboxNet.Active);
        var original = PortalQuickRecalHotkey.OpenConnection; int attempts = 0;
        try
        {
            PortalQuickRecalHotkey.OpenConnection = () => { attempts++; throw new InvalidOperationException("Unexpected portal connection"); };
            Assert.Null(await PortalQuickRecalHotkey.BindAsync(() => { }, () => { }, CancellationToken.None));
            Assert.Equal(0, attempts);
        }
        finally { PortalQuickRecalHotkey.OpenConnection = original; }
    }

    [Fact]
    public void PortalSignalsAreScopedAndHeldRepeatsAreSuppressed()
    {
        int presses = 0, closed = 0;
        var binding = new PortalQuickRecalHotkey(() => presses++, () => closed++);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        binding.SetSessionForTests("/session");
        binding.ShortcutChanged("/other", "Ctrl+Alt+K");
        binding.ShortcutChanged("/session", "Ctrl+Alt+G"); binding.ShortcutChanged("/session", "Strg+Alt+G"); // initial portal notifications
        binding.Route("/session", "quick_recal", true, now); // not yet bound
        Assert.True(binding.Ready());
        binding.Route("/other", "quick_recal", true, now);
        binding.Route("/session", "other", true, now);
        Assert.Equal(0, presses);
        binding.Route("/session", "quick_recal", true, now);
        binding.Route("/session", "quick_recal", true, now.AddMilliseconds(100));
        Assert.Equal(1, presses);
        binding.Route("/session", "quick_recal", false, now.AddMilliseconds(150));
        binding.Route("/session", "quick_recal", true, now.AddMilliseconds(200));
        binding.Route("/session", "quick_recal", true, now.AddSeconds(2)); // lost release
        Assert.Equal(3, presses);
        binding.ShortcutChanged("/session", "Ctrl+Alt+K");
        binding.ShortcutChanged("/session", "Ctrl+Alt+K");
        binding.Route("/session", "quick_recal", true, now.AddSeconds(4));
        Assert.Equal(3, presses);
        Assert.Equal(1, closed);
    }

    [Theory, InlineData("Ctrl+Alt+K"), InlineData("Strg+Alt+K")]
    public void UnsafeChangeDuringBindingCannotBecomeReady(string trigger)
    {
        int closed = 0;
        var binding = new PortalQuickRecalHotkey(() => { }, () => closed++);
        binding.SetSessionForTests("/session");
        binding.ShortcutChanged("/session", trigger);
        Assert.False(binding.Ready());
        Assert.Equal(1, closed);
    }

    [Fact]
    public Task EligibilityAndClashControlPortalBinding() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow { QuickRecalWayland = true };
        int attempts = 0;
        var first = new Registration();
        shell.RegisterWaylandQuickRecal = (_, _, _) => { attempts++; return Task.FromResult<IDisposable?>(first); };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", false, WebcamConsent.ConsentVersion);
            tracker.Calibration = null;
            shell.ApplyQuickRecalHotkey();
            s.WebcamConsentGiven = true;
            shell.ApplyQuickRecalHotkey();
            Assert.Equal(0, attempts);
            tracker.Calibration = new WebcamCalibrationData();
            shell.ApplyQuickRecalHotkey();
            Assert.Equal(1, attempts);
            Assert.True(shell.QuickRecalHotkeyArmed);
            s.PauseKey = "G";
            shell.ApplyQuickRecalHotkey();
            Assert.True(first.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
            Assert.Equal(1, attempts);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task PendingRegistrationIsCancelledAndLateSuccessIsDisposed() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow { QuickRecalWayland = true };
        var pending = new TaskCompletionSource<IDisposable?>();
        var registration = new Registration();
        CancellationToken token = default;
        shell.RegisterWaylandQuickRecal = (_, _, c) => { token = c; return pending.Task; };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            tracker.Calibration = new WebcamCalibrationData();
            shell.ApplyQuickRecalHotkey();
            Assert.False(shell.QuickRecalHotkeyArmed);
            shell.StopQuickRecalHotkey();
            Assert.True(token.IsCancellationRequested);
            pending.SetResult(registration); // even a backend that ignores cancellation cannot leak a grab
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
        }
        finally
        {
            pending.TrySetResult(null);
            shell.StopQuickRecalHotkey();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });

    [Fact]
    public Task WaylandDesktopStartupArmsAndShellShutdownReleases() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsLinux()) return;
        Init();
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        MainShellWindow? shell = null;
        var registration = new Registration();
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            tracker.Calibration = new WebcamCalibrationData();
            shell = new MainShellWindow();
            Assert.True(shell.QuickRecalWayland);
            Assert.True(shell.QuickRecalPlatformSupported);
            shell.RegisterWaylandQuickRecal = (_, _, _) => Task.FromResult<IDisposable?>(registration);
            shell.StartQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            shell.StopQuickRecalHotkey();
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
        }
        finally
        {
            shell?.StopQuickRecalHotkey();
            shell?.Close();
            tracker.Calibration = oldCal;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task PendingPortalResponseRechecksRevokedConsent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow { QuickRecalWayland = true };
        var pending = new TaskCompletionSource<IDisposable?>();
        var registration = new Registration();
        shell.RegisterWaylandQuickRecal = (_, _, _) => pending.Task;
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            tracker.Calibration = new WebcamCalibrationData();
            shell.ApplyQuickRecalHotkey();
            s.WebcamConsentGiven = false; // timer has not run yet
            pending.SetResult(registration);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
        }
        finally
        {
            pending.TrySetResult(null);
            shell.StopQuickRecalHotkey();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });

    [Fact]
    public Task AcceptedPortalPressOpensTheExistingQuickRecalFlow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow { QuickRecalWayland = true };
        Action pressed = () => { };
        var opened = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        shell.RegisterWaylandQuickRecal = (p, _, _) => { pressed = p; return Task.FromResult<IDisposable?>(new Registration()); };
        shell.ShowQuickRecal = (_, _) => { opened.TrySetResult(); return release.Task; };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            tracker.Calibration = new WebcamCalibrationData();
            Assert.True(await tracker.StartAsync(), tracker.LastError);
            shell.ApplyQuickRecalHotkey();
            pressed();
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(tracker.IsRunning);
        }
        finally
        {
            release.TrySetResult();
            shell.StopQuickRecalHotkey();
            await tracker.StopAsync();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public Task PortalCloseDisarmsAndOldCallbacksCannotOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var s = CoreSettings.Current;
        var old = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var shell = new MainShellWindow { QuickRecalWayland = true };
        Action pressed = () => { }, closed = () => { };
        int dialogs = 0;
        var registration = new Registration();
        shell.RegisterWaylandQuickRecal = (p, c, _) => { pressed = p; closed = c; return Task.FromResult<IDisposable?>(registration); };
        shell.ShowQuickRecal = (_, _) => { dialogs++; return Task.CompletedTask; };
        try
        {
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) =
                (true, true, "Escape", "", true, WebcamConsent.ConsentVersion);
            tracker.Calibration = new WebcamCalibrationData();
            shell.ApplyQuickRecalHotkey();
            Assert.True(shell.QuickRecalHotkeyArmed);
            closed();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(registration.Disposed);
            Assert.False(shell.QuickRecalHotkeyArmed);
            pressed();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(0, dialogs);
        }
        finally
        {
            shell.StopQuickRecalHotkey();
            shell.Close();
            tracker.Calibration = oldCal;
            (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });
}
