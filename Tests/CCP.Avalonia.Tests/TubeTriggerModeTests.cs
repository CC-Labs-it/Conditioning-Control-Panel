using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The tube's Trigger mode loop (WPF AvatarTube/AvatarTubeWindow.Speech.cs:1594-1635, 2047-2160):
/// started on open when enabled at the user's interval, first word 2 s in, a random custom trigger per
/// tick unless a line is up, haptic-only while muted, and restarted/stopped by the setting.</summary>
public sealed class TubeTriggerModeTests
{
    [Fact]
    public Task TriggerLoopShowsCustomTriggersAndFollowsTheSetting() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (on, interval, triggers, muted) = (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => static () => { };
        s.TriggerModeEnabled = true; s.TriggerIntervalSeconds = 45; s.CustomTriggers = new List<string> { "SLEEP" }; s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null);
        var loops = new List<(TimeSpan Interval, Action Tick, Func<bool> Stopped)>();
        var delays = new List<(TimeSpan Delay, Action Fire)>();
        tube.TriggerLoopScheduler = (every, tick) => { var stopped = false; loops.Add((every, tick, () => stopped)); return () => stopped = true; };
        tube.SpeechDelayScheduler = (delay, fire) => { var c = false; delays.Add((delay, () => { if (!c) fire(); })); return () => c = true; };
        tube.SpeechClock = new Stepped();
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var loop = Assert.Single(loops);
            Assert.Equal(45, loop.Interval.TotalSeconds);
            Assert.Equal(2, Assert.Single(delays).Delay.TotalSeconds);   // first word 2 s after start
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            delays[0].Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("SLEEP", text.Text);
            Assert.True(tube.IsSpeaking);
            text.Text = "";
            loop.Tick();                                   // a line is up: the tick is skipped, not queued
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal("", text.Text);
            Assert.Equal(5.25, AvatarTubeWindow.TriggerDisplaySeconds("SLEEP"), 3);

            tube.EndSpeechBubble();
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            s.AvatarMuted = true;                          // muted: haptic only, no bubble
            loop.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", text.Text);
            Assert.False(tube.IsSpeaking);

            s.TriggerIntervalSeconds = 120;
            tube.RestartTriggerTimer();
            Dispatcher.UIThread.RunJobs();
            Assert.True(loop.Stopped());
            Assert.Equal(120, loops[^1].Interval.TotalSeconds);
            s.TriggerModeEnabled = false;
            tube.RestartTriggerTimer();
            Dispatcher.UIThread.RunJobs();
            Assert.True(loops[^1].Stopped());
            Assert.False(tube.TriggerLoopRunning);
        }
        finally
        {
            tube.Close();
            CoreAudio.PlayStoppableProvider = provider;
            (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted) = (on, interval, triggers, muted);
        }
        return Task.CompletedTask;
    });

    private sealed class Stepped : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
