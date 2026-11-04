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
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RainSpeechTests
{
    private sealed class Scheduled : IDisposable
    {
        public long Due; public Action Fire = null!; public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "rain-speech-" + Guid.NewGuid());
        public readonly AvatarTubeWindow Tube;
        public readonly SettingsService Settings = new();
        public readonly List<Scheduled> Timers = new();
        public long Now;
        private readonly Func<SettingsService>? _settings = CoreSettings.ServiceProvider;
        private readonly Func<string>? _mod = CoreMods.ActiveModIdProvider;
        private readonly Func<string, float, string, Action<TimeSpan>?, Action?, Action?>? _audio = CoreAudio.PlayStoppableProvider;
        public Fixture(int minClipMs = 0)
        {
            if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var hash = new string('c', 64); var folder = Path.Combine(Root, hash); Directory.CreateDirectory(folder);
            var names = new[] { "idle", "talk_a", "talk_b", "greeting", "joy", "affection", "giggle" };
            foreach (var name in names)
            {
                using var bitmap = new SKBitmap(1, 2); bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(0, 1, SKColors.Blue);
                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(Path.Combine(folder, name + ".png"), png.ToArray());
            }
            File.WriteAllText(Path.Combine(folder, "manifest.json"), $"{{rev:1,lookHash:'{hash}',clips:{{" +
                string.Join(",", names.Select(n => $"{n}:{{file:'{n}.png',frames:2,fps:1,w:1,h:1}}")) + "}}");
            File.WriteAllText(Path.Combine(folder, "emotes.json"), $"{{fadeMs:0,minClipMs:{minClipMs},talkStartDelayMs:1000,talkLeadOutMs:500," +
                "idleRotation:[{clip:'idle'}],talking:{pool:['talk_a','talk_b'],maxTalkClips:2},expressive:{pool:['giggle']}," +
                "clickEmotes:['giggle'],stemPrefix:{session:'greeting',session_start:'joy',bad:'missing'},mood:{warm:'affection',playful:'giggle'}}");
            File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{talk_a:{durationMs:2500,speakStartMs:100,speakEndMs:2100},talk_b:{durationMs:2500,speakStartMs:100,speakEndMs:2100}}");
            Settings.Current.AvatarGeneration = "v2"; Settings.Current.RainPackHash = hash;
            Settings.Current.AvatarMuted = false; Settings.Current.MasterVolume = 80;
            CoreSettings.ServiceProvider = () => Settings; CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
            Tube = new AvatarTubeWindow(null) { RainPackRoot = Root, RainClock = () => Now };
            Tube.RainSchedule = (delay, fire) => { var t = new Scheduled { Due = Now + (long)delay.TotalMilliseconds, Fire = fire }; Timers.Add(t); return t; };
        }
        public async Task Start() { Tube.Show(); await Tube.RefreshRainPlayback(); Assert.Equal("idle", Tube.RainClip); }
        public void Advance(long ms)
        {
            long end = Now + ms;
            while (Timers.Where(t => !t.Cancelled && t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } next)
            { next.Cancelled = true; Now = next.Due; next.Fire(); }
            Now = end;
        }
        public async Task Clip(string clip)
        {
            for (int i = 0; i < 100 && Tube.RainClip != clip; i++) await Task.Delay(10);
            Assert.Equal(clip, Tube.RainClip);
        }
        public async Task Talk()
        {
            for (int i = 0; i < 100 && !(Tube.RainClip?.StartsWith("talk_") ?? false); i++) await Task.Delay(10);
            Assert.StartsWith("talk_", Tube.RainClip);
        }
        public void Dispose()
        {
            Tube.Close(); CoreSettings.ServiceProvider = _settings; CoreMods.ActiveModIdProvider = _mod;
            CoreAudio.PlayStoppableProvider = _audio; Directory.Delete(Root, true);
        }
    }
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(body);
    [Fact]
    public Task VoiceDurationSchedulesTalkWindowsAndLongestStemReaction() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start();
        var audio = Path.Combine(f.Root, "session_start_01.wav"); File.WriteAllText(audio, "voice");
        CoreAudio.PlayStoppableProvider = (_, _, _, started, _) => { started?.Invoke(TimeSpan.FromSeconds(8)); return () => { }; };
        f.Tube.GigglePriority("a spoken line", phraseAudioPath: audio, mood: "warm, playful");
        Assert.Equal(1000, f.Timers.Last(t => !t.Cancelled).Due);
        f.Advance(1000); await f.Talk();
        var first = f.Tube.RainClip;
        f.Tube.RainActivePlayer!.Tick(long.MaxValue); // completion cannot take over the speech schedule
        Assert.Equal(first, f.Tube.RainClip);
        f.Advance(2000); await f.Clip(first == "talk_a" ? "talk_b" : "talk_a");
        f.Advance(2000); await f.Clip(first == "talk_a" ? "talk_b" : "talk_a");
        f.Advance(2500); await f.Clip("joy"); // longest stem wins over mood, 500 ms before voice ends
    });
    [Fact]
    public Task QueuedLineKeepsStemAndMoodAndNonverbalUsesExpression() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start();
        Action? gap = null;
        f.Tube.SpeechDelayScheduler = (_, fire) => { gap = fire; return () => { }; };
        f.Tube.Giggle("first line");
        BarkHead.Speak(new ConditioningControlPanel.Services.Bark.BarkSpeech("queued line", "/tmp/session_start_02.wav", false, "warm", false), f.Tube); Assert.Equal(1, f.Tube.QueuedSpeechCount); Assert.Empty(f.Tube.ChatHistory);
        f.Tube.EndSpeechBubble(); Assert.NotNull(gap); gap!();
        f.Advance(1000); await f.Talk();
        f.Advance(500); await f.Clip("joy");
        f.Tube.GigglePriority("hello", mood: "warm, playful");
        f.Advance(1000); f.Advance(500); await f.Clip("affection");
        int plays = 0;
        var audio = Path.Combine(f.Root, "vocal.wav"); File.WriteAllText(audio, "voice");
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        f.Tube.GigglePriority("*giggles*", phraseAudioPath: audio, mood: "warm"); await f.Clip("giggle");
        Assert.Equal(1, plays); // an AI-source line has no lead-in (WPF Speech.cs:651-654)
        var map = Path.Combine(f.Tube.RainFolder!, "emotes.json");
        File.WriteAllText(map, File.ReadAllText(map).Replace("minClipMs:0", "minClipMs:2500"));
        await f.Tube.RefreshRainPlayback();
        f.Tube.GigglePriority("short line", playSound: false, mood: "warm");
        f.Advance(2499); Assert.Equal("idle", f.Tube.RainClip);
        f.Advance(1); await f.Clip("affection"); // short line skips talk and holds the prior clip
    });
    [Fact]
    public Task PanicMuteCloseAndClickCancelPendingSpeechAndStaleAudioCallbacks() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start();
        var audio = Path.Combine(f.Root, "session_start.wav"); File.WriteAllText(audio, "voice");
        Action<TimeSpan>? started = null; int stops = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, callback, _) => { started = callback; return () => stops++; };
        f.Settings.Current.AvatarMuted = true; f.Tube.GigglePriority("a spoken line", phraseAudioPath: audio);
        f.Advance(1000); await f.Talk(); Assert.Null(started);
        f.Settings.Current.AvatarMuted = false; f.Tube.PanicSilence(); f.Advance(10000); await f.Clip("idle");
        f.Tube.GigglePriority("a spoken line", phraseAudioPath: audio); f.Tube.ShowListeningBubble("listening"); Assert.Equal(0, stops); f.Tube.HideListeningBubble();
        var stale = f.Timers.Last(); f.Tube.PanicSilence(); Assert.True(stale.Cancelled); stale.Fire(); started!(TimeSpan.FromSeconds(8));
        f.Advance(10000); await f.Clip("idle");
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        f.Advance(1000); await f.Talk();
        var pending = f.Timers.Last(); f.Tube.OnMenuMute(); started!(TimeSpan.FromSeconds(8)); Assert.Equal(1, stops); Assert.False(pending.Cancelled);
        f.Advance(650); await f.Clip("joy");
        f.Settings.Current.AvatarMuted = false;
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        var clickPending = f.Timers.Last(); Assert.True(f.Tube.RainClickEmote()); clickPending.Fire(); f.Advance(2000); await f.Clip("giggle");
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        var closePending = f.Timers.Last(); f.Tube.Close(); closePending.Fire(); started!(TimeSpan.FromSeconds(8));
        Assert.Null(f.Tube.RainFolder); Assert.Null(f.Tube.RainActivePlayer);
    });
    [Fact]
    public Task ClickHoldCoalescesAndPanicAndCloseCancelIt() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start();
        Assert.True(f.Tube.RainClickEmote()); Assert.False(f.Tube.RainClickEmote());
        await Task.Delay(50); Assert.Equal("idle", f.Tube.RainClip);
        var first = f.Timers.Last(); f.Advance(1000);
        f.Tube.QueueRainClick("greeting"); Assert.True(first.Cancelled); first.Fire();
        f.Advance(999); Assert.Equal("idle", f.Tube.RainClip);
        f.Advance(1); await f.Clip("greeting");
        f.Tube.QueueRainClick("giggle"); var panic = f.Timers.Last();
        f.Tube.PanicSilence(); Assert.True(panic.Cancelled); panic.Fire(); await f.Clip("idle");
        f.Tube.QueueRainClick("giggle"); var mute = f.Timers.Last(); f.Settings.Current.AvatarMuted = true;
        Assert.False(mute.Cancelled); f.Advance(2000); await f.Clip("giggle"); f.Settings.Current.AvatarMuted = false;
        f.Tube.QueueRainClick("greeting"); var speech = f.Timers.Last();
        f.Tube.GigglePriority("a spoken line", playSound: false); Assert.True(speech.Cancelled); speech.Fire();
        f.Advance(1000); await f.Talk();
        f.Tube.QueueRainClick("giggle"); var close = f.Timers.Last();
        f.Tube.Close(); Assert.True(close.Cancelled); close.Fire(); Assert.Null(f.Tube.RainFolder);
    });
    [Fact]
    public Task RetriesContinuePastThreeAndResetAndCancelOnStop() => Run(async () =>
    {
        using var f = new Fixture();
        var folder = Path.Combine(f.Root, new string('c', 64)); var idle = Path.Combine(folder, "idle.png");
        File.WriteAllText(idle, "corrupt"); f.Tube.Show(); await f.Tube.RefreshRainPlayback();
        for (int i = 0; i < 5; i++)
        {
            int count = f.Timers.Count; Assert.Contains(f.Timers, t => !t.Cancelled);
            f.Advance(1200);
            for (int j = 0; j < 100 && f.Timers.Count == count; j++) await Task.Delay(10);
            Assert.True(f.Timers.Count > count);
        }
        var retry = f.Timers.Last(); f.Settings.Current.AvatarGeneration = "v1";
        Assert.True(retry.Cancelled); retry.Fire(); Assert.Null(f.Tube.RainFolder);
        var failures = typeof(AvatarTubeWindow).GetField("_rainFailures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(0, failures.GetValue(f.Tube));
        File.Copy(Path.Combine(folder, "giggle.png"), idle, true);
        f.Settings.Current.AvatarGeneration = "v2"; await f.Tube.RefreshRainPlayback(); await f.Clip("idle");
        File.WriteAllText(idle, "corrupt"); await f.Tube.PlayRainClip("idle");
        var close = f.Timers.Last(); f.Tube.Close(); Assert.True(close.Cancelled); close.Fire(); Assert.Null(f.Tube.RainFolder);
    });

    private static (Func<int> plays, string audio, Border bubble) Voice(Fixture f)
    {
        int plays = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        var audio = Path.Combine(f.Root, "vocal.wav"); File.WriteAllText(audio, "voice");
        return (() => plays, audio, f.Tube.FindControl<Border>("SpeechBubble")!);
    }
    private static object? HideTimer(AvatarTubeWindow tube) => typeof(AvatarTubeWindow)
        .GetField("_speechTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tube);

    [Fact]
    public Task PresetNonverbalLeadInHoldsBubbleHideTimerAndVoiceUntilAfterMinHold() => Run(async () =>
    {
        using var f = new Fixture(2500); await f.Start(); var (plays, audio, bubble) = Voice(f);
        BarkHead.Speak(new ConditioningControlPanel.Services.Bark.BarkSpeech("*giggles*", audio, false, null, false), f.Tube); Assert.Empty(f.Tube.ChatHistory);
        f.Advance(2749); await f.Clip("giggle"); // fresh idle: 2500 ms hold, then 250 ms lead-in (WPF CirceEmotes.cs:731-735)
        Assert.Equal(0, plays()); Assert.False(bubble.IsVisible); Assert.Null(HideTimer(f.Tube));
        f.Advance(1);
        Assert.Equal(1, plays()); Assert.True(bubble.IsVisible); Assert.True(((DispatcherTimer)HideTimer(f.Tube)!).IsEnabled);
    });
    [Fact]
    public Task PresetLeadInUsesMuteAtSpeechTime() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start(); var (plays, audio, bubble) = Voice(f);
        f.Settings.Current.AvatarMuted = true; f.Tube.Giggle("*giggles*", audio);
        f.Settings.Current.AvatarMuted = false; f.Advance(250);
        Assert.Equal(1, plays()); Assert.True(bubble.IsVisible);
        f.Tube.PanicSilence(); typeof(AvatarTubeWindow).GetField("_lastSpeechEndTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(f.Tube, DateTime.MinValue); f.Tube.Giggle("*giggles*", audio); f.Settings.Current.AvatarMuted = true; f.Advance(3000);
        Assert.Equal(1, plays()); Assert.True(bubble.IsVisible);
    });
    [Fact]
    public Task UntextedShortClipIsClassifiedByItsLengthBeforeItPlays() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start(); var (plays, audio, bubble) = Voice(f);
        f.Tube.RainAudioProbe = _ => 1.0; // under nonverbalMaxSec 1.4 (WPF CirceEmotes.cs:627-635, 788)
        f.Tube.Giggle("", audio); await f.Clip("giggle");
        f.Advance(249); Assert.Equal(0, plays());
        f.Advance(1); Assert.Equal(1, plays()); Assert.True(bubble.IsVisible);
    });
    [Fact]
    public Task ThinkingReplacesPendingVoiceAndRainSequence() => Run(async () =>
    {
        using var f = new Fixture(2500); await f.Start(); var (_, audio, bubble) = Voice(f); f.Tube.Giggle("*giggles*", audio);
        var old = f.Timers.ToArray(); var done = f.Tube.SpokenAudioDone; Assert.False(done.IsCompleted);
        f.Tube.StartThinkingAnimation(); Assert.True(done.IsCompleted); Assert.All(old, t => Assert.True(t.Cancelled));
        foreach (var t in old) t.Fire(); Assert.True(bubble.IsVisible); Assert.NotEqual("*giggles*", f.Tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        Assert.Contains(f.Timers, t => !t.Cancelled); CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => throw new InvalidOperationException(); f.Tube.GigglePriority("voiced", phraseAudioPath: audio); f.Tube.StartThinkingAnimation(); Assert.NotNull(f.Tube.ThinkingFrame);
    });
    [Fact]
    public Task PanicDuringPresetLeadInDropsVoiceAndBubble() => Run(async () =>
    {
        using var f = new Fixture(); await f.Start(); var (plays, audio, bubble) = Voice(f);
        f.Tube.Giggle("*giggles*", audio); var stale = f.Timers.Last(); f.Tube.PanicSilence();
        f.Tube.SpeechDelayScheduler = (_, _) => () => { }; f.Tube.Giggle("queued after panic"); Assert.True(f.Tube.IsSpeaking);
        Assert.True(stale.Cancelled); stale.Fire(); f.Advance(3000); Assert.Equal(0, plays()); Assert.False(bubble.IsVisible);
    });
}
