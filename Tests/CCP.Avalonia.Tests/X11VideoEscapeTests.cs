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
        bool down = false;
        int calls = 0;
        void Send(bool press, int code, int escape = 9) =>
            X11PanicKey.RouteRawKey(press, code, escape, ref down, () => { }, () => calls++);

        Send(true, 10); Send(false, 10); Send(true, 0, 0);
        Assert.Equal(0, calls);
        Send(true, 9); Send(true, 9); Send(false, 10); Send(true, 9);
        Assert.Equal(1, calls);
        Send(false, 9); Send(true, 9);
        Assert.Equal(2, calls);

        var oldCode = X11PanicKey.BoundKeycode;
        try
        {
            X11PanicKey.BindKeycodeForTest(9);
            down = false;
            var order = "";
            X11PanicKey.RouteRawKey(true, 9, 9, ref down, () => order += "panic ", () => order += "video");
            Assert.Equal("panic video", order);
        }
        finally { X11PanicKey.BindKeycodeForTest(oldCode); }
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
                    bool down = false;
                    X11PanicKey.RouteRawKey(true, 9, 9, ref down, () => { }, Win32Input.QueueVideoEscape);
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
