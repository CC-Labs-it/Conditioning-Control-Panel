using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class TubeTriggerTests
{
    [Fact]
    public Task TimerFollowsSettingsAndTicksOnlyWhenEnabled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, interval, triggers, muted) = (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted);
        s.TriggerModeEnabled = false; s.AvatarMuted = true; s.CustomTriggers = new() { "test" };
        var tube = new AvatarTubeWindow(null);
        var pulses = 0;
        tube.TriggerHaptic = _ => pulses++;
        try
        {
            PanicAftermath.EndQuiet(); tube.StartTriggerMode();
            Assert.Null(tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(0, pulses);
            s.TriggerIntervalSeconds = 23; s.TriggerModeEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal  (23, tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(1, pulses);
            s.TriggerIntervalSeconds = 37; Dispatcher.UIThread.RunJobs();
            Assert.Equal(37, tube.TriggerInterval);
            s.TriggerModeEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Null(tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(1, pulses);
        }
        finally
        {
            tube.StopTriggerMode(); tube.Close(); PanicAftermath.EndQuiet();
            s.TriggerModeEnabled = enabled; s.TriggerIntervalSeconds = interval; s.CustomTriggers = triggers; s.AvatarMuted = muted;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicSuppressesTriggerHapticsEvenWhenMuted() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = settings.AvatarMuted;
        settings.AvatarMuted = true;
        var tube = new AvatarTubeWindow(null);
        var pulses = 0;
        tube.TriggerHaptic = _ => pulses++;
        try
        {
            PanicAftermath.EndQuiet();
            tube.ShowTrigger("test");
            Assert.Equal(1, pulses);
            PanicAftermath.ArmQuiet();
            tube.ShowTrigger("test");
            Assert.Equal(1, pulses);
        }
        finally { PanicAftermath.EndQuiet(); settings.AvatarMuted = old; tube.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task FirstTickRestartsAndStops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, triggers, muted) = (s.TriggerModeEnabled, s.CustomTriggers, s.AvatarMuted);
        s.TriggerModeEnabled = true; s.CustomTriggers = new() { "test" }; s.AvatarMuted = true;
        var tube = new AvatarTubeWindow(null);
        var pending = new List<(TimeSpan Delay, Action Fire)>();
        tube.SpeechDelayScheduler = (delay, fire) => { pending.Add((delay, fire)); return () => { }; };
        int pulses = 0; tube.TriggerHaptic = _ => pulses++;
        try
        {
            PanicAftermath.EndQuiet(); tube.StartTriggerMode();
            Assert.Equal(TimeSpan.FromSeconds(2), pending[0].Delay);
            tube.RestartTriggerTimer(); pending[0].Fire(); Assert.Equal(0, pulses);
            Assert.Equal(TimeSpan.FromSeconds(2), pending[1].Delay);
            pending[1].Fire(); Assert.Equal(1, pulses);
            tube.RestartTriggerTimer(); tube.StopTriggerMode(); pending[2].Fire(); Assert.Equal(1, pulses);
        }
        finally { tube.StopTriggerMode(); tube.Close(); PanicAftermath.EndQuiet(); s.TriggerModeEnabled = enabled; s.CustomTriggers = triggers; s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LeadInChecksPanicAndPlaysAudioOnlyAfterDelay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; var muted = s.AvatarMuted; s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null); tube.TriggerVisibilityOverride = () => true;
        var pending = new List<(TimeSpan Delay, Action Fire)>();
        tube.SpeechDelayScheduler = (delay, fire) => { bool cancelled = false; pending.Add((delay, () => { if (!cancelled) fire(); })); return () => cancelled = true; };
        int audio = 0, haptic = 0; tube.TriggerAudio = _ => audio++; tube.TriggerHaptic = _ => haptic++;
        try
        {
            PanicAftermath.EndQuiet(); tube.ShowTrigger("hello");
            Assert.Equal(1, haptic); Assert.Equal(0, audio); Assert.Equal(TimeSpan.FromSeconds(.6), pending[0].Delay);
            PanicAftermath.ArmQuiet(); pending[0].Fire(); Assert.Equal(0, audio);
            PanicAftermath.EndQuiet(); tube.ShowTrigger("hello"); pending[1].Fire(); Assert.Equal(1, audio);
            Assert.Equal(75, AvatarTubeWindow.TriggerStepMs(5));
            Assert.Equal(5.625, AvatarTubeWindow.TriggerDuration(5), 3);
            Assert.Equal(5.625, tube.TriggerDisplayInterval!.Value, 3);
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            Assert.Equal("", text.Text);
            var tick = tube.TriggerTypewriterStep; Assert.NotNull(tick);
            tick(); Assert.Equal("h", text.Text);
            tick(); Assert.Equal("he", text.Text);
            tick(); tick(); tick();
            Assert.Equal("hello", text.Text);
            Assert.Null(tube.TriggerTypewriterStep);
            tube.EndSpeechBubble();
        }
        finally { tube.PanicSilence(); tube.Close(); PanicAftermath.EndQuiet(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public void VisibilityFollowsEnabledParentAndDetach()
    {
        Assert.False(AvatarTubeWindow.TriggerVisible(false, true, false, true, false, false));
        Assert.False(AvatarTubeWindow.TriggerVisible(true, false, false, true, false, false));
        Assert.False(AvatarTubeWindow.TriggerVisible(true, true, true, false, false, false));
        Assert.False(AvatarTubeWindow.TriggerVisible(true, true, true, true, true, false));
        Assert.False(AvatarTubeWindow.TriggerVisible(true, true, true, true, false, true));
        Assert.True(AvatarTubeWindow.TriggerVisible(true, true, true, true, false, false));
        Assert.True(AvatarTubeWindow.TriggerVisible(true, true, false, false, true, true));
    }
    [Fact]
    public Task HiddenAndMutedTriggersAreHapticOnly() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; var oldMute = s.AvatarMuted;
        var tube = new AvatarTubeWindow(null);
        var visible = false; int pulses = 0, audio = 0, delays = 0;
        tube.TriggerVisibilityOverride = () => visible;
        tube.TriggerHaptic = _ => pulses++;
        tube.TriggerAudio = _ => audio++;
        tube.SpeechDelayScheduler = (_, _) => { delays++; return () => { }; };
        try
        {
            PanicAftermath.EndQuiet(); s.AvatarMuted = false;
            tube.ShowTrigger("hidden");
            visible = true; s.AvatarMuted = true;
            tube.ShowTrigger("muted");
            Assert.Equal(2, pulses); Assert.Equal(0, audio); Assert.Equal(0, delays);
        }
        finally { s.AvatarMuted = oldMute; PanicAftermath.EndQuiet(); tube.Close(); }
        return Task.CompletedTask;
    });
    private static Task WithTrigger(Action<AvatarTubeWindow, Action> run) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; var muted = s.AvatarMuted; s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null) { TriggerVisibilityOverride = () => true, TriggerHaptic = _ => { }, TriggerAudio = _ => { } };
        Action fire = () => { }; tube.SpeechDelayScheduler = (_, action) => { fire = action; return () => { }; };
        try { PanicAftermath.EndQuiet(); run(tube, () => fire()); }
        finally { tube.PanicSilence(); tube.Close(); PanicAftermath.EndQuiet(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TriggerRetiresListeningBubble() => WithTrigger((tube, fire) =>
    {
        tube.ShowListeningBubble("Listening");
        tube.ShowTrigger("hello"); fire();
        tube.HideListeningBubble();
        Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        Assert.Null(typeof(AvatarTubeWindow).GetField("_listeningDotsTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(tube));
    });

    [Fact]
    public Task TriggerClearsPreviousAskCard() => WithTrigger((tube, fire) =>
    {
        tube.ShowAskCard(new AskCard(AskKind.Feedback, "Question?", new[] { new AskChoice("yes", "Yes", AskTone.Yes, AskAction.Loved) }, DateTime.UtcNow));
        var buttons = tube.FindControl<WrapPanel>("AskButtons")!;
        Assert.True(buttons.IsVisible); Assert.Single(buttons.Children);
        tube.EndSpeechBubble(); tube.ShowTrigger("different"); fire();
        Assert.False(buttons.IsVisible); Assert.Empty(buttons.Children);
    });

    [Fact]
    public Task TriggerRestoresClickableLinksAfterTyping() => WithTrigger((tube, fire) =>
    {
        var oldNavigate = CompanionLinkLauncher.Navigate; string? opened = null;
        CompanionLinkLauncher.Navigate = url => { opened = url; return true; };
        try
        {
            tube.ShowTrigger("Try [this](https://example.com/watch)"); fire();
            while (tube.TriggerTypewriterStep is { } tick) tick();
            var inlines = tube.FindControl<TextBlock>("TxtSpeech")!.Inlines!;
            var button = Assert.IsType<SafeHyperlinkButton>(Assert.Single(inlines.OfType<InlineUIContainer>()).Child);
            button.Command!.Execute(null);
            Assert.Equal("https://example.com/watch", opened);
            var video = ConditioningControlPanel.Services.HypnotubeDefaultLinks.KnownVideoTitles.First(x => x.Key.Length >= 10 && x.Value.StartsWith("https://"));
            tube.EndSpeechBubble(); tube.ShowTrigger(video.Key); fire();
            while (tube.TriggerTypewriterStep is { } videoTick) videoTick();
            var videoLink = Assert.IsType<SafeHyperlinkButton>(Assert.Single(tube.FindControl<TextBlock>("TxtSpeech")!.Inlines!.OfType<InlineUIContainer>()).Child);
            videoLink.Command!.Execute(null);
            Assert.Equal(video.Value, opened);
            tube.EndSpeechBubble(); tube.ShowTrigger("'https://example.com/raw'"); fire();
            while (tube.TriggerTypewriterStep is { } rawTick) rawTick();
            var rawLink = Assert.IsType<SafeHyperlinkButton>(Assert.Single(tube.FindControl<TextBlock>("TxtSpeech")!.Inlines!.OfType<InlineUIContainer>()).Child);
            rawLink.Command!.Execute(null); Assert.Equal("https://example.com/raw", opened);
            tube.EndSpeechBubble(); tube.ShowTrigger("next"); fire(); var staleTick = tube.TriggerTypewriterStep; tube.StartThinkingAnimation();
            Assert.Null(tube.TriggerTypewriterStep); staleTick?.Invoke(); tube.StopThinkingAnimation();
        }
        finally { CompanionLinkLauncher.Navigate = oldNavigate; }
    });
}
