using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Bark;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class TubeIdleHostTests
{
    [Fact]
    public Task TimerDispatchesAndOptInControlsVoiceThenPanicKeepsTimer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionSpeaksIdleWhenEngineOff, s.CompanionVoiceLinesMuted);
        var rotation = s.BarkIdleRotation; var engine = BarkHead.Engine; var audio = CoreAudio.PlayStoppableProvider;
        var engineProvider = CoreSession.IsEngineRunningProvider;
        var sessionProvider = CoreSession.IsSessionRunningProvider;
        var clip = Path.GetTempFileName();
        AvatarTubeWindow? tube = null;
        try
        {
            (s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionSpeaksIdleWhenEngineOff, s.CompanionVoiceLinesMuted) = (true, 80, false, false, false);
            CoreSession.IsEngineRunningProvider = () => false; CoreSession.IsSessionRunningProvider = () => false;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            int played = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => { }; };
            tube = new AvatarTubeWindow(); tube.Show(); Dispatcher.UIThread.RunJobs();
            var rule = new BarkRule { Id = Guid.NewGuid().ToString(), Trigger = "Idle", VariantPool = new() { new BarkVariant("idle", "voice") } };
            BarkHead.Engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = speech => BarkHead.Speak(tube, speech), ResolveAudio = _ => clip };
            BarkHead.Engine.Start();
            tube.DispatchIdleTick(); Dispatcher.UIThread.RunJobs();
            Assert.True(tube.HasBubbleUp); Assert.Equal(0, played);
            tube.PanicSilence(); Dispatcher.UIThread.RunJobs(); Assert.True(tube.IdleTimerRunning); // WPF keeps it
            s.CompanionSpeaksIdleWhenEngineOff = true;
            // Direct mouth path isolates the audio gate from the normal 60-second bark floor.
            BarkHead.Speak(tube, new BarkSpeech("opted in", clip, false, null, false, true));
            Dispatcher.UIThread.RunJobs(); Assert.Equal(1, played);
        }
        finally
        {
            s.BarkIdleRotation = rotation; tube?.Close(); File.Delete(clip); BarkHead.Engine = engine; CoreAudio.PlayStoppableProvider = audio;
            CoreSession.IsEngineRunningProvider = engineProvider; CoreSession.IsSessionRunningProvider = sessionProvider;
            (s.AvatarEnabled, s.MasterVolume, s.AvatarMuted, s.CompanionSpeaksIdleWhenEngineOff, s.CompanionVoiceLinesMuted) = old;
        }
        return Task.CompletedTask;
    });
}
