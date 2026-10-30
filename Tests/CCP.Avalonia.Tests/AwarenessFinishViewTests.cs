using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessFinishViewTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void CommentsWaitForBubbleAndVoiceBoundRetriesAndNeverSpeakAfterPanic() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var saved = (CoreAudio.PlayStoppableProvider, KeywordAvatarComment.ScheduleRetry, KeywordAvatarComment.TubeSource);
        var pending = new Queue<Action>();
        Action? finished = null;
        var file = Path.GetTempFileName();
        AvatarTubeWindow? tube = null;
        var volume = CoreSettings.Current.MasterVolume;
        try
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = 100;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finished = done; return () => { }; };
            KeywordAvatarComment.ScheduleRetry = (delay, fire) =>
            { Assert.Equal(TimeSpan.FromMilliseconds(750), delay); pending.Enqueue(fire); };
            tube = new AvatarTubeWindow(null); tube.Show(); Dispatcher.UIThread.RunJobs();
            // SetupWithoutStarting has no desktop lifetime/window list: provide its shown tube.
            KeywordAvatarComment.TubeSource = () => tube;
            tube.GigglePriority("original", playSound: false, aiGenerated: false, phraseAudioPath: file);
            tube.EndSpeechBubble();
            Assert.False(tube.IsSpeaking); Assert.False(tube.SpokenAudioDone.IsCompleted);
            // A real demo routes through the default delivery, not a captured test callback.
            var s = CoreSettings.Current;
            var entitlement = CoreEntitlement.HasPremiumProvider;
            var phrases = s.CustomCompanionPhrases;
            var ai = ConditioningControlPanel.Avalonia.App.Ai;
            try
            {
                CoreEntitlement.HasPremiumProvider = () => true;
                ConditioningControlPanel.Avalonia.App.Ai = null;
                s.CustomCompanionPhrases = new() { new() { Id = "retry:test", Category = "retry-test", Text = "comment", Enabled = true } };
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs(); Assert.Single(pending);
                finished!(); Dispatcher.UIThread.RunJobs(); Assert.True(tube.SpokenAudioDone.IsCompleted);
                pending.Dequeue()(); Assert.Equal("comment", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < 16; i++) pending.Dequeue()();
                Assert.Empty(pending); // ~12 seconds of nonstop speech drops the comment
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs(); Assert.Single(pending);
                Assert.Single(PanicSurfaces.All, x => x.Id == "keyword-comments").Stop(null);
                tube.PanicSilence(); pending.Dequeue()();
                Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            }
            finally { CoreEntitlement.HasPremiumProvider = entitlement; s.CustomCompanionPhrases = phrases; ConditioningControlPanel.Avalonia.App.Ai = ai; }
        }
        finally
        {
            tube?.Close(); KeywordAvatarComment.CancelPending(); File.Delete(file);
            CoreSettings.Current.MasterVolume = volume;
            (CoreAudio.PlayStoppableProvider, KeywordAvatarComment.ScheduleRetry, KeywordAvatarComment.TubeSource) = saved;
        }
    });
}
