using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11VideoEscapePartATests
{
    private sealed class StillTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static Task WithVideo(Action<MandatoryVideoOverlay> run) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey, s.AttentionChecksEnabled, s.GentleVisuals);
        (s.PanicKeyEnabled, s.PanicKey, s.AttentionChecksEnabled, s.GentleVisuals) = (true, "F12", false, false);
        var o = MandatoryVideoOverlay.Instance;
        var previous = o.Scheduler;
        var fields = new[] { "_strict", "_graceConsumed", "_lastGraceUtc" };
        var savedFields = Array.ConvertAll(fields, name => typeof(MandatoryVideoOverlay).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!);
        var values = Array.ConvertAll(savedFields, field => field.GetValue(o));
        savedFields[2].SetValue(o, default(DateTime));
        var code = X11PanicKey.BoundKeycode;
        o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => Array.Empty<string>());
        X11PanicKey.BindKeycodeForTest(96);
        try { run(o); }
        finally
        {
            o.Scheduler.ForceCleanup();
            o.CloseAll();
            o.Scheduler = previous;
            for (int i = 0; i < savedFields.Length; i++) savedFields[i].SetValue(o, values[i]);
            X11PanicKey.BindKeycodeForTest(code);
            (s.PanicKeyEnabled, s.PanicKey, s.AttentionChecksEnabled, s.GentleVisuals) = saved;
        }
        return Task.CompletedTask;
    });

    private static void WithCleanSurfaceClaims(Action body)
    {
        EscapeClaim.ClearSurfaceKeysForTest();
        try { body(); }
        finally { EscapeClaim.ClearSurfaceKeysForTest(); }
    }

    private static void Open(MandatoryVideoOverlay o, bool strict = false)
    {
        Assert.True(o.Scheduler.Trigger(strict, "/test/video.mp4"));
        typeof(MandatoryVideoOverlay).GetField("_strict", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, strict);
        ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(o.Build(null, strict));
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Paused;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Paused;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() { }
        public void Dispose() { }
    }

    [Fact]
    public Task EscapeFromAnotherXClientPausesVideo() => WithVideo(o =>
    {
        Open(o);
        var decoder = new Decoder();
        typeof(MandatoryVideoOverlay).GetField("_playback", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, decoder);
        typeof(MandatoryVideoOverlay).GetField("_graceConsumed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, false);
        bool down = false;
        X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, () => X11PanicKey.QueueVideoEscape());
        Dispatcher.UIThread.RunJobs();
        Assert.True(o.Scheduler.IsPlaying && o.GracePaused && decoder.Paused);
    });

    [Fact]
    public Task PanicBindingKeepsThePressAndStrictVideoStaysLocked() => WithVideo(o =>
    {
        var oldLockdown = LockdownService.Current;
        using var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            lockdown.Activate(TimeSpan.FromMinutes(30));
            Open(o, strict: true);
            bool held = false;
            X11PanicKey.RouteVideoEscape(true, 9, 9, ref held, () => X11PanicKey.QueueVideoEscape());
            Dispatcher.UIThread.RunJobs();
            Assert.True(o.Scheduler.IsPlaying);
        }
        finally { lockdown.Deactivate(); LockdownService.Current = oldLockdown; }
        o.Scheduler.ForceCleanup();
        Open(o);
        X11PanicKey.BindKeycodeForTest(9);
        CoreSettings.Current.PanicKey = "Escape";
        bool down = false;
        int panic = 0, videoRequests = 0;
        X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, () => videoRequests++);
        Assert.Equal(0, videoRequests);
        X11PanicKey.Route(true, 9, () => { panic++; o.Scheduler.ForceCleanup(); });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, panic);
        Assert.False(o.Scheduler.IsPlaying);
    });

    [Theory]
    [InlineData(false, false)] // panic disabled
    [InlineData(true, true)]   // Lockdown blocks the enabled panic key
    public Task SharedEscapeReachesNonStrictVideoWhenPanicCannotRun(bool panicEnabled, bool locked) => WithVideo(o =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (panicEnabled, "Escape");
        X11PanicKey.BindKeycodeForTest(9);
        var oldLockdown = LockdownService.Current;
        using var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            if (locked) lockdown.Activate(TimeSpan.FromMinutes(30));
            Open(o);
            var decoder = new Decoder();
            typeof(MandatoryVideoOverlay).GetField("_playback", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, decoder);
            typeof(MandatoryVideoOverlay).GetField("_graceConsumed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, false);
            bool down = false, videoOwns = false;
            int panic = 0;
            X11PanicKey.RouteRawKey(true, 9, 9, ref down,
                () => videoOwns = X11PanicKey.QueueVideoEscape(), () => panic++);
            Assert.True(videoOwns);
            Assert.Equal(0, panic);
            Dispatcher.UIThread.RunJobs();
            Assert.True(o.GracePaused || !o.Scheduler.IsPlaying);
        }
        finally { if (locked) lockdown.Deactivate(); LockdownService.Current = oldLockdown; }
    });

    [Fact]
    public Task QueuedEscapeCannotAffectAClipAfterPanicAndRestart() => WithVideo(o =>
    {
        Open(o);
        bool down = false;
        X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, () => X11PanicKey.QueueVideoEscape());
        o.Scheduler.ForceCleanup();
        Open(o);
        Dispatcher.UIThread.RunJobs();
        Assert.True(o.Scheduler.IsPlaying);
    });

    [Fact]
    public Task SurfaceClaimOwnsOnlyItsOwnPhysicalEscape() => WithVideo(o => WithCleanSurfaceClaims(() =>
    {
        Assert.False(EscapeClaim.SurfaceOwnsEscape(421337));
        Open(o);
        bool down = false;
        EscapeClaim.RecordSurfaceEscape(421337);
        X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, () => X11PanicKey.QueueVideoEscape(421337));
        Dispatcher.UIThread.RunJobs();
        Assert.True(o.Scheduler.IsPlaying);
        X11PanicKey.RouteVideoEscape(false, 9, 9, ref down, () => { });
        X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, () => X11PanicKey.QueueVideoEscape(421338));
        Dispatcher.UIThread.RunJobs();
        Assert.False(o.Scheduler.IsPlaying);
    }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FocusedSurfaceRecordsItsNativeEscapeTimestamp(bool palette) => AvaloniaTestDispatcher.Run(() => WithCleanSurfaceClaims(() =>
    {
        EscapeClaim.StartTracking(trackX11: true);
        global::Avalonia.Controls.Window window = palette
            ? new ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow()
            : new ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow();
        try
        {
            var root = typeof(global::Avalonia.Controls.TopLevel).GetProperty("InputRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var assembly = typeof(global::Avalonia.AvaloniaObject).Assembly;
            var device = Activator.CreateInstance(assembly.GetType("Avalonia.Input.KeyboardDevice")!)!;
            ulong stamp = palette ? 421339UL : 421340UL;
            Assert.False(EscapeClaim.SurfaceOwnsEscape(stamp));
            var key = Activator.CreateInstance(assembly.GetType("Avalonia.Input.Raw.RawKeyEventArgs")!, device, stamp, root,
                global::Avalonia.Input.Raw.RawKeyEventType.KeyDown, global::Avalonia.Input.Key.Escape,
                global::Avalonia.Input.RawInputModifiers.None, global::Avalonia.Input.PhysicalKey.Escape, null,
                global::Avalonia.Input.KeyDeviceType.Keyboard)!;
            var locator = assembly.GetType("Avalonia.AvaloniaLocator")!.GetProperty("Current")!.GetValue(null)!;
            var inputType = assembly.GetType("Avalonia.Input.IInputManager")!;
            var input = locator.GetType().GetMethod("GetService", new[] { typeof(Type) })!.Invoke(locator, new object[] { inputType })!;
            var stream = inputType.GetProperty("PreProcess")!.GetValue(input)!;
            stream.GetType().GetMethod("OnNext")!.Invoke(stream, new[] { key });
            Assert.True(EscapeClaim.SurfaceOwnsEscape(stamp));
        }
        finally { window.Close(); }
    }));

    [Fact]
    public void SurfaceClaimsAreClearedEvenWhenTheTestBodyThrows()
    {
        const uint stamp = 421341;
        try
        {
            Assert.Throws<InvalidOperationException>(() => WithCleanSurfaceClaims(() =>
            {
                EscapeClaim.RecordSurfaceEscape(stamp);
                Assert.True(EscapeClaim.SurfaceOwnsEscape(stamp));
                throw new InvalidOperationException();
            }));
            Assert.False(EscapeClaim.SurfaceOwnsEscape(stamp));
            WithCleanSurfaceClaims(() => Assert.False(EscapeClaim.SurfaceOwnsEscape(stamp)));
        }
        finally { EscapeClaim.ClearSurfaceKeysForTest(); }
    }

    [Fact]
    public void SharedPanicBindingDoesNotQueueVideo()
    {
        var code = X11PanicKey.BoundKeycode;
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "Escape");
        X11PanicKey.BindKeycodeForTest(9);
        try
        {
            bool down = false;
            int requests = 0, panic = 0;
            X11PanicKey.RouteRawKey(true, 9, 9, ref down, () => { requests++; return true; }, () => panic++);
            Assert.Equal(0, requests);
            Assert.Equal(1, panic);
        }
        finally { X11PanicKey.BindKeycodeForTest(code); (s.PanicKeyEnabled, s.PanicKey) = saved; }
    }

    [Fact]
    public void DuplicateDownIsOneVideoPressUntilRelease()
    {
        var code = X11PanicKey.BoundKeycode;
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F12");
        X11PanicKey.BindKeycodeForTest(96);
        try
        {
            bool down = false;
            int queued = 0;
            void Queue() => queued++;
            X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, Queue);
            X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, Queue);
            Assert.Equal(1, queued);
            X11PanicKey.RouteVideoEscape(false, 9, 9, ref down, Queue);
            X11PanicKey.RouteVideoEscape(true, 9, 9, ref down, Queue);
            Assert.Equal(2, queued);
        }
        finally { X11PanicKey.BindKeycodeForTest(code); (s.PanicKeyEnabled, s.PanicKey) = saved; }
    }
}
