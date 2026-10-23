using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Sync7CompanionTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void VoiceSliderSeedsPersistsAndRefreshes() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.CompanionVoiceLevel = 37;
        try
        {
            var cell = new WorkshopBehaviorCell();
            var slider = cell.FindControl<Slider>("SliderVoiceLevelCompanion");
            Assert.NotNull(slider);
            Assert.Equal(37, slider.Value);
            slider.Value = 91;
            Assert.Equal(91, service.Current.CompanionVoiceLevel);
            // The slider must request a (debounced) save; flush it here instead of waiting on a real timer (P08).
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(91, new SettingsService().Current.CompanionVoiceLevel);
            Assert.Equal("91", cell.FindControl<TextBlock>("TxtVoiceLevelCompanion")!.Text);
            service.Current.CompanionVoiceLevel = 22;
            cell.SyncFromSettings();
            Assert.Equal(22, slider.Value);
            Assert.Equal("22", cell.FindControl<TextBlock>("TxtVoiceLevelCompanion")!.Text);
        }
        finally { CoreSettings.ServiceProvider = provider; }
    });

    [Fact]
    public void SpokenLinesReadVoiceLevelForEveryClipAndZeroKeepsTheBubble() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var (provider, playback) = (CoreSettings.ServiceProvider, CoreAudio.PlayStoppableProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.MasterVolume = 50;
        service.Current.AvatarMuted = false;
        service.Current.CompanionVoiceLinesMuted = false;
        var file = Path.GetTempFileName();
        var gains = new List<float>();
        CoreAudio.PlayStoppableProvider = (_, gain, _, _, _) => { gains.Add(gain); return () => { }; };
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            foreach (var (level, bark) in new[] { (80, true), (100, true), (40, false) })
            {
                service.Current.CompanionVoiceLevel = level;
                tube.GigglePriority("recorded line", aiGenerated: false, phraseAudioPath: file, barkVoice: bark);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(CompanionVoiceGain.Compute(50, level,
                    bark ? CompanionVoiceGain.BarkTrim : CompanionVoiceGain.EventTrim), gains.Last(), 6);
            }
            service.Current.CompanionVoiceLevel = 0;
            var finished = false;
            tube.GigglePriority("silent line", aiGenerated: false, phraseAudioPath: file,
                barkVoice: true, onSpoken: () => finished = true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, gains.Count);
            Assert.True(finished);
            Assert.Equal("silent line", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally
        {
            tube.Close();
            (CoreSettings.ServiceProvider, CoreAudio.PlayStoppableProvider) = (provider, playback);
            File.Delete(file);
        }
    });

    [Fact]
    public void AudioFallbackReadsTheActiveModAndRespectsDisabledVoiceLines() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var (provider, modId, modPackage) = (CoreSettings.ServiceProvider, CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        var (voice, neutral) = (MediaCommand.ModVoiceLinesProvider, MediaCommand.NeutralWordClipsProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var root = Directory.CreateTempSubdirectory("ccp-audio-mod-").FullName;
        var folder = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(folder);
        var kept = Path.Combine(folder, "kept.mp3");
        File.WriteAllBytes(kept, new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(folder, "muted.mp3"), new byte[] { 1 });
        service.Current.DisabledPhraseIds.Add("VoiceLine:muted");
        CoreMods.ActiveModIdProvider = () => "test-mod";
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "test-mod" }, root, false);
        try
        {
            CompanionEffects.Seed();
            Assert.NotNull(MediaCommand.ModVoiceLinesProvider);
            Assert.NotNull(MediaCommand.NeutralWordClipsProvider);
            Assert.Equal(new[] { kept }, MediaCommand.ModVoiceLinesProvider());
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = (provider, modId, modPackage);
            (MediaCommand.ModVoiceLinesProvider, MediaCommand.NeutralWordClipsProvider) = (voice, neutral);
            Directory.Delete(root, true);
        }
    });

    [Fact]
    public Task AudioActionUsesAStopHandleAndPanicStopsItOnce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var (oneShot, stoppable) = (CoreAudio.PlayOneShotProvider, CoreAudio.PlayStoppableProvider);
        var stopped = 0;
        CoreAudio.PlayOneShotProvider = (_, _, _, started, _) => started?.Invoke(TimeSpan.FromSeconds(1));
        CoreAudio.PlayStoppableProvider = (_, _, _, started, _) =>
        {
            started?.Invoke(TimeSpan.FromSeconds(1));
            return () => stopped++;
        };
        try
        {
            Assert.True(await CompanionEffects.PlayAudio("recorded.mp3"));
            Assert.True(await CompanionEffects.PlayAudio("replacement.mp3"));
            Assert.Equal(1, stopped);
            var panic = PanicSurfaces.All.Single(s => s.Id == "companion-audio");
            panic.Stop(null);
            panic.Stop(null);
            Assert.Equal(2, stopped);
        }
        finally { (CoreAudio.PlayOneShotProvider, CoreAudio.PlayStoppableProvider) = (oneShot, stoppable); }
    });
}
