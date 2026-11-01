using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11VideoEscapeTests
{
    [Fact]
    public void Raw_escape_routes_once_per_press_and_releases_without_consuming_other_keys()
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
            (bool, int, int)? Next()
            {
                if (i == 3) Assert.Equal(0, deliveries);
                if (i == 7) Assert.Equal(1, deliveries);
                return i < events.Length ? events[i++] : null;
            }
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { panic++; leashTaskActive = false; }, Next);
            Assert.Equal(2, deliveries); // one per physical Escape press
            Assert.Equal(0, panic);      // video owns both, even when panic is bound to Escape
            Assert.True(leashTaskActive); // the leash path was never invoked

            // When panic can run, WPF leaves Escape to panic instead of the video.
            (s.PanicKeyEnabled, s.PanicKey) = (true, "Escape");
            i = 0;
            var one = new (bool, int, int)[] { (true, 9, 9) };
            X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { panic++; leashTaskActive = false; }, () => i < one.Length ? one[i++] : null);
            Assert.Equal(1, panic);
            Assert.False(leashTaskActive);
            Assert.Equal(2, deliveries);
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

    [Fact]
    public void Palette_escape_grace_is_visible_to_the_listener_after_the_window_closes()
    {
        var field = typeof(ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow)
            .GetField("_escapeClosedAtUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var old = field.GetValue(null);
        try
        {
            field.SetValue(null, DateTime.UtcNow);
            Assert.True(Win32Input.EscapeState().Palette);
            field.SetValue(null, DateTime.UtcNow.AddSeconds(-2));
            Assert.False(Win32Input.EscapeState().Palette);
        }
        finally { field.SetValue(null, old); }
    }

    [Fact]
    public async Task Unfocused_escape_reaches_the_video_grace_door_without_stopping_the_engine()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-escape-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i testsrc=d=10:s=320x180:r=25 -pix_fmt yuv420p \"{clip}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
            }
            catch (Exception) { }
            if (!File.Exists(clip)) Assert.Skip("ffmpeg unavailable");
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc unavailable: " + e.Message); }

            var o = MandatoryVideoOverlay.Instance;
            var saved = o.Scheduler;
            var s = CoreSettings.Current;
            var original = (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled);
            (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled) = (true, "F12", false, false);
            var v = new MandatoryVideoScheduler(o, library: () => new[] { clip });
            o.Scheduler = v;
            try
            {
                await AvaloniaTestDispatcher.RunAsync(async () =>
                {
                    if (Application.Current is null)
                        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                    await MandatoryVideoOverlayTests.OpenClip(v, o, false);
                    var sent = false;
                    X11PanicKey.Listen(IntPtr.Zero, 0, () => null, () => { }, () => sent ? null : (sent = true, 9, 9));
                    await MandatoryVideoOverlayTests.Until(() => o.GracePaused);
                    Assert.True(o.GracePaused);
                    Assert.True(v.IsPlaying);
                    v.Stop();
                    Assert.Empty(o.Windows);
                });
            }
            finally
            {
                v.Stop();
                o.Scheduler = saved;
                (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll, s.AttentionChecksEnabled) = original;
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
