using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
        public Fixture()
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
            File.WriteAllText(Path.Combine(folder, "emotes.json"), "{fadeMs:0,minClipMs:0,talkStartDelayMs:1000,talkLeadOutMs:500," +
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
        f.Tube.Giggle("queued line", "/tmp/session_start_02.wav", mood: "warm");
        f.Tube.EndSpeechBubble(); Assert.NotNull(gap); gap!();
        f.Advance(1000); await f.Talk();
        f.Advance(500); await f.Clip("joy");
        f.Tube.GigglePriority("hello", mood: "warm, playful");
        f.Advance(1000); f.Advance(500); await f.Clip("affection");
        int plays = 0;
        var audio = Path.Combine(f.Root, "vocal.wav"); File.WriteAllText(audio, "voice");
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        f.Tube.GigglePriority("*giggles*", phraseAudioPath: audio, mood: "warm"); await f.Clip("giggle");
        f.Advance(249); Assert.Equal(0, plays);
        f.Advance(1); Assert.Equal(1, plays);
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
        Action<TimeSpan>? started = null;
        CoreAudio.PlayStoppableProvider = (_, _, _, callback, _) => { started = callback; return () => { }; };
        f.Tube.GigglePriority("a spoken line", phraseAudioPath: audio);
        var stale = f.Timers.Last(); f.Tube.PanicSilence(); Assert.True(stale.Cancelled); stale.Fire(); started!(TimeSpan.FromSeconds(8));
        f.Advance(10000); await f.Clip("idle");
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        f.Advance(1000); await f.Talk();
        var pending = f.Timers.Last(); f.Settings.Current.AvatarMuted = true; Assert.True(pending.Cancelled); pending.Fire();
        f.Advance(10000); await f.Clip("idle");
        f.Settings.Current.AvatarMuted = false;
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        var clickPending = f.Timers.Last(); Assert.True(f.Tube.RainClickEmote()); clickPending.Fire(); await f.Clip("giggle");
        int plays = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
        f.Tube.GigglePriority("*giggles*", phraseAudioPath: audio);
        f.Tube.PanicSilence(); f.Advance(250); Assert.Equal(0, plays);
        f.Tube.GigglePriority("another spoken line", phraseAudioPath: audio);
        var closePending = f.Timers.Last(); f.Tube.Close(); closePending.Fire(); started!(TimeSpan.FromSeconds(8));
        Assert.Null(f.Tube.RainFolder); Assert.Null(f.Tube.RainActivePlayer);
    });
}
