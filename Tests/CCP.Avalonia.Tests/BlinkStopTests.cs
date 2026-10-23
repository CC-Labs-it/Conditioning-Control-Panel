using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.LabTab.cs:156-283 + Services/Safety/BlinkStopGate: six blinks inside 3.5 s
/// stop every panic surface except the camera (and the hosts WPF's recal stop never closed), only when
/// opted in and never where the panic key could not stop either. And :477: the camera pill stops it.</summary>
public sealed class BlinkStopTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Task WithShell(Func<MainShellWindow, List<string>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled);
        var prev = PanicSurfaces.All;
        var stopped = new List<string>();
        PanicSurfaces.All = prev.Select(x => new PanicSurfaces.Surface(x.Id, _ => stopped.Add(x.Id))).ToArray();
        (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled) = (true, true, false);
        var shell = new MainShellWindow();
        var offered = 0;
        shell.BlinkRecalOfferForTests = () => { offered++; return Task.CompletedTask; };
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(shell, stopped);
        }
        finally
        {
            shell.Close();
            PanicSurfaces.All = prev;
            (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled) = old;
        }
    });

    [Fact]
    public Task SixTrackerBlinksStopEverythingButTheCamera() => WithShell((shell, stopped) =>
    {
        for (var i = 0; i < 5; i++) WebcamTracker.Instance.RaiseBlinkForTest();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(stopped);
        WebcamTracker.Instance.RaiseBlinkForTest();
        Dispatcher.UIThread.RunJobs();
        // Exactly WPF StopAllForRecalibration's set, in registry order: the engine stop PAUSES the session
        // (StopEngine -> Sessions.Pause), lock cards close (App.xaml.cs:1778), the tube is silenced; KillAllAudio
        // also stops the ambient chant (App.xaml.cs:1844 MantraChant?.StopAndDisarm).
        Assert.Equal(new[] { "blink-trainer", "mantra", "mantra-chant", "takeover", "engine", "pink-rush", "tube-speech", "lock-cards" }, stopped);
        // A recalibration stop, not a panic: today's program run is paused by the engine stop, never ended,
        // and the camera, mic, AI and toys are left alone.
        foreach (var kept in new[] { "program-session", "camera", "voice-capture", "ai-followups", "haptics", "remote-haptics", "intake", "chaos" })
            Assert.DoesNotContain(kept, stopped);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheRunMustFitTheWindowAndTheToggle() => WithShell((shell, stopped) =>
    {
        var t0 = DateTime.UtcNow;
        // Six blinks spread over > 3.5 s: never six inside the window.
        for (var i = 0; i < 6; i++) Assert.False(shell.OnRapidBlink(t0.AddMilliseconds(i * 800)));
        Assert.Empty(stopped);
        // Toggle off: a fast run is just blinking.
        CoreSettings.Current.BlinkRecalibrateShortcutEnabled = false;
        var t1 = t0.AddSeconds(30);
        for (var i = 0; i < 6; i++) Assert.False(shell.OnRapidBlink(t1.AddMilliseconds(i * 500)));
        Assert.Empty(stopped);
        CoreSettings.Current.BlinkRecalibrateShortcutEnabled = true;
        var t2 = t0.AddSeconds(60);
        for (var i = 0; i < 5; i++) Assert.False(shell.OnRapidBlink(t2.AddMilliseconds(i * 500)));
        Assert.True(shell.OnRapidBlink(t2.AddMilliseconds(2500)));
        Assert.NotEmpty(stopped);
        return Task.CompletedTask;
    });

    /// <summary>Never more permissive than the panic key (WPF LabTab.cs:186): panic off, Strict Lock, Lockdown.</summary>
    [Fact]
    public Task TheGateRefusesWhereThePanicKeyCouldNot() => WithShell((shell, stopped) =>
    {
        var s = CoreSettings.Current;
        var t = DateTime.UtcNow;
        bool Run() { var fired = false; for (var i = 0; i < 6; i++) fired |= shell.OnRapidBlink(t = t.AddMilliseconds(500)); return fired; }

        s.PanicKeyEnabled = false;
        Assert.False(Run());
        s.PanicKeyEnabled = true;
        s.StrictLockEnabled = true;
        Assert.False(Run());
        s.StrictLockEnabled = false;
        var oldLd = LockdownService.Current;
        var oldArm = (s.LockdownForceStrictLock, s.LockdownDisablePanicKey);
        (s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, false);   // Lockdown alone, panic still on
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.True(s.PanicKeyEnabled && !s.StrictLockEnabled);
            Assert.False(Run());
        }
        finally
        {
            ld.Deactivate();
            ld.Dispose();
            LockdownService.Current = oldLd;
            (s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = oldArm;
        }
        Assert.Empty(stopped);
        Assert.True(Run());
        return Task.CompletedTask;
    });

    /// <summary>WPF :134/:477: the pill lights while the camera runs; Enter on it (P17) stops the camera.</summary>
    [Fact]
    public Task TheCameraPillShowsAndStopsTheCamera() => WithShell(async (shell, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion);
        var factory = WebcamTracker.SourceFactory;
        var tracker = WebcamTracker.Instance;
        try
        {
            (s.WebcamConsentGiven, s.WebcamConsentVersion) = (true, WebcamConsent.ConsentVersion);
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            var pill = shell.Named<Border>("WebcamActivePill")!;
            Assert.False(pill.IsVisible);
            Assert.True(await tracker.StartAsync(), tracker.LastError);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pill.IsVisible);

            pill.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.True(SpinWait.SpinUntil(() => !tracker.IsRunning, 8000));
            for (var sw = System.Diagnostics.Stopwatch.StartNew(); pill.IsVisible && sw.ElapsedMilliseconds < 8000;)
                Dispatcher.UIThread.RunJobs();
            Assert.False(pill.IsVisible);
        }
        finally
        {
            tracker.Stop();
            WebcamTracker.SourceFactory = factory;
            (s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });
}
