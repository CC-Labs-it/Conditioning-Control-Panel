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
    private static void Strip(string path)
    {
        using var bitmap = new SKBitmap(1, 2);
        bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(0, 1, SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
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
            player.Tick(200); Assert.True(player.IsComplete);
            player.Stop(); Assert.False(player.IsReady); Assert.Equal(0, player.FrameCount); Assert.Null(image.Source);
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
        CoreSettings.ServiceProvider = () => service; CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
        var tube = new AvatarTubeWindow(null) { RainPackRoot = root };
        try {
            tube.Show(); await tube.RefreshRainPlayback();
            Assert.Equal(folder, tube.RainFolder); Assert.True(tube.RainActivePlayer!.IsReady);
            Assert.Equal(2, tube.RainActivePlayer.FrameCount);
            service.Current.AvatarGeneration = "v1";
            Assert.Null(tube.RainFolder); Assert.True(tube.FindControl<Image>("ImgAvatar")!.IsVisible);
            service.Current.AvatarGeneration = "v2"; await tube.RefreshRainPlayback();
            Assert.Equal(folder, tube.RainFolder);
            Assert.True(tube.FindControl<Image>("ImgAvatarAnimated")!.IsVisible || tube.FindControl<Image>("ImgAvatarAnimatedB")!.IsVisible);
            File.Delete(Path.Combine(folder, "manifest.json"));
            await tube.RefreshRainPlayback();
            Assert.Equal("rain_emotes", tube.RainFolder); Assert.True(tube.RainActivePlayer!.IsReady);
            var player = tube.RainActivePlayer; tube.Close();
            Assert.False(player!.IsReady); Assert.Null(tube.RainFolder);
        }
        finally { tube.Close(); Directory.Delete(root, true); }
    });
}
