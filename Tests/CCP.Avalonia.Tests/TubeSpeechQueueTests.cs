using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The tube's speech queue (WPF AvatarTube/AvatarTubeWindow.Speech.cs:35,157,180,240,325,1842):
/// presets wait their turn in order behind the line on screen, spaced by the gap the previous line
/// earned; a priority line and panic drop the queue; every new line cuts the previous clip.</summary>
public sealed class TubeSpeechQueueTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Captures the post-line gap instead of waiting it out (P08).</summary>
    private static List<(TimeSpan Delay, Action Fire, Func<bool> Cancelled)> CaptureDelays(AvatarTubeWindow tube)
    {
        var delays = new List<(TimeSpan, Action, Func<bool>)>();
        tube.SpeechDelayScheduler = (delay, fire) =>
        {
            var cancelled = false;
            delays.Add((delay, () => { if (!cancelled) fire(); }, () => cancelled));
            return () => cancelled = true;
        };
        return delays;
    }

    [Fact]
    public Task PresetsSpeakInOrderAfterTheEarnedGap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var muted = s.AvatarMuted;
        s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var delays = CaptureDelays(tube);
            var clock = new Clock();
            tube.SpeechClock = clock;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;

            tube.Giggle("one");
            tube.Giggle("two");
            tube.Giggle("three");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("one", text.Text);          // nothing was up: immediate
            Assert.Equal(2, tube.QueuedSpeechCount); // the rest wait, not replace

            tube.EndSpeechBubble();                   // "one"'s auto-hide
            Assert.Single(delays);
            Assert.Equal(2.0, delays[0].Delay.TotalSeconds, 1);   // MinSpeechDelaySeconds after a preset
            Assert.NotEqual("two", text.Text);
            delays[0].Fire();
            Assert.Equal("two", text.Text);

            tube.EndSpeechBubble();
            delays[1].Fire();
            Assert.Equal("three", text.Text);
            Assert.Equal(0, tube.QueuedSpeechCount);

            tube.EndSpeechBubble();
            Assert.Equal(2, delays.Count);            // empty queue: nothing more scheduled
            Assert.False(tube.IsSpeaking);

            // The gap is measured on the clock: 1.5 s in, a new preset waits the last 0.5 s ...
            clock.Now += TimeSpan.FromSeconds(1.5);
            tube.Giggle("four");
            Assert.Equal(3, delays.Count);
            Assert.Equal(0.5, delays[2].Delay.TotalSeconds, 3);
            delays[2].Fire();
            Assert.Equal("four", text.Text);
            tube.EndSpeechBubble();
            // ... and once the gap has passed it speaks at once.
            clock.Now += TimeSpan.FromSeconds(2.1);
            tube.Giggle("five");
            Assert.Equal(3, delays.Count);
            Assert.Equal("five", text.Text);

            // An AI line earns 2 + 5 s, plus 0.02 s a char past 100 (WPF Speech.cs:157).
            Assert.Equal(7.0, AvatarTubeWindow.RequiredDelayAfter(AvatarTubeWindow.SpeechSource.AI, 50), 3);
            Assert.Equal(4.0, AvatarTubeWindow.RequiredDelayAfter(AvatarTubeWindow.SpeechSource.Preset, 200), 3);
        }
        finally { tube.Close(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PriorityLineDropsTheQueueAndEveryLineCutsThePreviousClip() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var (volume, muted, voMuted) = (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        var live = new HashSet<int>();
        var next = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { var id = next++; live.Add(id); return () => live.Remove(id); };
        s.MasterVolume = 80; s.AvatarMuted = false; s.CompanionVoiceLinesMuted = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var delays = CaptureDelays(tube);
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;

            tube.Giggle("preset a");
            tube.Giggle("preset b");
            tube.EndSpeechBubble();                   // "preset b" now waits out its gap
            Dispatcher.UIThread.RunJobs();
            Assert.Single(delays);

            tube.GigglePriority("voiced", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(delays[0].Cancelled());       // the pending gap is dropped
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal("voiced", text.Text);
            Assert.Single(live);

            // A silent line still cuts the voiced one (WPF ShowGiggle StopSpokenAudio): one clip at a time.
            tube.GigglePriority("silent", playSound: false, aiGenerated: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(live);
        }
        finally
        {
            tube.Close();
            CoreAudio.PlayStoppableProvider = provider;
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (volume, muted, voMuted);
            File.Delete(clip);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF ShowListeningBubble (Speech.cs:719-721): the open mic drops the queue and the
    /// pending gap, so no queued line replaces "she's listening" or speaks later.</summary>
    [Fact]
    public Task ListeningBubbleDropsTheQueue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var muted = s.AvatarMuted;
        s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var delays = CaptureDelays(tube);
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.Giggle("a");
            tube.Giggle("b");
            tube.Giggle("c");
            tube.EndSpeechBubble();                   // "b" waits out its gap
            Dispatcher.UIThread.RunJobs();
            Assert.Single(delays);

            tube.ShowListeningBubble("listening");
            Dispatcher.UIThread.RunJobs();
            Assert.True(delays[0].Cancelled());
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal("listening", text.Text);
            tube.HideListeningBubble();
            Dispatcher.UIThread.RunJobs();
        }
        finally { tube.Close(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OrdinaryVoicedBarksWaitThePresetGapAndPlayTheirClipOnlyOnTheImmediatePath() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var old = (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        var played = new List<string>();
        CoreAudio.PlayStoppableProvider = (path, _, _, _, _) => { played.Add(path); return () => { }; };
        (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (80, false, false);
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var delays = CaptureDelays(tube);
            var clock = new Clock(); tube.SpeechClock = clock;
            var speech = new ConditioningControlPanel.Services.Bark.BarkSpeech("voiced", clip, false, null, false);
            ConditioningControlPanel.Avalonia.Platform.BarkHead.Speak(speech, tube);
            Assert.Equal(clip, Assert.Single(played));
            tube.EndSpeechBubble();
            ConditioningControlPanel.Avalonia.Platform.BarkHead.Speak(speech with { Text = "next" }, tube);
            Assert.Single(delays); // GigglePriority would have replaced immediately
            Assert.Single(played);
            delays[0].Fire();
            Assert.Equal("next", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Single(played); // WPF deliberately drops the clip on a delayed preset
        }
        finally
        {
            tube.Close(); CoreAudio.PlayStoppableProvider = provider;
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = old;
            File.Delete(clip);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task IdleBeatUsesTheSliderAndStopsOnTheRegisteredPanicPathAndClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.IdleGiggleIntervalSeconds, s.CompanionSpeaksIdleWhenEngineOff);
        s.AvatarEnabled = true; s.IdleGiggleIntervalSeconds = 180; s.CompanionSpeaksIdleWhenEngineOff = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.True(tube.IdleTimerRunning);
            Assert.Equal(System.TimeSpan.FromSeconds(180), tube.IdleInterval);
            s.IdleGiggleIntervalSeconds = 240;
            tube.IdleTick();
            Assert.Equal(System.TimeSpan.FromSeconds(240), tube.IdleInterval);
            Assert.False(AvatarTubeWindow.ShouldVoice(true));
            Assert.True(AvatarTubeWindow.ShouldVoice(false));
            s.CompanionSpeaksIdleWhenEngineOff = true;
            Assert.True(AvatarTubeWindow.ShouldVoice(true));
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Assert.False(tube.IdleTimerRunning);
            tube.IdleTick();
            Assert.False(tube.IsSpeaking);
            tube.RestartIdleTimer();
            tube.Close();
            Assert.False(tube.IdleTimerRunning);
        }
        finally
        {
            shell.Close();
            (s.AvatarEnabled, s.IdleGiggleIntervalSeconds, s.CompanionSpeaksIdleWhenEngineOff) = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF OnIdleTick -> ClearStaleSpeechLatch (Speech.cs:1614,3070): a bubble collapsed out from
    /// under the latch is cleared by the idle beat, but a pending gap still holds it.</summary>
    [Fact]
    public Task IdleBeatClearsAWedgedLatchButNotAPendingGap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var muted = s.AvatarMuted;
        s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var delays = CaptureDelays(tube);
            tube.RestartIdleTimer();
            var bubble = tube.FindControl<Border>("SpeechBubble")!;

            tube.Giggle("a");
            tube.Giggle("b");
            tube.EndSpeechBubble();                   // "b" waits out its gap: latch up, legitimately
            tube.IdleTick();
            Assert.True(tube.IsSpeaking);
            delays[0].Fire();
            Assert.True(bubble.IsVisible);

            bubble.IsVisible = false;                 // collapsed without its hide tick: wedged
            Assert.True(tube.IsSpeaking);
            tube.IdleTick();
            Assert.False(tube.IsSpeaking);
        }
        finally { tube.Close(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicClearsTheQueueSoNothingSpeaksAfterIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var (enabled, muted) = (s.AvatarEnabled, s.AvatarMuted);
        s.AvatarEnabled = true; s.AvatarMuted = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            var delays = CaptureDelays(tube);
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            var panic = PanicSurfaces.All.Single(x => x.Id == "tube-speech");

            tube.Giggle("first");
            tube.Giggle("queued");
            tube.Giggle("queued too");
            tube.EndSpeechBubble();                   // a gap is pending for "queued"
            Dispatcher.UIThread.RunJobs();
            Assert.Single(delays);

            panic.Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.True(delays[0].Cancelled());
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.False(tube.IsSpeaking);
            Assert.NotEqual("queued", text.Text);
        }
        finally
        {
            shell.Close();
            (s.AvatarEnabled, s.AvatarMuted) = (enabled, muted);
        }
        return Task.CompletedTask;
    });
}
