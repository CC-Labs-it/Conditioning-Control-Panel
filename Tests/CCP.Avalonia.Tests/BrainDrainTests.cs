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
public class BrainDrainTests {
    private sealed class Voice : MindWipePlayer.IVoice {
        public bool Disposed; public int Starts;
        public bool Play() { Starts++; return true; }
        public double Gain; public int VolumeWrites; public Action End = () => { };
        public double Volume { set { Gain = value; VolumeWrites++; } }
        public void Dispose() => Disposed = true; }
    private sealed class Clock : TimeProvider {
        public TimeSpan Due, Period; public TimerCallback? Callback;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        { Callback = cb; Due = due; Period = period; return new Timer(this); }
        private sealed class Timer(Clock clock) : ITimer {
            public bool Change(TimeSpan due, TimeSpan period) { clock.Due = due; clock.Period = period; return true; }
            public void Dispose() => clock.Callback = null;
            public ValueTask DisposeAsync() { Dispose(); return default; } }
    }
    private static void Run(Action<string> body) {
        var dir = Directory.CreateTempSubdirectory("brain-drain");
        CorePaths.EffectiveAssetsProvider = () => dir.FullName;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.BrainDrainEnabled = true; Directory.CreateDirectory(BrainDrainPlayer.Folder);
        File.WriteAllText(Path.Combine(BrainDrainPlayer.Folder, "clip.mp3"), "test");
        try { body(dir.FullName); }
        finally { BrainDrainPlayer.Instance?.Stop(); BrainDrainPlayer.Instance = null; dir.Delete(true); } }
    [Fact]
    public void Cadence_probability_nonoverlap_volume_presence_and_stop_match_Wpf() => Run(_ => {
        using var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService(); var s = CoreSettings.Current;
        s.BrainDrainIntensity = 60; s.MasterVolume = 50; s.BrainDrainVolume = 40;
        s.BrainDrainHighRefresh = false; var clock = new Clock(); var voices = new List<Voice>();
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
    public void Stop_during_build_disposes_the_late_voice_and_disabled_start_does_nothing() => Run(_ => {
        var voice = new Voice(); BrainDrainPlayer? player = null;
        player = new BrainDrainPlayer((_, _, _, _) => { player!.Stop(); return voice; }, new Clock(), () => 0);
        player.Seed(); player.Start(); player.Tick();
        Assert.True(voice.Disposed); Assert.False(player.IsRunning); Assert.Equal(0, voice.Starts);
        CoreSettings.Current.BrainDrainEnabled = false; player.Start(); Assert.False(player.IsRunning);
    });
    [Fact]
    public async Task Studio_toggle_runs_audio_only_with_engine_and_reload_finds_new_clips() =>
        await AvaloniaTestDispatcher.RunAsync(() => { Run(_ => {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            using var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
            var clock = new Clock(); Voice? current = null;
            var player = new BrainDrainPlayer((_, _, _, _) => current = new Voice(), clock, () => 0);
            player.Seed(); var s = CoreSettings.Current; s.BrainDrainEnabled = false;
            var card = new BrainDrainFeatureControl(); var enable = card.FindControl<CheckBox>("ChkEnable")!;
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
    public async Task Emergency_watchdog_reaches_brain_drain_off_thread() {
        var stopped = 0;
        CoreEngine.StopBrainDrain = () => Interlocked.Increment(ref stopped);
        PanicWatchdog.ShutdownStartedForTest = false;
        await PanicWatchdog.Arm(new TaskCompletionSource().Task, TimeSpan.Zero);
        Assert.Equal(1, stopped); }
    [Fact]
    public void Clip_scan_prefers_assets_by_name_and_rescans_on_start() => Run(_ => {
        Directory.CreateDirectory(BrainDrainPlayer.LegacyFolder); var legacy = Path.Combine(BrainDrainPlayer.LegacyFolder, "clip.mp3");
        var extra = Path.Combine(BrainDrainPlayer.Folder, "added.wav");
        Assert.False(File.Exists(legacy)); File.WriteAllText(legacy, "legacy");
        string? played = null; var player = new BrainDrainPlayer((path, _, _, _) =>
        { played = path; return new Voice(); }, new Clock(), () => 0);
        player.Seed(); try {
            player.Start(); Assert.Equal(1, player.ClipCount); player.Tick();
            Assert.Equal(Path.Combine(BrainDrainPlayer.Folder, "clip.mp3"), played); player.Stop();
            File.WriteAllText(extra, "added");
            player.Start(); Assert.Equal(2, player.ClipCount); player.Stop(); }
        finally { player.Stop(); File.Delete(legacy); }
    });
    [Theory, InlineData(false), InlineData(true)]
    public void Preset_refresh_change_keeps_running_cadence(bool high) => Run(_ => {
        var s = CoreSettings.Current; s.BrainDrainIntensity = 60; s.BrainDrainHighRefresh = high;
        var clock = new Clock(); Voice? clip = null;
        var player = new BrainDrainPlayer((_, _, _, _) => clip = new Voice(), clock, () => .01);
        player.Seed(); player.Start(); var tick = clock.Callback;
        new ConditioningControlPanel.Models.Preset { BrainDrainEnabled = true, BrainDrainIntensity = 60, BrainDrainHighRefresh = !high }.ApplyTo(s);
        player.Tick(); Assert.Equal(high ? 0 : 1, clip?.Starts ?? 0); Assert.Same(tick, clock.Callback);
        Assert.Equal(TimeSpan.FromMilliseconds(high ? 500 : 5000), clock.Period); player.Stop();
    });
    [Fact]
    public void Session_drives_player_presence_ramp_and_pause() => Run(_ => {
        using var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
        var s = CoreSettings.Current; s.BrainDrainEnabled = false; s.BrainDrainIntensity = 70;
        var voices = new List<Voice>();
        var player = new BrainDrainPlayer((_, _, _, _) => { var v = new Voice(); voices.Add(v); return v; }, new Clock(), () => .04);
        player.Seed(); var runner = new SessionRunner(new SessionLogService());
        var session = new ConditioningControlPanel.Models.Session { Name = "Brain test", DurationMinutes = 4 };
        var ss = session.Settings; ss.BrainDrainEnabled = true; ss.BrainDrainStartMinute = 1;
        ss.BrainDrainEndMinute = 3; ss.BrainDrainStartIntensity = 20; ss.BrainDrainEndIntensity = 100;
        ss.RampCurve = ConditioningControlPanel.Models.RampCurve.Linear;
        try {
            runner.Start(session); Assert.False(player.IsRunning);
            runner.Tick(TimeSpan.FromMinutes(1)); Assert.True(player.IsRunning);
            Assert.Equal("Brain drain active", rpc.CurrentDetails); player.Tick(); Assert.Empty(voices);
            runner.Tick(TimeSpan.FromMinutes(2.5)); player.Tick(); Assert.Single(voices);
            runner.Pause(); Assert.True(voices[0].Disposed); Assert.Equal("Exploring the app", rpc.CurrentDetails);
            runner.Resume(); Assert.True(player.IsRunning); Assert.Equal("Brain drain active", rpc.CurrentDetails);
            runner.Tick(TimeSpan.FromMinutes(3)); Assert.False(player.IsRunning);
            Assert.Equal("Exploring the app", rpc.CurrentDetails);
            runner.Stop(); Assert.False(s.BrainDrainEnabled); Assert.Equal(70, s.BrainDrainIntensity);
        }
        finally { runner.Stop(); CoreEngine.Stop(); }
    });
    [Fact]
    public void Immediate_constant_session_keeps_already_running_global_intensity_until_restart() => Run(_ => {
        // WPF BrainDrainService.cs:223,232,275-285; SessionEngine.cs:1484-1490,848-856,529,579.
        var s = CoreSettings.Current; s.BrainDrainIntensity = 60; s.BrainDrainHighRefresh = false;
        var voices = new List<Voice>();
        var player = new BrainDrainPlayer((_, _, _, end) => { var v = new Voice { End = end }; voices.Add(v); return v; }, new Clock(), () => .04);
        player.Seed(); var runner = new SessionRunner(new SessionLogService());
        var session = new ConditioningControlPanel.Models.Session { Name = "Constant brain", DurationMinutes = 4 };
        session.Settings.BrainDrainEnabled = true; session.Settings.BrainDrainStartIntensity = 20;
        session.Settings.BrainDrainEndIntensity = 20;
        try {
            CoreEngine.Start(); Assert.True(player.IsRunning);
            runner.Start(session); Assert.Equal(20, s.BrainDrainIntensity);
            player.Tick(); Assert.Single(voices); // global 60 / 12, not session 20 / 12
            voices[0].End(); runner.Tick(TimeSpan.FromMinutes(2)); player.Tick();
            Assert.Equal(2, voices.Count); // a constant session never sends a ramp
            runner.Pause(); runner.Resume(); player.Tick(); Assert.Equal(2, voices.Count); // restart reads 20
        }
        finally { runner.Stop(); CoreEngine.Stop(); }
    });
    [Fact]
    public async Task Studio_intensity_slider_updates_the_running_player_explicitly() =>
        await AvaloniaTestDispatcher.RunAsync(() => { Run(_ => {
            // WPF BrainDrainFeatureControl.xaml.cs:246-262; BrainDrainService.cs:223,275-285.
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current; s.BrainDrainIntensity = 60; s.BrainDrainHighRefresh = false;
            var voices = new List<Voice>();
            var player = new BrainDrainPlayer((_, _, _, end) => { var v = new Voice { End = end }; voices.Add(v); return v; }, new Clock(), () => .04);
            player.Seed(); CoreEngine.Start();
            try {
                var card = new BrainDrainFeatureControl(); var slider = card.FindControl<Slider>("SliderIntensity")!;
                player.Tick(); Assert.Single(voices); voices[0].End();
                s.BrainDrainIntensity = 20; player.Tick(); Assert.Equal(2, voices.Count); // a settings write alone is not UpdateSettings
                voices[1].End(); slider.Value = 20; player.Tick(); Assert.Equal(2, voices.Count);
                slider.Value = 80; player.Tick(); Assert.Equal(3, voices.Count);
            }
            finally { CoreEngine.Stop(); }
        }); return Task.CompletedTask; });
    [Fact]
    public void Session_ramp_changes_real_player_probability_without_writing_settings() => Run(_ => {
        var s = CoreSettings.Current; s.BrainDrainIntensity = 20;
        var voices = new List<Voice>(); double roll = .04;
        var player = new BrainDrainPlayer((_, _, _, end) => { var v = new Voice { End = end }; voices.Add(v); return v; }, new Clock(), () => roll);
        player.Seed(); player.Start(); player.Tick(); Assert.Empty(voices); // 20 / 100 / 12
        CoreEngine.SetBrainDrainIntensity!(60); player.Tick(); Assert.Single(voices); // 60 / 100 / 12
        Assert.Equal(20, s.BrainDrainIntensity);
        voices[0].End(); s.BrainDrainIntensity = 30; CoreEngine.SetBrainDrainIntensity!(s.BrainDrainIntensity);
        player.Tick(); Assert.Single(voices); // the slider's explicit update resets the ramp
        CoreEngine.SetBrainDrainIntensity!(80); player.Stop(); player.Start(); player.Tick();
        Assert.Single(voices); // restart reads the saved intensity, not the prior ramp
    });
    [Fact]
    public void Restore_rebinds_live_volume_and_stop_unsubscribes() => Run(_ => {
        var service = CoreSettings.Service!; var old = service.Current; var voice = new Voice();
        Func<int> listeners = () => ((Delegate?)typeof(SettingsService).GetField(nameof(SettingsService.CurrentReplaced),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service))?.GetInvocationList().Length ?? 0;
        var before = listeners(); old.MasterVolume = 50; old.BrainDrainVolume = 40;
        var player = new BrainDrainPlayer((_, _, _, _) => voice, new Clock(), () => 0);
        player.Seed(); player.Start(); player.Tick(); Assert.Equal(.2, voice.Gain, 6);
        var restored = new ConditioningControlPanel.Models.AppSettings { MasterVolume = 80, BrainDrainVolume = 50, BrainDrainEnabled = true };
        service.RestoreFrom(restored); Assert.True(player.IsRunning); Assert.Equal(.4, voice.Gain, 6);
        var writes = voice.VolumeWrites; old.MasterVolume = 0; Assert.Equal(writes, voice.VolumeWrites);
        restored.MasterVolume = 0; Assert.Equal(0, voice.Gain); restored.MasterVolume = 40; Assert.Equal(.2, voice.Gain, 6);
        Assert.Equal(before + 1, listeners()); player.Stop(); Assert.True(voice.Disposed); Assert.Equal(before, listeners());
        writes = voice.VolumeWrites; restored.MasterVolume = 0; service.Reset(); Assert.Equal(writes, voice.VolumeWrites);
    });
}
