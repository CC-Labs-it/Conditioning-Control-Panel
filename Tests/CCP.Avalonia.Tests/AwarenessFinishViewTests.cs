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
    public void AppChipsRefreshOnShowScopeChangeAndCommitAndAddExactlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggerAppScope, s.KeywordTriggerApps, s.KeywordTriggersEnabled,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        Window? host = null;
        var engine = KeywordTriggerHead.Engine;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.KeywordTriggersEnabled = true;
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            s.KeywordTriggerApps = engine.RecentForegroundApps.ToList(); // don't inherit unrelated suggestions
            engine.Start();
            engine.RememberForeground("chips-editor", false);
            engine.RememberForeground("chips-browser", false);
            var view = new AwarenessTabView();
            host = new Window { Content = view }; host.Show(); Dispatcher.UIThread.RunJobs();
            var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            Assert.Equal(new[] { "+ chips-browser", "+ chips-editor" }, panel.Children.OfType<Button>().Select(b => b.Content));
            var browser = panel.Children.OfType<Button>().First();
            Assert.Equal("Add 'chips-browser' to the list", ToolTip.GetTip(browser));
            browser.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            browser.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // a stale click cannot duplicate a saved app
            Assert.Single(s.KeywordTriggerApps.Where(a => a == "chips-browser"));
            Assert.Equal("+ chips-editor", Assert.IsType<Button>(Assert.Single(panel.Children)).Content);
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            box.Text = string.Join(", ", s.KeywordTriggerApps) + ", CHIPS-EDITOR.EXE";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Empty(panel.Children);
            Assert.False(view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!.IsVisible);
            s.KeywordTriggerApps = new();
            var combo = view.FindControl<ComboBox>("CmbAwarenessAppScope")!;
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "ExceptListed");
            Assert.NotEmpty(panel.Children);
            view.IsVisible = false;
            engine.RememberForeground("chips-new", false);
            view.IsVisible = true;
            Assert.Equal("+ chips-new", ((Button)panel.Children[0]).Content);
        }
        finally
        {
            host?.Close(); KeywordTriggerHead.Stop();
            (s.KeywordTriggerAppScope, s.KeywordTriggerApps, s.KeywordTriggersEnabled,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
    });

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
            CoreSettings.Current.MasterVolume = 100;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finished = done; return () => { }; };
            KeywordAvatarComment.ScheduleRetry = (delay, fire) =>
            { Assert.Equal(TimeSpan.FromMilliseconds(750), delay); pending.Enqueue(fire); };
            tube = new AvatarTubeWindow(null); tube.Show(); Dispatcher.UIThread.RunJobs();
            // SetupWithoutStarting has no desktop lifetime/window list: provide its shown tube.
            KeywordAvatarComment.TubeSource = () => tube;
            tube.GigglePriority("original", playSound: false, aiGenerated: false, phraseAudioPath: file);
            tube.EndSpeechBubble();
            Assert.False(tube.IsSpeaking); Assert.True(tube.IsSpeakingAudio);
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
                finished!(); Dispatcher.UIThread.RunJobs(); Assert.False(tube.IsSpeakingAudio);
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
