using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11VideoEscapeTests
{
    private sealed class FrozenClock : TimeProvider
    {
        public override System.Threading.ITimer CreateTimer(System.Threading.TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : System.Threading.ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    [Fact]
    public void Raw_escape_routes_repeats_and_releases_without_consuming_other_keys()
    {
        var oldCode = X11PanicKey.BoundKeycode;
        var s = CoreSettings.Current;
        var oldPanic = (s.PanicKeyEnabled, s.PanicKey);
        var oldState = Win32Input.EscapeState;
        var oldPost = Win32Input.PostEscape;
        var oldTry = Win32Input.TryVideoEscape;
        try
        {
            (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape");
            X11PanicKey.BindKeycodeForTest(9);
            bool video = true, leashTaskActive = true;
            int deliveries = 0, panic = 0;
            Win32Input.EscapeState = () => (video, false, false);
            Win32Input.PostEscape = action => action();
            Win32Input.TryVideoEscape = () => { deliveries++; return true; };
            var events = new (bool, int, int)[] { (true, 10, 9), (false, 10, 9), (true, 0, 0),
                (true, 9, 9), (true, 9, 9), (false, 10, 9), (true, 9, 9), (false, 9, 9), (true, 9, 9) };
            int i = 0;
            ulong time = 1000;
            (bool, int, int)? Next()
            {
                if (i == 3) Assert.Equal(0, deliveries);
                if (i == 7) Assert.Equal(3, deliveries);
                if (i == 4) time += 201; // repeat after the video grace dedup window
                return i < events.Length ? events[i++] : null;
            }
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { panic++; leashTaskActive = false; }, Next, injectedTime: () => time);
            Assert.Equal(4, deliveries); // every video-owned key-down, including repeats
            Assert.Equal(0, panic);      // video owns both, even when panic is bound to Escape
            Assert.True(leashTaskActive); // the leash path was never invoked

            // When panic can run, WPF leaves Escape to panic instead of the video.
            (s.PanicKeyEnabled, s.PanicKey) = (true, "Escape");
            i = 0;
            var one = new (bool, int, int)[] { (true, 9, 9) };
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { panic++; leashTaskActive = false; }, () => i < one.Length ? one[i++] : null);
            Assert.Equal(1, panic);
            Assert.False(leashTaskActive);
            Assert.Equal(4, deliveries);
        }
        finally
        {
            X11PanicKey.BindKeycodeForTest(oldCode);
            (s.PanicKeyEnabled, s.PanicKey) = oldPanic;
            Win32Input.EscapeState = oldState;
            Win32Input.PostEscape = oldPost;
            Win32Input.TryVideoEscape = oldTry;
        }
    }

    [Fact]
    public void Listener_snapshots_palette_ownership_and_rechecks_video_on_delivery()
    {
        var oldCode = X11PanicKey.BoundKeycode;
        var s = CoreSettings.Current;
        var oldPanic = (s.PanicKeyEnabled, s.PanicKey);
        var oldState = Win32Input.EscapeState;
        var oldPost = Win32Input.PostEscape;
        var oldTry = Win32Input.TryVideoEscape;
        try
        {
            (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape");
            X11PanicKey.BindKeycodeForTest(9);
            bool video = true, palette = true, card = false;
            int deliveries = 0, attempts = 0, panic = 0;
            var pending = new System.Collections.Generic.Queue<Action>();
            Win32Input.EscapeState = () => (video, card, palette);
            Win32Input.PostEscape = pending.Enqueue;
            Win32Input.TryVideoEscape = () => { attempts++; deliveries++; return true; };
            void Press()
            {
                var keys = new (bool, int, int)[] { (true, 9, 9), (false, 9, 9) };
                int i = 0;
                X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => panic++, () => i < keys.Length ? keys[i++] : null);
            }

            Press();                         // palette owns it at the event
            palette = false;                 // focused palette closes before the UI queue drains
            Assert.Empty(pending);
            Assert.Equal(1, panic);          // it follows the pre-existing panic/leash path
            Assert.Equal(0, deliveries);

            Press();                         // video owns it at the event
            palette = true;                  // palette opens before delivery
            Assert.Single(pending);
            pending.Dequeue()();
            Assert.Equal(1, deliveries);
            Assert.Equal(1, panic);

            palette = false;
            Press();
            video = false;                   // clip stopped before queued delivery
            pending.Dequeue()();
            Assert.Equal(1, deliveries);
            Assert.Equal(1, attempts);       // a stopped clip is not called again
            Assert.Empty(pending);

            video = true;
            card = true;
            Press();                         // lock card also outranks the video
            Assert.Empty(pending);
            Assert.Equal(2, panic);
        }
        finally
        {
            X11PanicKey.BindKeycodeForTest(oldCode);
            (s.PanicKeyEnabled, s.PanicKey) = oldPanic;
            Win32Input.EscapeState = oldState;
            Win32Input.PostEscape = oldPost;
            Win32Input.TryVideoEscape = oldTry;
        }
    }

    [Fact]
    public void Listener_keeps_panic_available_if_the_ownership_snapshot_throws()
    {
        var oldCode = X11PanicKey.BoundKeycode;
        var oldState = Win32Input.EscapeState;
        try
        {
            X11PanicKey.BindKeycodeForTest(9);
            Win32Input.EscapeState = () => throw new InvalidOperationException("snapshot failed");
            bool sent = false;
            int panic = 0;
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => panic++,
                () => sent ? null : (sent = true, 9, 9));
            Assert.Equal(1, panic);
        }
        finally
        {
            X11PanicKey.BindKeycodeForTest(oldCode);
            Win32Input.EscapeState = oldState;
        }
    }

    [Fact]
    public void Listener_does_not_deliver_a_queued_escape_after_video_cleanup()
    {
        var oldState = Win32Input.EscapeState;
        var oldPost = Win32Input.PostEscape;
        var oldTry = Win32Input.TryVideoEscape;
        var settings = CoreSettings.Current;
        var oldPanic = (settings.PanicKeyEnabled, settings.PanicKey);
        try
        {
            (settings.PanicKeyEnabled, settings.PanicKey) = (true, "F12");
            bool video = true;
            int attempts = 0;
            Action? pending = null;
            Win32Input.EscapeState = () => (video, false, false);
            Win32Input.PostEscape = action => pending = action;
            Win32Input.TryVideoEscape = () => { attempts++; return true; };
            var sent = false;
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { }, () => sent ? null : (sent = true, 9, 9));
            Assert.NotNull(pending);
            video = false;
            pending();
            Assert.Equal(0, attempts);
        }
        finally
        {
            (settings.PanicKeyEnabled, settings.PanicKey) = oldPanic;
            Win32Input.EscapeState = oldState;
            Win32Input.PostEscape = oldPost;
            Win32Input.TryVideoEscape = oldTry;
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Surface_owns_only_its_original_escape_in_both_event_orders(bool palette, bool uiFirst) =>
        AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        EscapeClaim.StartTracking();
        var s = CoreSettings.Current;
        var oldPanic = (s.PanicKeyEnabled, s.PanicKey);
        var oldCode = X11PanicKey.BoundKeycode;
        var oldState = Win32Input.EscapeState;
        var oldPost = Win32Input.PostEscape;
        var oldTry = Win32Input.TryVideoEscape;
        try
        {
            (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape");
            X11PanicKey.BindKeycodeForTest(9);
            bool open = !uiFirst;
            int video = 0, leash = 0, step = 0;
            ulong stamp = 0, first = palette ? (uiFirst ? 1001UL : 3001UL) : (uiFirst ? 2001UL : 4001UL);
            Window surface = palette ? new ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow()
                : new ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow();
            var root = typeof(TopLevel).GetProperty("InputRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(surface)!;
            void FocusedEscape()
            {
                var assembly = typeof(AvaloniaObject).Assembly;
                var keyType = assembly.GetType("Avalonia.Input.Raw.RawKeyEventArgs")!;
                var device = Activator.CreateInstance(assembly.GetType("Avalonia.Input.KeyboardDevice")!)!;
                var key = (RawInputEventArgs)Activator.CreateInstance(keyType, device, first, root,
                    RawKeyEventType.KeyDown, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null, KeyDeviceType.Keyboard)!;
                var locator = assembly.GetType("Avalonia.AvaloniaLocator")!.GetProperty("Current")!.GetValue(null)!;
                var inputType = assembly.GetType("Avalonia.Input.IInputManager")!;
                var input = locator.GetType().GetMethod("GetService", new[] { typeof(Type) })!.Invoke(locator, new object[] { inputType })!;
                var stream = inputType.GetProperty("PreProcess")!.GetValue(input)!;
                stream.GetType().GetMethod("OnNext")!.Invoke(stream, new object[] { key });
                Assert.True(EscapeClaim.SurfaceOwnsEscape(first));
            }
            Win32Input.EscapeState = () => (true, !palette && open, palette && open);
            Win32Input.PostEscape = action => action();
            Win32Input.TryVideoEscape = () => { video++; return true; };
            (bool, int, int)? Next()
            {
                switch (step++)
                {
                    case 0:
                        if (uiFirst) FocusedEscape();
                        stamp = first; return (true, 9, 9);
                    case 1:
                        if (!uiFirst) FocusedEscape();
                        open = false; stamp = first + 1; return (false, 9, 9);
                    case 2:
                        Assert.Equal(0, video); // first press belonged to the surface
                        stamp = first + 2; return (true, 9, 9);
                    case 3:
                        stamp = first + 3; return (false, 9, 9);
                    default: return null;
                }
            }
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => leash++, Next, injectedTime: () => stamp);
            Assert.Equal(1, video); // distinct second press belongs to the video, even within 350 ms
            Assert.Equal(1, leash); // second press did not reach the leash
        }
        finally
        {
            (s.PanicKeyEnabled, s.PanicKey) = oldPanic;
            X11PanicKey.BindKeycodeForTest(oldCode);
            Win32Input.EscapeState = oldState;
            Win32Input.PostEscape = oldPost;
            Win32Input.TryVideoEscape = oldTry;
        }
    });

    [Fact]
    public async Task Unfocused_escape_reaches_the_video_grace_door_without_stopping_the_engine()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var o = MandatoryVideoOverlay.Instance;
            var saved = o.Scheduler;
            var s = CoreSettings.Current;
            var original = (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled);
            var oldAvailable = o.PlayerAvailableOverride;
            var oldPause = o.SetPlayerPauseOverride;
            var oldPost = Win32Input.PostEscape;
            var surfaces = (System.Collections.Generic.List<MandatoryVideoOverlay.Surface>)typeof(MandatoryVideoOverlay)
                .GetField("_surfaces", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(o)!;
            var lastGrace = typeof(MandatoryVideoOverlay).GetField("_lastGraceUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var consumed = typeof(MandatoryVideoOverlay).GetField("_graceConsumed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var v = new MandatoryVideoScheduler(o, new FrozenClock(), library: () => new[] { "/v/clip.mp4" });
            o.Scheduler = v;
            try
            {
                (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled) = (true, "F12", false, false);
                lastGrace.SetValue(o, default(DateTime));
                consumed.SetValue(o, false);
                bool decoderPaused = false;
                o.PlayerAvailableOverride = () => true;
                o.SetPlayerPauseOverride = value => decoderPaused = value;
                var layer = new Canvas();
                var grace = new Border { IsVisible = false };
                surfaces.Add(new MandatoryVideoOverlay.Surface(new Window(), new Image(), new Border(), new Image(),
                    layer, grace, new TextBlock(), null!));
                Assert.True(v.Trigger(false));
                var sent = false;
                X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { }, () => sent ? null : (sent = true, 9, 9));
                Dispatcher.UIThread.RunJobs();
                Assert.True(o.GracePaused);
                Assert.True(decoderPaused);
                Assert.True(grace.IsVisible);
                Assert.False(layer.IsHitTestVisible);
                Assert.True(v.IsPlaying);
                o.ResumeFromGrace("test");
                Assert.False(decoderPaused);
                Assert.False(o.GracePaused);
                v.Stop();
                Assert.Empty(o.Windows);
            }
            finally
            {
                v.Stop();
                o.PlayerAvailableOverride = oldAvailable;
                o.SetPlayerPauseOverride = oldPause;
                o.Scheduler = saved;
                Win32Input.PostEscape = oldPost;
                (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled) = original;
            }
            return Task.CompletedTask;
        });
    }
}
