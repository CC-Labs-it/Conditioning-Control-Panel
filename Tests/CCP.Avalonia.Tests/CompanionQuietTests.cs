using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionQuietTests
{
    [Fact]
    public Task QuietBarksKeepTheBubbleAndAnswersKeepTheirVoice() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        CoreSession.IsSessionRunningProvider = () => false;
        var s = CoreSettings.Current;
        s.AvatarMuted = false;
        s.CompanionSpeaksIdleWhenEngineOff = false;
        var tube = new AvatarTubeWindow(null);
        var audio = 0;
        tube.SpeechAudioPlayer = (_, _, _, done) => { audio++; done?.Invoke(); };
        try
        {
            tube.Show();
            AvApp.WindowAwareness.Stop();
            BarkHead.Speak(new BarkSpeech("sensing", null, true, null, false, Unprompted: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("sensing", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(0, audio);
            BarkHead.Speak(new BarkSpeech("answer", null, true, null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, audio);
            s.CompanionSpeaksIdleWhenEngineOff = true;
            tube.GigglePriority("opt in", unprompted: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, audio);
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QueuedSensingAndAwarenessStaySilent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        CoreSession.IsSessionRunningProvider = () => false;
        CoreSettings.Current.AvatarMuted = false;
        CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff = false;
        CoreSettings.Current.AiChatEnabled = false;
        var tube = new AvatarTubeWindow(null);
        var audio = 0;
        tube.SpeechAudioPlayer = (_, _, _, done) => { audio++; done?.Invoke(); };
        Action? pending = null;
        tube.SpeechDelayScheduler = (_, fire) => { pending = fire; return () => { }; };
        try
        {
            tube.Show();
            AvApp.WindowAwareness.Stop();
            tube.Giggle("first");
            tube.Giggle("queued sensing", unprompted: true);
            tube.EndSpeechBubble();
            audio = 0;
            Assert.NotNull(pending);
            pending!();
            Assert.Equal("queued sensing", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(0, audio);
            tube.EndSpeechBubble();
            tube._startupTime = DateTime.MinValue;
            foreach (var field in new[] { "_lastReactionTime", "_lastStillOnTime" })
                typeof(WindowAwarenessService).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(AvApp.WindowAwareness, DateTime.MinValue);
            var change = new ActivityChangedEventArgs(ActivityCategory.Gaming, ActivityCategory.Unknown, "Steam", "Steam");
            await tube.ReactToActivityAsync(change);
            pending?.Invoke();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(0, audio);
            tube.EndSpeechBubble();
            await tube.ReactStillOnAsync(change);
            pending?.Invoke();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(0, audio);
        }
        finally { tube.Close(); }
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
