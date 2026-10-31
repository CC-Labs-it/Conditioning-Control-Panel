using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionVoiceIdleTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void BarkEchoPolicyUsesLivePausedSessionAndPanicState()
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff = false;
        CoreSession.IsEngineRunningProvider = () => false;
        CoreSession.IsSessionRunningProvider = () => true;
        CoreSession.IsSessionPausedProvider = () => true;
        ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
        BarkHead.Seed(new BarkEngine());
        Assert.False(CompanionBrain.IdleLineVoiced!("FaceLost"));
        CoreSession.IsSessionPausedProvider = () => false;
        Assert.True(CompanionBrain.IdleLineVoiced!("FaceLost"));
        ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
        Assert.False(CompanionBrain.IdleLineVoiced!("FaceLost"));
        Assert.True(CompanionBrain.IdleLineVoiced!("AvatarClicked"));
    }

    [Fact]
    public Task ControlsSeedAndWriteSettings() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var oldSettingsProvider = CoreSettings.ServiceProvider;
        var settingsService = new SettingsService();
        CoreSettings.ServiceProvider = () => settingsService;
        var s = CoreSettings.Current;
        var old = (s.CompanionVoiceLevel, s.CompanionSpeaksIdleWhenEngineOff);
        try
        {
            s.CompanionVoiceLevel = 37; s.CompanionSpeaksIdleWhenEngineOff = true;
            var cell = new WorkshopBehaviorCell();
            var slider = cell.FindControl<Slider>("SliderVoiceLevelCompanion")!;
            var toggle = cell.FindControl<CheckBox>("ChkIdleVoiceEngineOffCompanion")!;
            Assert.Equal(37, slider.Value);
            Assert.True(toggle.IsChecked);
            slider.Value = 91;
            toggle.IsChecked = false;
            Assert.Equal(91, s.CompanionVoiceLevel);
            Assert.Equal("91", cell.FindControl<TextBlock>("TxtVoiceLevelCompanion")!.Text);
            Assert.False(s.CompanionSpeaksIdleWhenEngineOff);
        }
        finally { (s.CompanionVoiceLevel, s.CompanionSpeaksIdleWhenEngineOff) = old; CoreSettings.Save(); CoreSettings.ServiceProvider = oldSettingsProvider; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VoiceGainAndHeldPriorityKeepTheBubbleAndCompleteCallbacks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var oldSettingsProvider = CoreSettings.ServiceProvider;
        var settingsService = new SettingsService();
        CoreSettings.ServiceProvider = () => settingsService;
        var s = CoreSettings.Current;
        var old = (s.MasterVolume, s.CompanionVoiceLevel, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff);
        var provider = CoreAudio.PlayStoppableProvider;
        var running = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var clip = Path.GetTempFileName();
        var gains = new List<float>();
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            s.MasterVolume = 50; s.CompanionVoiceLevel = 100; s.AvatarMuted = false;
            s.CompanionVoiceLinesMuted = false; s.CompanionSpeaksIdleWhenEngineOff = false;
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, gain, _, _, done) => { gains.Add(gain); done?.Invoke(); return () => { }; };
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            var completed = 0;
            tube.GigglePriority("quiet idle", phraseAudioPath: clip, barkVoice: true, unprompted: true, onSpoken: () => completed++);
            Assert.Empty(gains);
            Assert.Equal(1, completed);
            Assert.Equal("quiet idle", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            tube.GigglePriority("prompted", phraseAudioPath: clip, barkVoice: true);
            Assert.Equal(CompanionVoiceGain.Compute(50, 100, CompanionVoiceGain.BarkTrim), Assert.Single(gains));
            s.CompanionVoiceLevel = 0;
            tube.GigglePriority("zero", phraseAudioPath: clip, barkVoice: true);
            Assert.Single(gains);
            s.CompanionVoiceLevel = 80; s.CompanionSpeaksIdleWhenEngineOff = true;
            tube.GigglePriority("opted in", phraseAudioPath: clip, unprompted: true);
            Assert.Equal(CompanionVoiceGain.Compute(50, 80, CompanionVoiceGain.EventTrim), gains[1]);
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            tube.GigglePriority("panic quiet", phraseAudioPath: clip, unprompted: true, onSpoken: () => completed++);
            Assert.Equal(2, gains.Count);
            Assert.Equal(2, completed);
            tube.GigglePriority("prompted after panic", phraseAudioPath: clip);
            Assert.Equal(3, gains.Count);
        }
        finally
        {
            tube.Close(); File.Delete(clip); CoreAudio.PlayStoppableProvider = provider;
            CoreSession.IsEngineRunningProvider = running; CoreSession.IsSessionRunningProvider = session;
            CoreSettings.ServiceProvider = oldSettingsProvider;
            (s.MasterVolume, s.CompanionVoiceLevel, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QueueRetainsIdleFlagUntilPlayback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var oldSettingsProvider = CoreSettings.ServiceProvider;
        var settingsService = new SettingsService();
        CoreSettings.ServiceProvider = () => settingsService;
        var s = CoreSettings.Current;
        var old = (s.AvatarMuted, s.CompanionSpeaksIdleWhenEngineOff);
        var running = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var tube = new AvatarTubeWindow(null);
        try
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            tube.Show(); s.AvatarMuted = false; s.CompanionSpeaksIdleWhenEngineOff = false;
            CoreSession.IsEngineRunningProvider = () => false; CoreSession.IsSessionRunningProvider = () => false;
            int reads = 0;
            CoreSession.IsEngineRunningProvider = () => { reads++; return false; };
            tube.Giggle("first"); tube.Giggle("queued idle", unprompted: true);
            Action? fire = null;
            tube.SpeechDelayScheduler = (_, action) => { fire = action; return () => { }; };
            tube.EndSpeechBubble();
            Assert.Equal(0, reads);
            fire!();
            Assert.Equal(1, reads);
            Assert.Equal("queued idle", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }
        finally
        {
            tube.Close();
            CoreSession.IsEngineRunningProvider = running; CoreSession.IsSessionRunningProvider = session;
            CoreSettings.ServiceProvider = oldSettingsProvider;
            (s.AvatarMuted, s.CompanionSpeaksIdleWhenEngineOff) = old;
        }
        return Task.CompletedTask;
    });
}
