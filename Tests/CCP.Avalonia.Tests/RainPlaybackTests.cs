using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class RainPlaybackTests
{
    private static Task Run(Func<Task> action) => AvaloniaTestDispatcher.RunAsync(() => {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
            .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return action();
    });
    private sealed class Hold(Action fire, TimeSpan delay) : IDisposable {
        public TimeSpan Delay => delay;
        public bool Cancelled;
        public void Fire() => fire();
        public void Dispose() => Cancelled = true;
    }
    private static void Strip(string path)
    {
        using var bitmap = new SKBitmap(1, 2); bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(0, 1, SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, png.ToArray());
    }
    [Fact]
    public Task StripShowsEachFrameAtManifestFpsAndStopReleasesIt() => Run(async () => {
        var path = Path.Combine(Path.GetTempPath(), "rain-strip-" + Guid.NewGuid() + ".png");
        Strip(path);
        try {
            var image = new Image();
            using var player = new RainClipPlayer(image) { ManualClock = true, Now = () => 0 };
            await player.LoadStrip(path, 2, 10);
            Assert.True(player.IsReady); Assert.Equal(2, player.FrameCount);
            using (var frame = Assert.IsType<WriteableBitmap>(image.Source).Lock()) Assert.Equal(255, Marshal.ReadByte(frame.Address, 2));
            player.Tick(99); Assert.Equal(0, player.FrameIndex);
            player.Tick(100); Assert.Equal(1, player.FrameIndex);
            using (var frame = Assert.IsType<WriteableBitmap>(image.Source).Lock()) Assert.Equal(255, Marshal.ReadByte(frame.Address, 0));
            player.Tick(200); Assert.True(player.IsComplete); player.Pause(); player.ManualClock = false; player.Resume(); Assert.False(player.TimerRunning);
            player.Stop(); Assert.False(player.IsReady); Assert.Equal(0, player.FrameCount); Assert.Null(image.Source); long beatClock = 0; using var beatPlayer = new RainClipPlayer(image) { Now = () => beatClock }; await beatPlayer.LoadStrip(path, 2, 10); Assert.Contains(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, image)); beatClock = 100; BeatLoop.StepAll(); Assert.Equal(1, beatPlayer.FrameIndex); beatPlayer.Pause(); beatClock += 10_000; BeatLoop.StepAll(); Assert.Equal(1, beatPlayer.FrameIndex); beatPlayer.Resume(); beatClock += 99; BeatLoop.StepAll(); Assert.False(beatPlayer.IsComplete); beatClock++; BeatLoop.StepAll(); Assert.True(beatPlayer.IsComplete); Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, image));
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task TubeUsesValidPackFallsBackAndCleansUpOnClose() => Run(async () => {
        var root = Path.Combine(Path.GetTempPath(), "rain-host-" + Guid.NewGuid());
        var hash = new string('a', 64); var folder = Path.Combine(root, hash); Directory.CreateDirectory(folder);
        Strip(Path.Combine(folder, "idle.strip.png"));
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{idleRotation:[{clip:'idle',weight:1}],fadeMs:0}");
        File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "manifest.json"), $"{{rev:1,lookHash:'{hash}',clips:{{idle:{{file:'idle.strip.png',frames:2,fps:10,w:1,h:1}}}}}}");
        var service = new SettingsService(); service.Current.AvatarGeneration = "v2"; service.Current.RainPackHash = hash;
        var savedSettings = CoreSettings.ServiceProvider; var savedMod = CoreMods.ActiveModIdProvider; AvatarTubeWindow? tube = null;
        try {
            CoreSettings.ServiceProvider = () => service; CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
            tube = new AvatarTubeWindow(null) { RainPackRoot = root };
            // The release switch is off: a saved "v2" plays V1 and stays saved (WPF CirceEmotes.cs:169).
            // V1 is the mod's embedded avatar0 emote set (WPF ResolveV1EmoteFolder, CirceEmotes.cs:178-188).
            tube.Show(); await tube.RefreshRainPlayback(); Assert.Equal("avatar0_emotes", tube.RainFolder); Assert.Equal("v2", service.Current.AvatarGeneration); tube.Hide();
            tube.RainGateOn = () => true;
            await tube.RefreshRainPlayback(); Assert.Null(tube.RainActivePlayer); tube.Show(); await tube.RefreshRainPlayback();
            Assert.Equal(folder, tube.RainFolder); Assert.True(tube.RainActivePlayer!.IsReady); Assert.Equal(2, tube.RainActivePlayer.FrameCount);
            var paused = tube.RainActivePlayer; long clock = paused.FrameStarted; paused.Now = () => clock; Assert.True(paused.TimerRunning); clock += 40; paused.Tick(clock); tube.Hide(); Assert.False(paused.TimerRunning);
            clock += 10_000; paused.Tick(clock); Assert.Equal(0, paused.FrameIndex); tube.Show(); Assert.Same(paused, tube.RainActivePlayer); Assert.True(paused.TimerRunning);
            clock += 59; paused.Tick(clock); Assert.Equal(0, paused.FrameIndex); clock++; paused.Tick(clock); Assert.Equal(1, paused.FrameIndex);
            clock += 100; paused.Tick(clock); Assert.True(paused.IsComplete); for (var i = 0; i < 40 && ReferenceEquals(paused, tube.RainActivePlayer); i++) await Task.Delay(50); Assert.NotSame(paused, tube.RainActivePlayer); Assert.True(tube.RainActivePlayer!.IsReady);
            service.Current.AvatarGeneration = "v1";
            Assert.Equal("avatar0_emotes", tube.RainFolder);
            // The V1 clips are GIFs (WPF ResolveEmoteClipUri, CirceEmotes.cs:1238): the tube swaps to them once decoded.
            for (var i = 0; i < 100 && tube.RainActivePlayer?.IsReady != true; i++) await Task.Delay(50);
            Assert.True(tube.RainActivePlayer!.IsReady); Assert.True(tube.RainActivePlayer.FrameCount > 1); Assert.False(tube.FindControl<Image>("ImgAvatar")!.IsVisible);
            // A mod row whose clips this head does not embed keeps the static poses.
            CoreMods.ActiveModIdProvider = () => "builtin-locked"; await tube.RefreshRainPlayback();
            Assert.Null(tube.RainFolder); Assert.True(tube.FindControl<Image>("ImgAvatar")!.IsVisible);
            CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
            service.Current.AvatarGeneration = "v2"; await tube.RefreshRainPlayback();
            Assert.Equal(folder, tube.RainFolder);
            Assert.True(tube.FindControl<Image>("ImgAvatarAnimated")!.IsVisible || tube.FindControl<Image>("ImgAvatarAnimatedB")!.IsVisible);
            File.Delete(Path.Combine(folder, "manifest.json"));
            await tube.RefreshRainPlayback();
            Assert.Equal("rain_emotes", tube.RainFolder); Assert.True(tube.RainActivePlayer!.IsReady);
            var player = tube.RainActivePlayer; tube.Close();
            Assert.False(player!.IsReady); Assert.Null(tube.RainFolder);
        }
        finally { try { tube?.Close(); } finally { CoreSettings.ServiceProvider = savedSettings; CoreMods.ActiveModIdProvider = savedMod; Assert.Same(savedSettings, CoreSettings.ServiceProvider); Assert.Same(savedMod, CoreMods.ActiveModIdProvider); Directory.Delete(root, true); } }
    });
    [Fact]
    public Task FailuresKeepRetryingAndClickRequestsRespectHold() => Run(async () => {
        var root = Path.Combine(Path.GetTempPath(), "rain-retry-" + Guid.NewGuid());
        var hash = new string('b', 64); var folder = Path.Combine(root, hash); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "idle.strip.png"), "corrupt"); Strip(Path.Combine(folder, "giggle.strip.png")); Strip(Path.Combine(folder, "alternate.strip.png")); Strip(Path.Combine(folder, "fixed.png"));
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{idleRotation:[{clip:'idle'}],clickEmotes:['giggle','absent'],fadeMs:0}");
        File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "manifest.json"), $"{{rev:1,lookHash:'{hash}',clips:{{idle:{{file:'idle.strip.png',frames:2,fps:10,w:1,h:1}},giggle:{{file:'giggle.strip.png',frames:2,fps:1,w:1,h:1}},alternate:{{file:'alternate.strip.png',frames:2,fps:1,w:1,h:1}}}}}}");
        var service = new SettingsService(); service.Current.AvatarGeneration = "v2"; service.Current.RainPackHash = hash;
        var savedSettings = CoreSettings.ServiceProvider; var savedMod = CoreMods.ActiveModIdProvider; AvatarTubeWindow? tube = null; long clock = 10_000; Hold? hold = null;
        try {
            CoreSettings.ServiceProvider = () => service; CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
            tube = new AvatarTubeWindow(null) { RainPackRoot = root, RainRetryDelay = TimeSpan.FromMilliseconds(25), RainGateOn = () => true };
            tube.RainClock = () => clock; tube.RainHoldScheduler = (fire, delay) => hold = new Hold(fire, delay);
            tube.Show(); await tube.RefreshRainPlayback();
            Assert.Null(tube.RainActivePlayer); Assert.True(tube.FindControl<Image>("ImgAvatar")!.IsVisible);
            for (var i = 0; i < 400 && tube.RainFailureCount < 5; i++) await Task.Delay(10); Assert.True(tube.RainFailureCount >= 5);
            var fadeField = typeof(AvatarTubeWindow).GetField("_rainFade", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!; ((IDisposable?)fadeField.GetValue(tube))?.Dispose(); var fadeProbe = new Hold(() => { }, TimeSpan.Zero); fadeField.SetValue(tube, fadeProbe);
            tube.Hide(); Assert.True(fadeProbe.Cancelled); var hiddenFailures = tube.RainFailureCount; await Task.Delay(150); Assert.Equal(hiddenFailures, tube.RainFailureCount); tube.Show(); for (var i = 0; i < 400 && tube.RainFailureCount == hiddenFailures; i++) await Task.Delay(10); Assert.True(tube.RainFailureCount > hiddenFailures);
            tube.RainRetryDelay = TimeSpan.FromSeconds(1); await tube.RefreshRainPlayback(); Assert.Equal(1, tube.RainFailureCount);
            tube.RainRetryDelay = TimeSpan.FromMilliseconds(25); for (var i = 0; i < 400 && tube.RainFailureCount < 5; i++) await Task.Delay(10); Assert.True(tube.RainFailureCount >= 5);
            for (var i = 0; ; i++) { try { File.Move(Path.Combine(folder, "fixed.png"), Path.Combine(folder, "idle.strip.png"), true); break; } catch (IOException) when (i < 100) { await Task.Delay(20); } }
            for (var i = 0; i < 60 && tube.RainActivePlayer?.IsReady != true; i++) await Task.Delay(50);
            Assert.NotNull(tube.RainActivePlayer); Assert.True(tube.RainActivePlayer!.IsReady); Assert.Equal(0, tube.RainFailureCount);
            var version = tube.RainVersion; tube.RequestRainClip("idle"); Assert.Equal(version, tube.RainVersion); Assert.Null(hold);
            Assert.True(tube.RainClickEmote()); Assert.False(tube.RainClickEmote());
            Assert.NotNull(hold); Assert.Equal(TimeSpan.FromMilliseconds(2000), hold!.Delay); Assert.Equal("idle", tube.RainClip); tube.RequestRainClip("idle"); Assert.False(hold.Cancelled); await Task.Delay(600); Assert.Equal(version, tube.RainVersion);
            clock += 1999; Assert.Equal("idle", tube.RainClip); clock++; hold.Fire();
            for (var i = 0; i < 40 && tube.RainClip != "giggle"; i++) await Task.Delay(50);
            Assert.Equal("giggle", tube.RainClip);
            tube.RequestRainClip("idle"); var old = hold!; clock += 1999; tube.RequestRainClip("alternate"); Assert.True(old.Cancelled);
            Assert.Equal(TimeSpan.FromMilliseconds(1), hold!.Delay); var coalesced = tube.RainVersion; old.Fire(); Assert.Equal(coalesced, tube.RainVersion); Assert.Equal("giggle", tube.RainClip);
            clock++; hold.Fire();
            for (var i = 0; i < 40 && tube.RainClip != "alternate"; i++) await Task.Delay(50);
            Assert.Equal("alternate", tube.RainClip);
            File.WriteAllText(Path.Combine(folder, "idle.strip.png"), "corrupt");
            var shown = tube.RainActivePlayer; await tube.PlayRainClip("idle");
            Assert.Same(shown, tube.RainActivePlayer); Assert.True(shown!.IsReady); Assert.Equal("alternate", tube.RainClip);
            tube.RequestRainClip("giggle"); var hidden = hold!; tube.Hide(); Assert.True(hidden.Cancelled); hidden.Fire(); Assert.Equal("alternate", tube.RainClip);
            tube.Show(); var visibleVersion = tube.RainVersion; hidden.Fire(); Assert.Equal(visibleVersion, tube.RainVersion); tube.RequestRainClip("giggle"); var cancelled = hold!; tube.Close(); Assert.True(cancelled.Cancelled);
            cancelled.Fire(); Assert.Null(tube.RainClip); Assert.Equal(0, tube.RainFailureCount);
        }
        finally { try { tube?.Close(); } finally { CoreSettings.ServiceProvider = savedSettings; CoreMods.ActiveModIdProvider = savedMod; Assert.Same(savedSettings, CoreSettings.ServiceProvider); Assert.Same(savedMod, CoreMods.ActiveModIdProvider); Directory.Delete(root, true); } }
    });
}
