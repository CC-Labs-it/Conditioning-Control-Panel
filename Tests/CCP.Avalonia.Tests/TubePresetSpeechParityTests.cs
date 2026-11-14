using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TubePresetSpeechParityTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF Speech.cs:503-547,565-586,2936-3033: the reading clock starts after the slow typewriter.
    [Fact]
    public Task PresetTypesGraduallyAndKeepsItsReadingTime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = true;
        s.BubbleDurationSeconds = 2;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.Giggle("1234567890");
            Assert.Equal("", text.Text);
            Assert.Equal(TimeSpan.FromMilliseconds(75), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(2.75), tube.SpeechHideDelay);
            var stale = tube.TypewriterFrame!;
            stale();
            Assert.Equal("1", text.Text);
            tube.GigglePriority("replacement", playSound: false, aiGenerated: false);
            stale();
            Assert.Equal("replacement", text.Text); // an interrupted tick cannot overwrite a newer line

            tube.EndSpeechBubble();
            tube.SpeechDelayScheduler = (_, fire) => { fire(); return () => { }; };
            tube.Giggle("1234567890");
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.Equal("1234567890", text.Text);
            Assert.Null(tube.TypewriterFrame);

            tube.EndSpeechBubble();
            tube.Giggle(new string('a', 200));
            Assert.Equal(TimeSpan.FromMilliseconds(35), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(5.5), tube.SpeechHideDelay);
            tube.TypewriterFrame!();
            Assert.Equal("aa", text.Text); // two characters per tick at length 200
            while (tube.TypewriterFrame is { } mediumFrame) mediumFrame();
            Assert.Equal(new string('a', 200), text.Text);

            tube.EndSpeechBubble();
            tube.Giggle(new string('b', 400));
            Assert.Equal(TimeSpan.FromMilliseconds(24), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(4.4), tube.SpeechHideDelay);
            tube.TypewriterFrame!();
            Assert.Equal("bbbb", text.Text);
            while (tube.TypewriterFrame is { } longFrame) longFrame();
            Assert.Equal(new string('b', 400), text.Text);
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    // WPF Speech.cs:531-539 and Reactions.cs:322-347: 4/5 presets choose giggle1-4 at half volume.
    [Fact]
    public Task PresetFallbackUsesTheQuietModAwareCue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = false;
        s.MasterVolume = 64;
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldPath = CoreModArt.AudioOverridePathProvider;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var path = Path.GetTempFileName();
        var names = new List<string>();
        var played = new List<(string Path, float Volume, string Tag)>();
        CoreMods.ActiveModIdProvider = () => "test";
        CoreModArt.AudioOverridePathProvider = name => { names.Add(name); return path; };
        CoreAudio.PlayOneShotProvider = (p, v, tag, _, _) => played.Add((p, v, tag));
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.Giggle("fallback");
            var cue = Assert.Single(played);
            Assert.Contains(Assert.Single(names), new[] { "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3" });
            Assert.Equal(path, cue.Path);
            Assert.Equal("bubble-fallback", cue.Tag);
            Assert.Equal(0.256, cue.Volume, 3);
            tube.GigglePriority("silent priority", playSound: false, aiGenerated: false);
            Assert.Single(played); // WPF suppresses this fallback for the AI-source path
        }
        finally
        {
            tube.Close();
            CoreMods.ActiveModIdProvider = oldMod;
            CoreModArt.AudioOverridePathProvider = oldPath;
            CoreAudio.PlayOneShotProvider = oldAudio;
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
    // WPF Reactions.cs:145-152, Speech.cs:280,489-511, CompanionIdleVoice.cs:60-71.
    [Theory]
    [InlineData(0)] // real awareness reaction, engine off
    [InlineData(1)] // queued unprompted preset, engine off at delivery
    [InlineData(2)] // unprompted priority giggle during post-panic quiet
    public Task UnpromptedSoundObeysTheLiveGate(int scenario) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.MasterVolume = 100; s.AiChatEnabled = false;
        s.CompanionSpeaksIdleWhenEngineOff = scenario != 0;
        var old = (CoreMods.ActiveModIdProvider, CoreModArt.AudioOverridePathProvider, CoreModArt.OverridePathProvider,
            CoreAudio.PlayOneShotProvider, CoreAudio.PlayStoppableProvider, AvApp.Sessions);
        var path = Path.GetTempFileName();
        int plays = 0;
        CoreMods.ActiveModIdProvider = () => "test";
        CoreModArt.AudioOverridePathProvider = _ => path;
        CoreModArt.OverridePathProvider = _ => path;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => plays++;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        AvApp.Sessions = null;
        PanicAftermath.EndQuiet();
        var lastReaction = typeof(WindowAwarenessService).GetField("_lastReactionTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldReaction = lastReaction.GetValue(AvApp.WindowAwareness);
        var cooldown = typeof(WindowAwarenessService).GetField("_nextReactionCooldownSeconds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldCooldown = cooldown.GetValue(AvApp.WindowAwareness);
        var tube = new AvatarTubeWindow(null);
        try
        {
            Assert.False(CoreEngine.IsRunning);
            tube.Show();
            if (scenario == 0)
            {
                AvApp.WindowAwareness.Stop();
                lastReaction.SetValue(AvApp.WindowAwareness, DateTime.MinValue);
                tube._startupTime = DateTime.MinValue;
                await tube.ReactToActivityAsync(new ActivityChangedEventArgs(ActivityCategory.Gaming, ActivityCategory.Unknown, "Steam", "Steam"));
            }
            else if (scenario == 1)
            {
                tube.Giggle("prompted");
                Assert.Equal(1, plays);
                tube.Giggle("queued", unprompted: true);
                s.CompanionSpeaksIdleWhenEngineOff = false; // checked when the queue actually speaks
                Action? deliver = null;
                tube.SpeechDelayScheduler = (_, fire) => { deliver = fire; return () => { }; };
                tube.EndSpeechBubble();
                Assert.NotNull(deliver);
                deliver!();
                Assert.Equal(1, plays);
                plays = 0;
            }
            else
            {
                PanicAftermath.ArmQuiet();
                tube.GigglePriority("unprompted", unprompted: true);
            }
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(0, plays); // text remains visible, but neither cue plays
            tube.GigglePriority("prompted", unprompted: false);
            Assert.Equal(1, plays); // prompted speech is never held
            PanicAftermath.EndQuiet();
            s.CompanionSpeaksIdleWhenEngineOff = true;
            tube.GigglePriority("allowed", unprompted: true);
            Assert.Equal(2, plays);
        }
        finally
        {
            tube.Close(); PanicAftermath.EndQuiet();
            lastReaction.SetValue(AvApp.WindowAwareness, oldReaction);
            cooldown.SetValue(AvApp.WindowAwareness, oldCooldown);
            (CoreMods.ActiveModIdProvider, CoreModArt.AudioOverridePathProvider, CoreModArt.OverridePathProvider,
                CoreAudio.PlayOneShotProvider, CoreAudio.PlayStoppableProvider, AvApp.Sessions) = old;
            File.Delete(path);
        }
    });

    // WPF Speech.cs:1277-1313,1417-1455,3031-3033: restore the clickable URL after typing.
    [Fact]
    public Task PresetLinkSurvivesTypingAndInvokesTheCompanionLauncher() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarMuted = true;
        var oldNavigate = CompanionLinkLauncher.Navigate;
        string? opened = null;
        CompanionLinkLauncher.Navigate = url => { opened = url; return Task.FromResult<bool?>(true); };
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.Giggle("See [my_playlist](https://example.com) now");
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.TypewriterFrame!();
            Assert.Equal("S", text.Text);
            while (tube.TypewriterFrame is { } frame) frame();
            var link = Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton>(Assert.Single(text.Inlines!.OfType<InlineUIContainer>()).Child);
            Assert.Equal("my_playlist", Assert.IsType<TextBlock>(link.Content).Text);
            Assert.Equal(text.FontSize, link.FontSize);
            ((global::Avalonia.Automation.Provider.IInvokeProvider)
                global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(link)).Invoke();
            Assert.Equal("https://example.com/", opened); // the shared renderer hands over Uri.AbsoluteUri
            tube.GigglePriority("next", playSound: false, aiGenerated: false);
            Assert.Equal("next", text.Text);
            Assert.Empty(text.Inlines!.OfType<InlineUIContainer>());
        }
        finally { tube.Close(); CompanionLinkLauncher.Navigate = oldNavigate; }
        return Task.CompletedTask;
    });

}
