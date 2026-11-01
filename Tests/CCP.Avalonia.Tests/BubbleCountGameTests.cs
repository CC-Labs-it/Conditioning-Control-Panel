using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The bubble-count game headless with a real clip on the shared LibVLC: the Test trigger
/// opens the game after the lead-in and frames reach the window; the clip's end opens the answer
/// window and a correct answer completes the game with XP; strict falls open to the panic key with
/// no global listener and swallows it with one; engine Stop (panic) closes it. The clip is committed
/// (Assets/bubblecount-2s.mp4); only a missing libvlc skips, and never on Linux CI.</summary>
public sealed class BubbleCountGameTests
{
    [Fact]
    public async Task PlaysAnswersAndHonoursStrictAndPanic()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-bcount-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), clip);
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e)
            {
                // core-linux installs libvlc (build.yml), so a missing one there is a broken job, not a skip.
                if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
                Assert.Skip("libvlc not installed (apt install libvlc-dev vlc-plugin-base): " + e.Message);
            }

            var s = CoreSettings.Current;
            var saved = (s.PanicKeyEnabled, s.PanicKey, s.BubbleCountStrictLock, s.DualMonitorEnabled);
            (s.PanicKeyEnabled, s.PanicKey, s.DualMonitorEnabled) = (true, "F12", false);
            double xp = 0;
            var prevXp = CoreProgression.AddXPProvider;
            CoreProgression.AddXPProvider = (a, _) => xp += a;
            var host = BubbleCountHost.Instance;
            var b = new BubbleCountScheduler(host, library: () => new[] { clip });
            var real = host.Scheduler;
            var prevEngine = CoreEngine.BubbleCount;
            var listener = BubbleCountWindow.PanicListenerLive;
            host.Scheduler = b;
            CoreEngine.BubbleCount = b;
            try
            {
                await AvaloniaTestDispatcher.RunAsync(async () =>
                {
                    ShellTestPlatform.Ensure();

                    async Task Open(bool strict, Action<BubbleCountWindow>? assertPlaying = null)
                    {
                        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void OnFrame(BubbleCountWindow w)
                        {
                            if (presented.Task.IsCompleted || (BubbleCountWindow.Sink?.FrameCount ?? 0) < 3) return;
                            try
                            {
                                Assert.Same(w, Assert.Single(BubbleCountWindow.OpenWindows));
                                Assert.True(BubbleCountWindow.Sink!.FrameCount >= 3, "frames decoded");
                                Assert.Same(BubbleCountWindow.Sink.Bitmap, w.VideoImage.Source);
                                Assert.True(w.Topmost && w.IsVisible);
                                assertPlaying?.Invoke(w);
                                presented.SetResult();
                            }
                            catch (Exception e) { presented.SetException(e); }
                        }
                        BubbleCountWindow.FramePresented += OnFrame;
                        try
                        {
                            s.BubbleCountStrictLock = strict;
                            b.Trigger(forceTest: true);
                            Assert.True(b.IsBusy);
                            Assert.Empty(BubbleCountWindow.OpenWindows);        // the 800 ms lead-in
                            // Cold LibVLC may need 20 s. Assert during presentation, not after a delayed
                            // polling continuation: the two-second clip can already have ended then.
                            await presented.Task.WaitAsync(TimeSpan.FromSeconds(20));
                        }
                        finally { BubbleCountWindow.FramePresented -= OnFrame; }
                    }

                    // Non-strict: the 2 s clip ends, the answer window asks, the right count wins.
                    await Open(strict: false);
                    for (var i = 0; i < 400 && BubbleCountResultWindow.OpenWindows.Count == 0; i++) await Task.Delay(50);
                    var r = Assert.Single(BubbleCountResultWindow.OpenWindows);
                    Assert.Equal(2, BubbleCountWindow.LastVideoDurationSeconds, 0);   // LengthChanged replaced the 30 s fallback
                    r.FindControl<TextBox>("TxtAnswer")!.Text = r.CorrectAnswer.ToString();
                    r.FindControl<TextBox>("TxtAnswer")!.Focus();
                    r.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                    Assert.Equal(25, xp);                                 // 250 scaled by a 2 s clip
                    for (var i = 0; i < 60 && b.IsBusy; i++) await Task.Delay(50);
                    Assert.False(b.IsBusy);
                    Assert.Equal(25 + 10, xp);                            // + the service's 100, scaled
                    Assert.Empty(BubbleCountWindow.OpenWindows);

                    // Strict with a live listener: the panic key and Esc are swallowed; engine stop closes it.
                    BubbleCountWindow.PanicListenerLive = () => true;
                    await Open(strict: true, w =>
                    {
                        w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                        Assert.True(b.IsBusy && w.IsVisible, "strict game survives the panic key and Esc");
                        CoreEngine.Stop();
                        Assert.False(b.IsBusy);
                        Assert.Empty(BubbleCountWindow.OpenWindows);
                        Assert.Null(BubbleCountWindow.Sink);
                    });

                    // Strict with NO live listener (#875): the panic key must escape, not trap.
                    BubbleCountWindow.PanicListenerLive = () => false;
                    await Open(strict: true, w =>
                    {
                        w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                        Assert.False(b.IsBusy, "no listener: the panic key force-stops a strict game");
                        Assert.Empty(BubbleCountWindow.OpenWindows);
                        Assert.Empty(host.Messages);                          // a stop, not a WRONG! retry
                    });
                });
            }
            finally
            {
                b.ForceCleanup();
                host.Scheduler = real;
                CoreEngine.BubbleCount = prevEngine;
                BubbleCountWindow.PanicListenerLive = listener;
                CoreProgression.AddXPProvider = prevXp;
                (s.PanicKeyEnabled, s.PanicKey, s.BubbleCountStrictLock, s.DualMonitorEnabled) = saved;
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
