using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeIdleTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public Task TimerDispatchesAndSliderSpeechAndCloseControlItsCadence() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled, s.AvatarMuted) = (true, true, 1, true, false);
        (s.IdleGiggleIntervalSeconds, s.MasterVolume, s.BarkChatSuppressionMs) = (120, 50, 0);
        var old = BarkHead.Engine;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowAvatarTube();
            var tube = Assert.IsType<AvatarTubeWindow>(shell.Tube);
            var timer = (DispatcherTimer)typeof(AvatarTubeWindow).GetField("_idleTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tube)!;
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(120), timer.Interval);
            var clock = new Clock { Now = DateTimeOffset.Now.AddMinutes(1) };
            tube.EndSpeechBubble();
            tube.SpeechClock = clock;
            s.AvatarEnabled = true;
            Assert.True(tube.IsSpeechReady());
            var lines = new List<BarkSpeech>();
            var rule = new BarkRule { Id = "idle_test", Trigger = "Idle", VariantPool = new() { new BarkVariant("idle test") } };
            var bark = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Clock = clock };
            bark.Speak = line => { lines.Add(line); tube.GigglePriority(line.Text, playSound: false, aiGenerated: false, unprompted: line.Unprompted); };
            bark.Start();
            BarkHead.Engine = bark;
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(20);
            timer.Start();
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromSeconds(120), timer.Interval);
            Assert.Single(lines);
            Assert.True(lines[0].Unprompted);
            Assert.Equal("idle test", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(TimeSpan.FromSeconds(120), timer.Interval);

            clock.Now += TimeSpan.FromSeconds(61);
            tube.OnIdleTick(null, EventArgs.Empty);
            Assert.Single(lines);
            tube.EndSpeechBubble();
            tube.OnIdleTick(null, EventArgs.Empty);
            Assert.Single(lines);
            clock.Now += TimeSpan.FromSeconds(61);
            tube.OnIdleTick(null, EventArgs.Empty);
            Assert.Equal(2, lines.Count);
            tube.EndSpeechBubble();
            clock.Now += TimeSpan.FromSeconds(61);
            typeof(AvatarTubeWindow).GetField("_isGiggling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tube, true);
            tube.OnIdleTick(null, EventArgs.Empty); // a stale latch is cleared and earns a speech gap
            Assert.False(tube.IsSpeaking);
            Assert.Equal(2, lines.Count);
            clock.Now += TimeSpan.FromSeconds(8);
            BarkHead.Engine = null;
            tube.OnIdleTick(null, EventArgs.Empty); // WPF's preset fallback when no bark service exists
            Assert.True(tube.IsSpeaking);
            tube.EndSpeechBubble();
            BarkHead.Engine = bark;

            shell.ShowTab("companion");
            shell.RevealCompanionWorkshopCell(CompanionRoomAnchors.WorkshopBehaviorCell);
            Dispatcher.UIThread.RunJobs();
            var cell = shell.Named<AiPage>("CompanionAiTab")!.GetLogicalDescendants().OfType<WorkshopBehaviorCell>().Single();
            cell.FindControl<Slider>("SliderIdleIntervalCompanion")!.Value = 200;
            Assert.Equal(200, s.IdleGiggleIntervalSeconds);
            Assert.Equal(TimeSpan.FromSeconds(200), timer.Interval);
            timer.Interval = TimeSpan.FromSeconds(30);
            tube.GigglePriority("activity", playSound: false);
            Assert.Equal(TimeSpan.FromSeconds(200), timer.Interval);
            timer.Interval = TimeSpan.FromSeconds(30);
            tube.ShowListeningBubble("listening");
            Assert.Equal(TimeSpan.FromSeconds(200), timer.Interval);
            tube.Close();
            Assert.False(timer.IsEnabled);
            clock.Now += TimeSpan.FromSeconds(61);
            tube.OnIdleTick(null, EventArgs.Empty);
            Assert.Equal(2, lines.Count);
        }
        finally { BarkHead.Engine = old; shell.Close(); CoreSettings.ServiceProvider = oldProvider; }
    });

    [Fact]
    public Task IdleVoiceIsHeldUnlessAllowedAndPanicQuietWins() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) = (false, 80, false, false);
        var oldProvider = CoreSession.IsEngineRunningProvider;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        var plays = 0;
        CoreSession.IsEngineRunningProvider = () => false;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        var tube = new AvatarTubeWindow(null);
        try
        {
            PanicAftermath.EndQuiet();
            tube.Show();
            tube.GigglePriority("held", phraseAudioPath: clip, barkVoice: true, unprompted: true);
            Assert.Equal("held", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(0, plays);
            CoreSession.IsEngineRunningProvider = () => true;
            tube.GigglePriority("engine on", phraseAudioPath: clip, barkVoice: true, unprompted: true);
            Assert.Equal(1, plays);
            CoreSession.IsEngineRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = true;
            tube.GigglePriority("voiced", phraseAudioPath: clip, barkVoice: true, unprompted: true);
            Assert.Equal(2, plays);
            PanicAftermath.ArmQuiet();
            tube.GigglePriority("quiet", phraseAudioPath: clip, barkVoice: true, unprompted: true);
            Assert.Equal(2, plays);
            tube.GigglePriority("prompted", phraseAudioPath: clip, barkVoice: true);
            Assert.Equal(3, plays);
        }
        finally
        {
            tube.Close(); PanicAftermath.EndQuiet(); File.Delete(clip);
            CoreSession.IsEngineRunningProvider = oldProvider; CoreAudio.PlayStoppableProvider = oldAudio;
        }
        return Task.CompletedTask;
    });
}
