using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;
using ConditioningControlPanel.Services.Safety;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The tube's Trigger mode loop (WPF AvatarTube/AvatarTubeWindow.Speech.cs:1594-1635, 2047-2160):
/// started on open when enabled at the user's interval, first word 2 s in, a random custom trigger per
/// tick unless a line is up, haptic-only while muted, and restarted/stopped by the setting.</summary>
public sealed class TubeTriggerModeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TriggerLoopShowsCustomTriggersAndFollowsTheSetting(bool closeDuringTyping) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        PanicAftermath.EndQuiet();
        var s = CoreSettings.Current;
        var (on, interval, triggers, muted) = (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        var audio = 0;
        CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) => { if (tag == "whisper") audio++; return static () => { }; };
        s.SubAudioEnabled = true; s.SubAudioMuted = false;
        s.TriggerModeEnabled = true; s.TriggerIntervalSeconds = 45; s.CustomTriggers = new List<string> { "freeze" }; s.AvatarMuted = false;
        var (detached, enabled) = (s.AvatarTubeDetached, s.AvatarEnabled);
        s.AvatarTubeDetached = true; s.AvatarEnabled = true;
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
            Assert.NotEqual("freeze", text.Text); // no immediate trigger text or audio
            Assert.Equal(0, audio);
            Assert.Equal(0.6, delays[^1].Delay.TotalSeconds);
            delays[^1].Fire();
            Assert.Equal(1, audio);
            Assert.Equal("", text.Text);
            var typing = loops[^1];
            Assert.Equal(75, typing.Interval.TotalMilliseconds);
            typing.Tick();
            Assert.Equal("f", text.Text);
            for (var i = 1; i < 6; i++) typing.Tick();
            Assert.Equal("freeze", text.Text);
            Assert.True(typing.Stopped());
            Assert.Equal(TimeSpan.FromSeconds(5.75), tube.SpeechHideDelay); // 5.3 + 0.45 typing
            Assert.True(tube.IsSpeaking);
            text.Text = "";
            loop.Tick();                                   // a line is up: the tick is skipped, not queued
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal("", text.Text);
            Assert.Equal(5.75, AvatarTubeWindow.TriggerDisplaySeconds("freeze"), 3);

            tube.EndSpeechBubble();
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            s.AvatarMuted = true;                          // muted: haptic only, no bubble
            loop.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", text.Text);
            Assert.False(tube.IsSpeaking);

            // Panic refuses a ready tick, even while muted. Fail-proof: removing the guard
            // updates the speech end stamp, so the following unmuted tick cannot speak.
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            PanicAftermath.ArmQuiet();
            loop.Tick();
            PanicAftermath.EndQuiet();
            s.AvatarMuted = false;
            loop.Tick();
            delays[^1].Fire();
            for (var i = 0; i < 6; i++) loops[^1].Tick();
            Assert.Equal("freeze", text.Text);
            tube.EndSpeechBubble();
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            loop.Tick();
            var pendingLead = delays[^1];
            var beforePanic = audio;
            tube.PanicSilence();
            pendingLead.Fire();
            Assert.Equal(beforePanic, audio); // fail-proof: a cancelled lead-in cannot play
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            loop.Tick();
            delays[^1].Fire();
            var interruptedTyping = loops[^1];
            interruptedTyping.Tick();
            tube.PanicSilence();
            var afterPanic = text.Text;
            interruptedTyping.Tick();
            Assert.True(interruptedTyping.Stopped());
            Assert.Equal(afterPanic, text.Text); // fail-proof: even an already dispatched tick is stale

            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            loop.Tick();
            delays[^1].Fire();
            var replacedTyping = loops[^1];
            tube.GigglePriority("replacement", playSound: false);
            replacedTyping.Tick();
            Assert.True(replacedTyping.Stopped());
            Assert.Equal("replacement", text.Text);
            tube.EndSpeechBubble();

            // Linked-trigger regression: type the label, then render a real link, preserving surrounding text.
            s.CustomTriggers = new() { "Before [Watch](https://example.com) [Local](file:///tmp/a) after" };
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(8); // replacement earned the AI-source gap
            Assert.True(tube.IsSpeechReady());
            loop.Tick();
            delays[^1].Fire();
            var linkedTyping = loops[^1];
            linkedTyping.Tick();
            Assert.Equal("B", text.Text);
            for (var i = 1; i < "Before Watch [Local](file:///tmp/a) after".Length; i++) linkedTyping.Tick();
            Assert.True(linkedTyping.Stopped());
            var link = Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton>(Assert.Single(text.Inlines!.OfType<InlineUIContainer>()).Child);
            Assert.Equal("Watch", link.Content);
            Assert.Equal(new[] { "Before ", " ", "Local", " after" }, text.Inlines!.OfType<Run>().Select(r => r.Text));
            var previousNavigate = CompanionLinkLauncher.Navigate;
            string? opened = null;
            try
            {
                CompanionLinkLauncher.Navigate = url => { opened = url; return true; };
                typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(link, null);
                Assert.Equal("https://example.com/", opened); // fail-proof: a label without navigation fails
            }
            finally { CompanionLinkLauncher.Navigate = previousNavigate; }
            tube.EndSpeechBubble();
            s.CustomTriggers = new() { "freeze" }; // retain the close-path audio fail-proof

            s.TriggerIntervalSeconds = 120; // the settings event alone must restart the loop
            Dispatcher.UIThread.RunJobs();
            Assert.True(loop.Stopped());
            Assert.Equal(120, loops[^1].Interval.TotalSeconds);
            s.TriggerModeEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(loops[^1].Stopped());
            Assert.False(tube.TriggerLoopRunning);
            s.TriggerModeEnabled = true;
            ((Stepped)tube.SpeechClock).Now += TimeSpan.FromSeconds(3);
            delays[^1].Fire(); // first tick schedules the lead-in
            var pending = delays[^1];
            if (closeDuringTyping) pending.Fire();
            var closingAnimation = loops[^1];
            var beforeClose = audio;
            tube.Close();
            Assert.True(loops[^1].Stopped());
            text.Text = "closed";
            pending.Fire();
            closingAnimation.Tick();
            Assert.True(closingAnimation.Stopped());
            loop.Tick();
            Assert.Equal("closed", text.Text);
            Assert.Equal(beforeClose, audio);
            Assert.False(tube.TriggerLoopRunning);
        }
        finally
        {
            tube.Close();
            PanicAftermath.EndQuiet();
            (s.AvatarTubeDetached, s.AvatarEnabled) = (detached, enabled);
            CoreAudio.PlayStoppableProvider = provider;
            (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted) = (on, interval, triggers, muted);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(6, 450, 5.75)]
    [InlineData(100, 7000, 17)]
    [InlineData(200, 3500, 17.5)]
    [InlineData(1000, 2400, 16.4)]
    public void SlowTypingUsesTheWpfProfile(int length, int expectedMs, double displaySeconds)
    {
        Assert.Equal(expectedMs, AvatarTubeWindow.TriggerTypingMs(length));
        Assert.Equal(displaySeconds, AvatarTubeWindow.TriggerDisplaySeconds(new string('x', length)), 3);
    }

    private sealed class Stepped : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
