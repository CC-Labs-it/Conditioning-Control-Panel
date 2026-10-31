using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class BrainDrainTests
{
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public bool Disposed;
        public double Gain;
        public Action End = () => { };
        public double Volume { set => Gain = value; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Clock : TimeProvider
    {
        public TimeSpan Due, Period;
        public TimerCallback? Callback;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        { Callback = cb; Due = due; Period = period; return new Timer(this); }
        private sealed class Timer(Clock clock) : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) { clock.Due = due; clock.Period = period; return true; }
            public void Dispose() => clock.Callback = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
    private static void Run(Action<string> body)
    {
        var dir = Directory.CreateTempSubdirectory("brain-drain");
        CorePaths.EffectiveAssetsProvider = () => dir.FullName;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.BrainDrainEnabled = true;
        Directory.CreateDirectory(BrainDrainPlayer.Folder);
        File.WriteAllText(Path.Combine(BrainDrainPlayer.Folder, "clip.mp3"), "test");
        try { body(dir.FullName); }
        finally { BrainDrainPlayer.Instance?.Stop(); BrainDrainPlayer.Instance = null; dir.Delete(true); }
    }

    [Fact]
    public void Cadence_probability_nonoverlap_volume_presence_and_stop_match_Wpf() => Run(_ =>
    {
        using var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
        var s = CoreSettings.Current;
        s.BrainDrainIntensity = 60; s.MasterVolume = 50; s.BrainDrainVolume = 40;
        s.BrainDrainHighRefresh = false;
        var clock = new Clock();
        var voices = new List<Voice>();
        double roll = .051;
        var player = new BrainDrainPlayer((path, vol, loop, end) =>
        { Assert.False(loop); var v = new Voice { Gain = vol, End = end }; voices.Add(v); return v; }, clock, () => roll);
        player.Seed(); player.Start(); player.Start();
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Period);
        Assert.Equal("Brain drain active", rpc.CurrentDetails);
        player.Tick(); Assert.Empty(voices); // .6 / 12 = .05
        roll = .049; player.Tick(); Assert.Single(voices); Assert.Equal(.2, voices[0].Gain, 6);
        player.Tick(); Assert.Single(voices);
        s.MasterVolume = 25; Assert.Equal(.1, voices[0].Gain, 6);
        s.MasterVolume = 50; s.BrainDrainVolume = 80; Assert.Equal(.4, voices[0].Gain, 6);
        voices[0].End();
        s.BrainDrainHighRefresh = true; player.Stop(); player.Start(); Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Period);
        roll = .006; player.Tick(); Assert.Single(voices); // .6 / 120 = .005
        roll = .004; player.Tick(); Assert.Equal(2, voices.Count);
        player.Stop(); Assert.True(voices[1].Disposed); Assert.False(player.IsRunning);
        Assert.Null(clock.Callback); Assert.Equal("Exploring the app", rpc.CurrentDetails);
        player.Tick(); Assert.Equal(2, voices.Count);
    });

    [Fact]
    public void Stop_during_build_disposes_the_late_voice_and_disabled_start_does_nothing() => Run(_ =>
    {
        var voice = new Voice();
        BrainDrainPlayer? player = null;
        player = new BrainDrainPlayer((_, _, _, _) => { player!.Stop(); return voice; }, new Clock(), () => 0);
        player.Seed(); player.Start(); player.Tick();
        Assert.True(voice.Disposed); Assert.False(player.IsRunning);
        CoreSettings.Current.BrainDrainEnabled = false; player.Start(); Assert.False(player.IsRunning);
    });

    [Fact]
    public async Task Studio_toggle_runs_audio_only_with_engine_and_reload_finds_new_clips() =>
        await AvaloniaTestDispatcher.RunAsync(() => { Run(_ =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            using var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
            var clock = new Clock();
            Voice? current = null;
            var player = new BrainDrainPlayer((_, _, _, _) => current = new Voice(), clock, () => 0);
            player.Seed();
            var s = CoreSettings.Current;
            s.BrainDrainEnabled = false;
            var card = new BrainDrainFeatureControl();
            var enable = card.FindControl<CheckBox>("ChkEnable")!;
            enable.IsChecked = true; Assert.True(s.BrainDrainEnabled); Assert.False(player.IsRunning);
            s.AudioOnlySession = true; CoreEngine.Start(); Assert.False(player.IsRunning); CoreEngine.Stop();
            s.AudioOnlySession = false; CoreEngine.Start(); Assert.True(player.IsRunning);
            enable.IsChecked = false; Assert.False(player.IsRunning);
            enable.IsChecked = true; Assert.True(player.IsRunning);
            card.FindControl<Slider>("SliderIntensity")!.Value = 32; Assert.Equal(32, s.BrainDrainIntensity);
            player.Tick(); Assert.NotNull(current);
            card.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked = true; Assert.True(s.BrainDrainHighRefresh);
            Assert.True(current!.Disposed); Assert.True(player.IsRunning);
            Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Period);
            File.WriteAllText(Path.Combine(BrainDrainPlayer.Folder, "new.OGG"), "test");
            File.WriteAllText(Path.Combine(BrainDrainPlayer.Folder, "ignored.txt"), "test");
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, player.ClipCount); Assert.False(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            var opened = ""; ExternalOpener.Shell = path => { opened = path; return true; };
            card.FindControl<Button>("BtnOpenAudioFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(BrainDrainPlayer.Folder, opened);
            CoreEngine.Stop(); Assert.False(player.IsRunning);
        }); return Task.CompletedTask; });

    [Fact]
    public async Task Emergency_watchdog_reaches_brain_drain_off_thread()
    {
        var stopped = 0;
        CoreEngine.StopBrainDrain = () => Interlocked.Increment(ref stopped);
        PanicWatchdog.ShutdownStartedForTest = false;
        await PanicWatchdog.Arm(new TaskCompletionSource().Task, TimeSpan.Zero);
        Assert.Equal(1, stopped);
    }


    [Fact]
    public void Clip_scan_prefers_assets_by_name_and_rescans_on_start() => Run(_ =>
    {
        Directory.CreateDirectory(BrainDrainPlayer.LegacyFolder);
        var legacy = Path.Combine(BrainDrainPlayer.LegacyFolder, "clip.mp3");
        var extra = Path.Combine(BrainDrainPlayer.Folder, "added.wav");
        Assert.False(File.Exists(legacy));
        File.WriteAllText(legacy, "legacy");
        string? played = null;
        var player = new BrainDrainPlayer((path, _, _, _) =>
        { played = path; return new Voice(); }, new Clock(), () => 0);
        player.Seed();
        try
        {
            player.Start(); Assert.Equal(1, player.ClipCount); player.Tick();
            Assert.Equal(Path.Combine(BrainDrainPlayer.Folder, "clip.mp3"), played); player.Stop();
            File.WriteAllText(extra, "added");
            player.Start(); Assert.Equal(2, player.ClipCount); player.Stop();
        }
        finally { player.Stop(); File.Delete(legacy); }
    });

}
