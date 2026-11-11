using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiGifRainTests
{
    private const string TinyGif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";
    private sealed class Clock : TimeProvider
    {
        public long Now = TimeSpan.FromSeconds(10).Ticks;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds).Ticks;
    }

    [Fact]
    public async Task GlassClickRainsAndDismissalStopsIt()
    {
        var root = Directory.CreateTempSubdirectory("emi-rain-").FullName;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var previous = CorePaths.EffectiveAssetsProvider;
                bool glass = CoreSettings.Current.EmiDeskGlass;
                var clock = new Clock();
                EmiDeskWindow? owner = null;
                try
                {
                    CorePaths.EffectiveAssetsProvider = () => root;
                    CoreSettings.Current.EmiDeskGlass = true;
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.Clock = clock;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    owner = new EmiDeskWindow();
                    owner.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(owner.GlassHost.ClipToBounds);
                    Assert.Empty(EmiGifRain.Images());
                    Assert.False(owner.TryShowRainChannel());
                    Assert.False(EmiGifRain.Start(owner));
                    var dir = Directory.CreateDirectory(Path.Combine(root, "images"));
                    File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "not an image");
                    Assert.Empty(EmiGifRain.Images());
                    File.WriteAllBytes(Path.Combine(dir.FullName, "a.GIF"), Convert.FromBase64String(TinyGif));
                    Assert.Single(EmiGifRain.Images());
                    CoreSettings.Current.EmiDeskGlass = false;
                    Assert.False(owner.TryShowRainChannel());
                    CoreSettings.Current.EmiDeskGlass = true;
                    Assert.True(owner.TryShowRainChannel());
                    owner.Topmost = false;
                    var body = owner.FindControl<Control>("BodyRoot")!;
                    var point = body.TranslatePoint(owner.GlassRect.Center, owner)!.Value;
                    owner.MouseDown(point, MouseButton.Left);
                    owner.MouseUp(point, MouseButton.Left);
                    Assert.NotNull(EmiGifRain.LastPick);
                    Assert.False(EmiGifRain.Start(owner));
                    await EmiGifRain.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    var overlay = Assert.IsType<ChaosGifCascadeOverlay>(ChaosGifCascadeOverlay.Active);
                    Assert.True(owner.Topmost);
                    Assert.True(overlay.Cascade.Spawning);
                    Assert.False(EmiGifRain.Start(owner));
                    clock.Advance(9); overlay.Frame();
                    Assert.True(overlay.Cascade.Spawning);
                    clock.Advance(1.1); overlay.Frame();
                    Assert.False(overlay.Cascade.Spawning);
                    owner.RunDismiss();
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                    Assert.True(EmiGifRain.Start(owner));
                    Assert.False(EmiGifRain.Start(owner)); // the first pool pick is still pending
                    var pick = EmiGifRain.LastPick!;
                    EmiGifRain.Stop();
                    await pick;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                    Assert.True(EmiGifRain.Start(owner));
                    pick = EmiGifRain.LastPick!;
                    ChaosGifCascadeOverlay.CloseActive(); // panic can overtake the pool pick
                    await pick;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                    Assert.True(EmiGifRain.Start(owner));
                    await EmiGifRain.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    ChaosGifCascadeOverlay.ShowWithFiles(new() { Path.Combine(dir.FullName, "a.GIF") },
                        GifCascade.PayloadSpawnRatePerSec, 6, GifCascade.PayloadGifSize,
                        GifCascade.PayloadFallSpeed, GifCascade.PayloadOpacity, GifCascade.PayloadStartScale);
                    EmiGifRain.Stop();
                    Assert.NotNull(ChaosGifCascadeOverlay.Active);
                }
                finally
                {
                    owner?.ShutDown();
                    EmiGifRain.Stop();
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.Clock = TimeProvider.System;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                    CorePaths.EffectiveAssetsProvider = previous;
                    CoreSettings.Current.EmiDeskGlass = glass;
                }
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
