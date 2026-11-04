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

    // Each listener test restores the shared hooks even if an assertion fails.
    private sealed class EscapeHooks : IDisposable
    {
        private readonly int code = X11PanicKey.BoundKeycode;
        private readonly ConditioningControlPanel.Models.AppSettings settings = CoreSettings.Current;
        private readonly (bool, string) panic = (CoreSettings.Current.PanicKeyEnabled, CoreSettings.Current.PanicKey);
        private readonly Func<(bool Video, bool LockCard, bool Palette)> state = Win32Input.EscapeState;
        private readonly Action<Action> post = Win32Input.PostEscape;
        private readonly Func<bool> attempt = Win32Input.TryVideoEscape;
        public void Dispose()
        {
            X11PanicKey.BindKeycodeForTest(code);
            (settings.PanicKeyEnabled, settings.PanicKey) = panic;
            Win32Input.EscapeState = state; Win32Input.PostEscape = post; Win32Input.TryVideoEscape = attempt;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Held_escape_checks_surface_and_lost_release(bool surfaceFirst)
    {
        using var hooks = new EscapeHooks(); var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape");
        bool surface = surfaceFirst, down = true, video = true; int calls = 0, step = 0;
        using var checkedKey = new System.Threading.ManualResetEventSlim(); using var delivered = new System.Threading.ManualResetEventSlim();
        Win32Input.EscapeState = () => (video, false, surface);
        Win32Input.PostEscape = action => action();
        Win32Input.TryVideoEscape = () => { System.Threading.Interlocked.Increment(ref calls); delivered.Set(); return true; };
        (bool, int, int)? Next()
        {
            if (step++ == 0) return (true, 9, 9);
            if (surfaceFirst) surface = false;
            else { video = false; down = false; } // release was lost as the first video closed
            Assert.True(checkedKey.Wait(2000));
            if (surfaceFirst) { Assert.True(delivered.Wait(2000)); down = false; }
            video = !surfaceFirst; // a new video must not receive the stale repeat
            System.Threading.Thread.Sleep(150);
            Assert.True(surfaceFirst ? calls >= 1 : calls == 1);
            return null;
        }
        X11PanicKey.ListenRawEvents(Next, () => { }, repeatRate: _ => (20, 30), isDown: _ => { checkedKey.Set(); return down; });
    }

    [Fact]
    public void Raw_escape_retains_ownership_until_release()
    {
        using var hooks = new EscapeHooks(); var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape"); X11PanicKey.BindKeycodeForTest(9);
        bool video = true, leashTaskActive = true; int deliveries = 0, panic = 0;
        Win32Input.EscapeState = () => (video, false, false);
        Win32Input.PostEscape = action => action(); Win32Input.TryVideoEscape = () => { deliveries++; return true; };
        var events = new (bool, int, int)[] { (true, 10, 9), (false, 10, 9), (true, 0, 0),
            (true, 9, 9), (true, 9, 9), (false, 10, 9), (true, 9, 9), (false, 9, 9), (true, 9, 9) };
        int i = 0;
        (bool, int, int)? Next()
        {
            if (i == 3) Assert.Equal(0, deliveries);
            if (i == 7) Assert.Equal(1, deliveries);
            if (i == 8) { System.Threading.Thread.Sleep(650); Assert.Equal(1, deliveries); } // release stopped repeats
            return i < events.Length ? events[i++] : null;
        }
        X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { panic++; leashTaskActive = false; }, Next);
        Assert.Equal(2, deliveries); // one delivery per physical press
        Assert.Equal(0, panic);      // video owns both, even when panic is bound to Escape
        Assert.True(leashTaskActive); // the leash path was never invoked

        // When panic can run, WPF leaves Escape to panic instead of the video.
        (s.PanicKeyEnabled, s.PanicKey) = (true, "Escape"); i = 0;
        using var repeats = new System.Threading.CountdownEvent(3);
        (bool, int, int)? PanicNext() => i++ switch
        {
            0 => (true, 9, 9),
            1 => repeats.Wait(2000) ? (true, 9, 9) : throw new TimeoutException("repeat clock did not fire"),
            2 => (false, 9, 9),
            3 => (true, 9, 9),
            _ => null
        };
        X11PanicKey.ListenRawEvents(PanicNext, () => { panic++; leashTaskActive = false; }, repeatRate: _ => (10, 10),
            isDown: _ => { if (repeats.CurrentCount > 0) repeats.Signal(); return true; });
        Assert.Equal(2, panic); Assert.False(leashTaskActive); Assert.Equal(2, deliveries);
    }

    [Fact]
    public void Listener_snapshots_palette_ownership_and_rechecks_video_on_delivery()
    {
        using var hooks = new EscapeHooks(); var s = CoreSettings.Current;
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
        Assert.Empty(pending); Assert.Equal(1, panic); Assert.Equal(0, deliveries);

        Press();                         // video owns it at the event
        palette = true;                  // palette opens before delivery
        Assert.Single(pending); pending.Dequeue()();
        Assert.Equal(1, deliveries); Assert.Equal(1, panic);

        palette = false; Press();
        video = false;                   // clip stopped before queued delivery
        pending.Dequeue()();
        Assert.Equal(1, deliveries); Assert.Equal(1, attempts); Assert.Empty(pending);

        video = true; card = true;
        Press();                         // lock card also outranks the video
        Assert.Empty(pending); Assert.Equal(2, panic);
    }

    [Fact]
    public void Listener_keeps_panic_available_if_the_ownership_snapshot_throws()
    {
        using var hooks = new EscapeHooks();
        X11PanicKey.BindKeycodeForTest(9);
        Win32Input.EscapeState = () => throw new InvalidOperationException("snapshot failed");
        bool sent = false; int panic = 0;
        X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => panic++,
            () => sent ? null : (sent = true, 9, 9));
        Assert.Equal(1, panic);
    }

    [Fact]
    public void Listener_does_not_deliver_a_queued_escape_after_video_cleanup()
    {
        using var hooks = new EscapeHooks(); var settings = CoreSettings.Current;
        (settings.PanicKeyEnabled, settings.PanicKey) = (true, "F12");
        bool video = true; int attempts = 0;
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
        EscapeClaim.StartTracking(); EscapeClaim.StartTracking(trackX11: true);
        using var hooks = new EscapeHooks(); var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (false, "Escape");
        X11PanicKey.BindKeycodeForTest(9);
        bool open = !uiFirst; int video = 0, leash = 0, step = 0;
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
    });

    [Fact]
    public async Task Held_unfocused_escape_pauses_then_dismisses_video()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var o = MandatoryVideoOverlay.Instance;
            var saved = o.Scheduler; var s = CoreSettings.Current;
            var original = (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled);
            var oldAvailable = o.PlayerAvailableOverride; var oldPause = o.SetPlayerPauseOverride; var oldPost = Win32Input.PostEscape;
            var surfaces = (System.Collections.Generic.List<MandatoryVideoOverlay.Surface>)typeof(MandatoryVideoOverlay)
                .GetField("_surfaces", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(o)!;
            var lastGrace = typeof(MandatoryVideoOverlay).GetField("_lastGraceUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var consumed = typeof(MandatoryVideoOverlay).GetField("_graceConsumed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var v = new MandatoryVideoScheduler(o, new FrozenClock(), library: () => new[] { "/v/clip.mp4" });
            o.Scheduler = v;
            try
            {
                (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled) = (true, "F12", false, false);
                lastGrace.SetValue(o, default(DateTime)); consumed.SetValue(o, false);
                bool decoderPaused = false;
                o.PlayerAvailableOverride = () => true;
                o.SetPlayerPauseOverride = value => decoderPaused = value;
                var layer = new Canvas(); var grace = new Border { IsVisible = false };
                surfaces.Add(new MandatoryVideoOverlay.Surface(new Window(), new Image(), new Border(), new Image(),
                    layer, grace, new TextBlock(), null!));
                Assert.True(v.Trigger(false));
                int step = 0;
                (bool, int, int)? Next()
                {
                    if (step++ == 0) return (true, 9, 9);
                    if (step == 2)
                    {
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(o.GracePaused); Assert.True(decoderPaused); Assert.True(grace.IsVisible);
                        Assert.False(layer.IsHitTestVisible); Assert.True(v.IsPlaying);
                        o.ResumeFromGrace("test"); Assert.False(decoderPaused); Assert.False(o.GracePaused);
                        lastGrace.SetValue(o, default(DateTime)); consumed.SetValue(o, false);
                        return (false, 9, 9);
                    }
                    if (step == 3) return (true, 9, 9);
                    if (step == 4)
                    {
                        Dispatcher.UIThread.RunJobs(); Assert.True(o.GracePaused); Assert.True(v.IsPlaying);
                        System.Threading.Thread.Sleep(1000); Dispatcher.UIThread.RunJobs();
                        return (false, 9, 9);
                    }
                    return null;
                }
                X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { }, Next);
                Assert.False(v.IsPlaying);
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
