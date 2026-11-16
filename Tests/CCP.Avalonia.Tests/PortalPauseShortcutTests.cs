using System;
using System.Collections.Generic;
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
using Tmds.DBus.Protocol;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PortalPauseShortcutTests
{
    [Fact]
    public void SignalsAreScopedAndHeldPressesAreSuppressed()
    {
        int presses = 0, closed = 0;
        var binding = new PortalPauseShortcut("G", () => presses++, () => closed++);
        binding.SetSessionForTests("/pause");
        Assert.True(binding.Ready());
        var now = DateTime.UtcNow;
        binding.Route("/panic", "pause", true, now);
        binding.Route("/pause", "panic", true, now);
        binding.Route("/pause", "pause", true, now);
        binding.Route("/pause", "pause", true, now.AddMilliseconds(100));
        Assert.Equal(1, presses);
        binding.Route("/pause", "pause", false, now.AddMilliseconds(150));
        binding.Route("/pause", "pause", true, now.AddMilliseconds(200));
        Assert.Equal(2, presses);
        binding.Dispose();
        binding.Route("/pause", "pause", true, now.AddSeconds(3));
        Assert.Equal(2, presses);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void LostSessionClosesOnceAndCannotBeReadied()
    {
        int closed = 0;
        var binding = new PortalPauseShortcut("G", () => { }, () => closed++);
        binding.SetSessionForTests("/pause");
        binding.Lost();
        binding.Lost();
        Assert.False(binding.Ready());
        Assert.Equal(1, closed);
    }

    // BindShortcuts responses as KDE and GNOME send them: trigger_description is display text.
    private static Dictionary<string, VariantValue> Response(string id, string description) => new()
    {
        ["shortcuts"] = new Array<Struct<string, Dict<string, VariantValue>>>(new[]
        {
            Struct.Create(id, new Dict<string, VariantValue> { ["trigger_description"] = VariantValue.Variant(description) }),
        }).AsVariantValue(),
    };

    [Theory]
    [InlineData("Page Up")]
    [InlineData("Bild auf")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("")]
    public void AnyListedPauseShortcutIsBoundWhateverItsDescription(string description)
    {
        Assert.True(PortalPauseShortcut.Bound(0, Response("pause", description)));
        Assert.False(PortalPauseShortcut.Bound(1, Response("pause", description)));   // refused or cancelled
        Assert.False(PortalPauseShortcut.Bound(0, Response("panic", description)));   // ours not bound
        Assert.False(PortalPauseShortcut.Bound(0, new Dictionary<string, VariantValue>()));
    }

    [Theory]
    [InlineData("Oem102")]
    [InlineData("OemBackslash")]
    public async Task OemKeyReachesPortalWithoutPreferredTrigger(string key)
    {
        var options = PortalPauseShortcut.ShortcutOptions(X11PanicKey.XKeyName(key));
        Assert.True(options.ContainsKey("description"));
        Assert.False(options.ContainsKey("preferred_trigger"));
        Assert.Equal("F9", PortalPauseShortcut.ShortcutOptions(X11PanicKey.XKeyName("F9"))["preferred_trigger"].GetString());
        var original = PortalPauseShortcut.OpenConnection;
        int attempts = 0;
        try
        {
            PortalPauseShortcut.OpenConnection = () => { attempts++; throw new InvalidOperationException("fake bus"); };
            Assert.Null(await PortalPauseShortcut.BindAsync(key, () => { }, () => { }, CancellationToken.None, false));
            Assert.Equal(1, attempts);
            Assert.Null(await PortalPauseShortcut.BindAsync("  ", () => { }, () => { }, CancellationToken.None, false));
            Assert.Null(await PortalPauseShortcut.BindAsync(key, () => { }, () => { }, CancellationToken.None, true));
            Assert.Equal(1, attempts);
        }
        finally { PortalPauseShortcut.OpenConnection = original; }
    }

    [Theory]
    [InlineData("Oem102", "OemBackslash")]
    [InlineData("OemBackslash", "Oem102")]
    public Task OemKeyBindsDuringVideoUnlessPanicShadowsIt(string pause, string panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var oldLockdown = LockdownService.Current;
        var shell = new MainShellWindow { PauseKeyWayland = true, PauseKeyVideoPlaying = () => true };
        int attempts = 0, graces = 0;
        Action? press = null;
        var registrations = new List<Registration>();
        shell.RegisterWaylandPauseKey = (key, action, _, _) =>
        {
            Assert.Equal(pause, key);
            Assert.False(PortalPauseShortcut.ShortcutOptions(X11PanicKey.XKeyName(key)).ContainsKey("preferred_trigger"));
            attempts++; press = action;
            var r = new Registration(); registrations.Add(r); return Task.FromResult<IDisposable?>(r);
        };
        shell.PauseKeyGracePause = () => { graces++; return true; };
        try
        {
            LockdownService.Current = null;
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, "", pause);
            shell.StartPauseKeyPortal();
            Assert.Equal(1, attempts); Assert.True(shell.PauseKeyArmed);
            press!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, graces);
            s.PanicKey = panic; shell.ApplyPauseKeyPortal();
            Assert.True(registrations[0].Disposed); Assert.False(shell.PauseKeyArmed);
            Assert.Equal(1, attempts);
        }
        finally
        {
            shell.StopPauseKeyPortal(); shell.Close(); LockdownService.Current = oldLockdown;
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = saved;
        }
        return Task.CompletedTask;
    });

    // WPF MainWindow.xaml.cs:968: with panic off, a leash still takes the panic key before pause.
    [Fact]
    public void LeashKeepsAPanicOffSharedKeyAwayFromPause()
    {
        var s = new ConditioningControlPanel.Models.AppSettings { PanicKeyEnabled = false, PanicKey = "F9", PauseKey = "F9" };
        var (oldSvc, oldLockdown) = (LeashHead.Service, LockdownService.Current);
        try
        {
            (LeashHead.Service, LockdownService.Current) = (null, null);
            Assert.False(MainShellWindow.PauseKeyRefused(s));
            var svc = new ConditioningControlPanel.Services.Leash.LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
            svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            LeashHead.Service = svc;
            Assert.True(MainShellWindow.PauseKeyRefused(s));
            s.PauseKey = "F10";
            Assert.False(MainShellWindow.PauseKeyRefused(s));
        }
        finally { (LeashHead.Service, LockdownService.Current) = (oldSvc, oldLockdown); }
    }

    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public Task ShellBindsOnlyDuringVideoAndRoutesToGracePause() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var rig = new VideoRig();
        var (s, shell) = (CoreSettings.Current, rig.Shell);
        bool playing = false;
        int graces = 0;
        var keys = new List<string>();
        var registrations = new List<Registration>();
        Action? press = null, closed = null;
        shell.PauseKeyVideoPlaying = () => playing;
        shell.PauseKeyGracePause = () => { graces++; return true; };
        shell.RegisterWaylandPauseKey = (key, pressed, lost, _) =>
        {
            keys.Add(key); press = pressed; closed = lost;
            var r = new Registration(); registrations.Add(r);
            return Task.FromResult<IDisposable?>(r);
        };
        shell.StartPauseKeyPortal();
        Assert.Empty(keys);                       // no video: no desktop-wide grab
        playing = true;
        shell.ApplyPauseKeyPortal();
        Assert.Equal(new[] { "F9" }, keys);
        Assert.True(shell.PauseKeyArmed);
        press!();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, graces);

        s.PauseKey = "F10";                       // rebind follows the setting
        shell.ApplyPauseKeyPortal();
        Assert.True(registrations[0].Disposed);
        Assert.Equal("F10", keys[^1]);

        s.PanicKey = "F10";                       // panic wins a shared key: released, press refused
        shell.ApplyPauseKeyPortal();
        Assert.True(registrations[1].Disposed);
        Assert.False(shell.PauseKeyArmed);
        shell.OnPortalPauseKeyPressed("F10");
        Assert.Equal(1, graces);
        s.PanicKey = "Escape";
        shell.ApplyPauseKeyPortal();
        Assert.True(shell.PauseKeyArmed);

        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        press!();                                 // a press racing the poll is refused under Lockdown
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, graces);
        Assert.False(shell.PauseKeyArmed);
        ld.Deactivate();
        LockdownService.Current = null;
        shell.ApplyPauseKeyPortal();
        Assert.True(shell.PauseKeyArmed);

        closed!();                                // portal dropped it: no rebind loop
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.PauseKeyArmed);
        int count = keys.Count;
        shell.ApplyPauseKeyPortal();
        Assert.Equal(count, keys.Count);

        playing = false;                          // video ended, then the next one rebinds
        shell.ApplyPauseKeyPortal();
        playing = true;
        shell.ApplyPauseKeyPortal();
        Assert.Equal(count + 1, keys.Count);
        shell.StopPauseKeyPortal();
        Assert.True(registrations[^1].Disposed);
        await Task.CompletedTask;
    });
    [Fact]
    public Task LostPortalSessionRetriesWithBackoffUntilVideoEnds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var rig = new VideoRig();
        var shell = rig.Shell;
        bool playing = true;
        long now = 0;
        int attempts = 0, graces = 0;
        Action? lost = null, press = null;
        var first = new Registration();
        shell.PauseKeyVideoPlaying = () => playing;
        shell.PauseKeyNow = () => now;
        shell.PauseKeyGracePause = () => { graces++; return true; };
        shell.RegisterWaylandPauseKey = (_, p, l, _) =>
        {
            attempts++; press = p; lost = l;
            return Task.FromResult<IDisposable?>(attempts == 1 ? first : attempts == 2 ? null : new Registration());
        };
        shell.StartPauseKeyPortal();
        var staleLost = lost!;
        staleLost(); Dispatcher.UIThread.RunJobs();
        Assert.True(first.Disposed); Assert.False(shell.PauseKeyArmed);
        shell.ApplyPauseKeyPortal(); now = 999; shell.ApplyPauseKeyPortal();
        Assert.Equal(1, attempts);
        now = 1000; shell.ApplyPauseKeyPortal(); // a refused retry also backs off
        Assert.Equal(2, attempts); Assert.False(shell.PauseKeyArmed);
        shell.ApplyPauseKeyPortal(); Assert.Equal(2, attempts);
        now = 2000; shell.ApplyPauseKeyPortal();
        Assert.Equal(3, attempts); Assert.True(shell.PauseKeyArmed);
        staleLost(); Dispatcher.UIThread.RunJobs(); // old session cannot cancel the replacement
        press!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, graces);
        lost!(); Dispatcher.UIThread.RunJobs();
        playing = false; shell.ApplyPauseKeyPortal();
        now = 3000; shell.ApplyPauseKeyPortal(); Assert.Equal(3, attempts);
        playing = true; shell.ApplyPauseKeyPortal(); Assert.Equal(4, attempts);
        lost!(); Dispatcher.UIThread.RunJobs();
        CoreSettings.Current.PauseKey = "F10"; // a new binding does not wait for the old key's retry
        shell.ApplyPauseKeyPortal(); Assert.Equal(5, attempts); Assert.True(shell.PauseKeyArmed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StaleActivationCannotPauseAfterRebindOrVideoEnd() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var rig = new VideoRig();
        var shell = rig.Shell;
        bool playing = true;
        int graces = 0;
        Action? press = null;
        var registrations = new List<Registration>();
        shell.PauseKeyVideoPlaying = () => playing;
        shell.PauseKeyGracePause = () => { graces++; return true; };
        shell.RegisterWaylandPauseKey = (_, pressed, _, _) =>
        {
            press = pressed;
            var r = new Registration();
            registrations.Add(r);
            return Task.FromResult<IDisposable?>(r);
        };
        shell.StartPauseKeyPortal();
        var oldPress = press!;
        CoreSettings.Current.PauseKey = "F10"; // no poll has run yet
        oldPress();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, graces);
        Assert.True(registrations[0].Disposed);
        Assert.Equal(2, registrations.Count);
        oldPress(); // callback queued from the old generation
        press!();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, graces);
        playing = false; // no poll has run yet
        press!();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, graces);
        Assert.True(registrations[1].Disposed);
        return Task.CompletedTask;
    });

    private sealed class VideoHost : IMandatoryVideoHost
    {
        internal int Shows, Freezes;
        internal Action? OnShow;
        public void Show(string path, bool strict) { Shows++; OnShow?.Invoke(); }
        public void Freeze() => Freezes++;
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class VideoClock : TimeProvider
    {
        private ManualTimer? _timer;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => callback(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    /// <summary>A real scheduler driving the shell. Saves every static seam it sets and restores it on Dispose.</summary>
    private sealed class VideoRig : IDisposable
    {
        private readonly (bool, string, string, bool) _saved;
        private readonly LockdownService? _lockdown = LockdownService.Current;
        private readonly Action<Action>? _post = CoreDispatch.PostProvider;
        private readonly Action? _freeze = CoreSubliminal.BambiFreezeProvider;
        internal readonly VideoClock Clock = new();
        internal readonly VideoHost Host = new();
        internal readonly MandatoryVideoScheduler Scheduler;
        internal readonly MainShellWindow Shell;

        internal VideoRig(string pauseKey = "F9")
        {
            var s = CoreSettings.Current;
            _saved = (s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.AttentionChecksEnabled);
            var scheduler = Scheduler = new MandatoryVideoScheduler(Host, Clock, () => new[] { "/test/start.mp4" });
            Shell = new MainShellWindow { PauseKeyWayland = true, PauseKeyScheduler = () => scheduler, PauseKeyVideoPlaying = () => scheduler.IsPlaying };
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, "Escape", pauseKey);
            LockdownService.Current = null; CoreDispatch.PostProvider = a => a(); CoreSubliminal.BambiFreezeProvider = null;
        }

        public void Dispose()
        {
            try { Shell.StopPauseKeyPortal(); Scheduler.ForceCleanup(); Shell.Close(); }
            finally
            {
                CoreDispatch.PostProvider = _post; CoreSubliminal.BambiFreezeProvider = _freeze;
                LockdownService.Current = _lockdown;
                var s = CoreSettings.Current;
                (s.PanicKeyEnabled, s.PanicKey, s.PauseKey, s.AttentionChecksEnabled) = _saved;
            }
        }
    }

    [Fact]
    public Task PortalBindsBeforeFirstVisibleFrameAndHandlesTheFirstPress() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var pending = new TaskCompletionSource<IDisposable?>();
        var registration = new Registration();
        Action? press = null;
        int binds = 0, graces = 0;
        shell.RegisterWaylandPauseKey = (_, p, _, _) => { binds++; press = p; return pending.Task; };
        shell.PauseKeyGracePause = () => { graces++; return true; };
        shell.StartPauseKeyPortal();
        Assert.True(scheduler.Trigger());
        Assert.Equal(1, binds); // before pre-roll or any poll
        clock.Fire();
        Assert.Equal(0, host.Shows); // permission is still pending
        host.OnShow = () => press!(); // a press at the first visible frame
        pending.SetResult(registration);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, host.Shows);
        Assert.Equal(1, graces);
        scheduler.ForceCleanup();
        Assert.True(registration.Disposed);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingVideoStartCanBeStoppedOrTimeOut(bool timeout) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        if (timeout) shell.PauseKeyBindTimeout = TimeSpan.Zero;
        var pending = new TaskCompletionSource<IDisposable?>(); var late = new Registration();
        CancellationToken token = default;
        shell.RegisterWaylandPauseKey = (_, _, _, t) => { token = t; return pending.Task; };
        shell.StartPauseKeyPortal();
        Assert.True(scheduler.Trigger());
        clock.Fire();
        if (!timeout) { Assert.Equal(0, host.Shows); scheduler.ForceCleanup(); }
        // The deadline starts the clip but leaves the portal request (a permission dialog) running.
        Assert.Equal(!timeout, token.IsCancellationRequested);
        pending.SetResult(late);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(!timeout, late.Disposed);
        Assert.Equal(timeout, shell.PauseKeyArmed);
        Assert.Equal(timeout ? 1 : 0, host.Shows);
        scheduler.ForceCleanup(); Assert.True(late.Disposed);
    });

    [Fact]
    public Task RebindDuringPreRollWaitsForTheNewRegistration() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var first = new TaskCompletionSource<IDisposable?>(); var second = new TaskCompletionSource<IDisposable?>();
        var oldRegistration = new Registration(); var newRegistration = new Registration();
        CancellationToken oldToken = default; int calls = 0;
        shell.RegisterWaylandPauseKey = (_, _, _, token) => { calls++; if (calls == 1) { oldToken = token; return first.Task; } return second.Task; };
        shell.StartPauseKeyPortal(); Assert.True(scheduler.Trigger()); clock.Fire();
        Assert.Equal(0, host.Shows);
        CoreSettings.Current.PauseKey = "F10"; shell.ApplyPauseKeyPortal();
        Assert.True(oldToken.IsCancellationRequested); Assert.Equal(2, calls);
        second.SetResult(newRegistration);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, host.Shows);
        first.SetResult(oldRegistration);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.True(oldRegistration.Disposed);
        scheduler.ForceCleanup(); Assert.True(newRegistration.Disposed);
    });

    [Fact]
    public Task RebindAfterPreparationBeforeShowWaitsForTheNewKey() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var first = new TaskCompletionSource<IDisposable?>(); var second = new TaskCompletionSource<IDisposable?>();
        var firstRegistration = new Registration(); var secondRegistration = new Registration();
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var postedReady = new SemaphoreSlim(0);
        bool hold = false; int calls = 0;
        shell.RegisterWaylandPauseKey = (_, _, _, _) => ++calls == 1 ? first.Task : second.Task;
        CoreDispatch.PostProvider = a => { if (hold) { posted.Enqueue(a); postedReady.Release(); } else a(); };
        shell.StartPauseKeyPortal(); Assert.True(scheduler.Trigger()); clock.Fire();
        hold = true; first.SetResult(firstRegistration);
        Assert.True(await postedReady.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(posted.TryDequeue(out var show));
        CoreSettings.Current.PauseKey = "F10"; // no poll before the queued show callback
        show!();
        Assert.Equal(0, host.Shows); Assert.Equal(2, calls); Assert.True(firstRegistration.Disposed);
        second.SetResult(secondRegistration);
        Assert.True(await postedReady.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(posted.TryDequeue(out var resume)); resume!();
        Assert.Equal(1, host.Shows);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnansweredPortalCannotExtendPreRollByMoreThan100Milliseconds(bool freeze) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var deadline = new TaskCompletionSource(); var pending = new TaskCompletionSource<IDisposable?>();
        TimeSpan requested = default; int delays = 0, graces = 0; CancellationToken token = default;
        var registration = new Registration();
        shell.PauseKeyGracePause = () => { graces++; return true; };
        shell.PauseKeyDelay = delay => { requested = delay; delays++; return deadline.Task; };
        shell.RegisterWaylandPauseKey = (_, _, _, t) => { token = t; return pending.Task; };
        CoreSettings.Current.AttentionChecksEnabled = false;
        CoreSubliminal.BambiFreezeProvider = freeze ? () => { } : null;
        shell.StartPauseKeyPortal(); Assert.True(scheduler.Trigger());
        Assert.Equal(MandatoryVideoScheduler.PreRoll + TimeSpan.FromMilliseconds(100), requested);
        Assert.Equal(1, delays);
        if (freeze) { Assert.Equal(1, host.Freezes); clock.Fire(); }
        clock.Fire(); Assert.Equal(0, host.Shows);
        deadline.SetResult();
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.False(token.IsCancellationRequested); // a slow permission dialog keeps its request
        Assert.Equal(1, host.Shows);
        Assert.Equal(1, delays); // rechecks do not restart the deadline
        pending.SetResult(registration); // the user answers after the clip started
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.True(shell.PauseKeyArmed);
        shell.OnPortalPauseKeyPressed("F9"); Assert.Equal(1, graces);
        scheduler.ForceCleanup(); Assert.True(registration.Disposed); Assert.True(token.IsCancellationRequested);
    });

    [Fact]
    public Task OldVideoDeadlineCannotCancelImmediatelyRestartedVideo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig();
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var deadlines = new List<TaskCompletionSource>(); var requests = new List<TaskCompletionSource<IDisposable?>>();
        var tokens = new List<CancellationToken>(); var registration = new Registration();
        shell.PauseKeyDelay = _ => { var d = new TaskCompletionSource(); deadlines.Add(d); return d.Task; };
        shell.RegisterWaylandPauseKey = (_, _, _, t) => { tokens.Add(t); var r = new TaskCompletionSource<IDisposable?>(); requests.Add(r); return r.Task; };
        shell.StartPauseKeyPortal(); Assert.True(scheduler.Trigger());
        Assert.Single(deadlines); Assert.Single(requests);
        scheduler.ForceCleanup(); Assert.True(tokens[0].IsCancellationRequested);
        Assert.True(scheduler.Trigger()); Assert.Equal(2, deadlines.Count); Assert.Equal(2, requests.Count);
        deadlines[0].SetResult(); // old deadline expires after the new clip is accepted
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.False(tokens[1].IsCancellationRequested);
        requests[1].SetResult(registration); clock.Fire();
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.True(shell.PauseKeyArmed); Assert.Equal(1, host.Shows);
        scheduler.ForceCleanup(); Assert.True(registration.Disposed);
        requests[0].SetResult(null);
    });

    [Fact]
    public Task LateBindingDoesNotRestartTheVideoDeadline() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new VideoRig(pauseKey: "");
        var (shell, scheduler, host, clock) = (rig.Shell, rig.Scheduler, rig.Host, rig.Clock);
        var deadline = new TaskCompletionSource(); var pending = new TaskCompletionSource<IDisposable?>();
        int deadlines = 0, binds = 0; CancellationToken token = default;
        shell.PauseKeyDelay = _ => { deadlines++; return deadline.Task; };
        shell.RegisterWaylandPauseKey = (_, _, _, t) => { binds++; token = t; return pending.Task; };
        shell.StartPauseKeyPortal(); Assert.True(scheduler.Trigger());
        Assert.Equal(1, deadlines); Assert.Equal(0, binds);
        CoreSettings.Current.PauseKey = "F9"; clock.Fire(); // no poll before the pre-roll ends
        Assert.Equal(1, binds); Assert.Equal(0, host.Shows);
        deadline.SetResult();
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, deadlines); Assert.False(token.IsCancellationRequested); Assert.Equal(1, host.Shows);
        scheduler.ForceCleanup(); Assert.True(token.IsCancellationRequested);
        pending.SetResult(null);
    });
}
