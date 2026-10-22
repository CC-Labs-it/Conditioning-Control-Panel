using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VoiceConfirmationTests
{
    [Fact]
    public Task VoiceHostUsesTheActiveManifestAndWaitsForItsMatchingClip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.MasterVolume);
        var voice = (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled);
        var oldEngine = BarkHead.Engine;
        var oldBubbles = CoreBubbles.StartAction;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        MainShellWindow? shell = null;
        try
        {
            (s.AvatarEnabled, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.MasterVolume) = (true, false, false, 70);
            (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled) = (true, false, false);
            var rule = new BarkRule
            {
                Id = "voicecmd_wake", Trigger = "VoiceCommand",
                VariantPool = new() { new BarkVariant("Matching manifest text", "wake.wav") },
            };
            var command = new BarkRule
            {
                Id = "voicecmd_bubbles_on", Trigger = "VoiceCommand",
                VariantPool = new() { new BarkVariant("Bubbles manifest confirmation", "bubbles.wav") },
            };
            var bark = new BarkEngine(() => new BarkRuleSet(new[] { rule, command })) { ResolveAudio = _ => clip };
            bark.Start();
            BarkHead.Engine = bark;
            Action? finish = null;
            string? played = null, playedTag = null;
            var playCount = 0;
            CoreAudio.PlayStoppableProvider = (path, volume, tag, started, ended) =>
            {
                playCount++;
                playedTag = tag;
                played = path;
                finish = ended;
                return () => ended?.Invoke();
            };
            shell = new MainShellWindow();
            shell.Show();
            shell.SetAvatarEnabled(true);
            Assert.NotNull(shell.Tube);
            var host = shell.VoiceCmds.Host;
            var picked = host.PickVoiceLine("VOICECMD_WAKE");
            Assert.True(picked.HasValue, "The voice host must read the active bark manifest.");
            Assert.Equal(("Matching manifest text", clip), picked.Value);
            host.Say(picked.Value.Text, picked.Value.Audio);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(clip, played);
            Assert.Equal("bark-voice", playedTag);
            var tail = new TaskCompletionSource();
            var tailStarted = new TaskCompletionSource();
            shell.VoiceEchoTail = _ => { tailStarted.TrySetResult(); return tail.Task; }; // no real 300 ms sleep (P08)
            var quiet = host.WaitQuiet(true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(quiet.IsCompleted, "The mic must not reopen while her voiced confirmation is playing.");
            Assert.False(tailStarted.Task.IsCompleted, "The echo tail starts only after the clip ends.");
            finish!();
            await tailStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(quiet.IsCompleted, "The mic must wait out the echo tail after the clip.");
            tail.SetResult();
            await quiet.WaitAsync(TimeSpan.FromSeconds(2));

            // A bark that cuts her confirmation is still her voice (WPF polls IsSpeakingAudio):
            // the mic waits for THAT clip too, not just the confirmation's own callback.
            var tail2 = new TaskCompletionSource();
            var tail2Started = new TaskCompletionSource();
            shell.VoiceEchoTail = _ => { tail2Started.TrySetResult(); return tail2.Task; };
            host.Say(picked.Value.Text, picked.Value.Audio);
            Dispatcher.UIThread.RunJobs();
            var quiet2 = host.WaitQuiet(true);
            shell.Tube!.GigglePriority("a bark cuts in", aiGenerated: false, phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            var barkFinish = finish!;
            await Assert.ThrowsAsync<TimeoutException>(() => tail2Started.Task.WaitAsync(TimeSpan.FromMilliseconds(300)));
            barkFinish();
            await tail2Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            tail2.SetResult();
            await quiet2.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(host.PickVoiceLine("unknown"));
            host.Say("unvoiced fallback", null);
            Dispatcher.UIThread.RunJobs();
            await host.WaitQuiet(false).WaitAsync(TimeSpan.FromSeconds(2));

            // The real wake-prefix command route executes the feature and paints that manifest's line.
            var starts = 0;
            CoreBubbles.StartAction = () => starts++;
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi bubbles on", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, starts);
            Assert.Equal("Bubbles manifest confirmation", shell.Tube!.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(clip, played);
            finish!();

            // Panic's registered voice-capture stopper must also drop a speech post not started yet.
            var beforePanic = playCount;
            host.Say("queued before panic", clip);
            shell.CancelVoicePrompt();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(beforePanic, playCount);
            await host.WaitQuiet(false).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            shell?.Tube?.Close();
            shell?.Close();
            CoreAudio.PlayStoppableProvider = oldAudio;
            CoreBubbles.StartAction = oldBubbles;
            BarkHead.Engine = oldEngine;
            (s.AvatarEnabled, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.MasterVolume) = old;
            (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled) = voice;
            File.Delete(clip);
        }
    });
}
