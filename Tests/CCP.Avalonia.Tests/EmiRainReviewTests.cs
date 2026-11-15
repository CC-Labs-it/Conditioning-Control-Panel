using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRainReviewTests
{
    private const string Gif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";
    private const string Lines = "{\"version\":1,\"moments\":{\"m\":{\"priority\":3,\"scripted\":true,\"askOdds\":1.0}},\"pools\":{},\"asks\":[{\"id\":\"rain.ask\",\"moment\":\"m\",\"q\":\"rain?\",\"chips\":[\"yes\",\"no\"],\"effect\":\"rain\"}]}";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task IdleFlipGlitchesAndPointerActivityClosesIt()
    {
        var root = Directory.CreateTempSubdirectory("emi-review-").FullName;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                Setup(); var oldPath = CorePaths.EffectiveAssetsProvider; bool oldGlass = CoreSettings.Current.EmiDeskGlass; bool oldOffers = CoreSettings.Current.EmiDeskOffers;
                EmiDeskWindow? w = null;
                try
                {
                    CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.EmiDeskGlass = true; CoreSettings.Current.EmiDeskOffers = false;
                    var dir = Directory.CreateDirectory(Path.Combine(root, "images"));
                    File.WriteAllBytes(Path.Combine(dir.FullName, "a.gif"), Convert.FromBase64String(Gif));
                    w = new EmiDeskWindow(); w.Show(); Dispatcher.UIThread.RunJobs();
                    var type = typeof(EmiDeskWindow);
                    // One glass director (b209): the idle flip is PollGlass's, and rain is one of its channels.
                    var oldPick = EmiDeskWindow.GlassPick;
                    EmiDeskWindow.GlassPick = _ => "rain";
                    try { w.PollGlass(DateTime.UtcNow.AddMinutes(2)); }
                    finally { EmiDeskWindow.GlassPick = oldPick; }
                    var glitch = Assert.IsType<Canvas>(type.GetField("_rainGlitch", Private)!.GetValue(w));
                    Assert.InRange(glitch.Children.Count, 3, 5);
                    Assert.NotEmpty(w.GlassHost.Children);
                    type.GetField("_rainOpened", Private)!.SetValue(w, DateTime.UtcNow.AddSeconds(-1));
                    type.GetMethod("RainTick", Private)!.Invoke(w, new object?[] { null, EventArgs.Empty });
                    Assert.Null(type.GetField("_rainGlitch", Private)!.GetValue(w));
                    var body = w.FindControl<Control>("BodyRoot")!;
                    var point = body.TranslatePoint(w.GlassRect.Center, w)!.Value;
                    w.MouseMove(point);
                    Assert.Empty(w.GlassHost.Children);
                    Assert.True(w.TryShowRainChannel());
                    var gear = w.FindControl<Button>("BtnGear")!;
                    gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Empty(w.GlassHost.Children);
                    gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(w.TryShowRainChannel());
                    var grip = w.FindControl<Control>("ResizeGrip")!;
                    var gripPoint = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), w)!.Value;
                    w.MouseDown(gripPoint, MouseButton.Left); w.MouseUp(gripPoint, MouseButton.Left);
                    Assert.Empty(w.GlassHost.Children);
                }
                finally { w?.ShutDown(); CorePaths.EffectiveAssetsProvider = oldPath; CoreSettings.Current.EmiDeskGlass = oldGlass; CoreSettings.Current.EmiDeskOffers = oldOffers; }
                return Task.CompletedTask;
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FeasibleOfferAcceptsRainAndDismissalCancelsAnUnansweredOffer()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json")));
        var root = Directory.CreateTempSubdirectory("emi-ask-").FullName;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                Setup(); var oldPath = CorePaths.EffectiveAssetsProvider; bool oldOffers = CoreSettings.Current.EmiDeskOffers; bool oldGlass = CoreSettings.Current.EmiDeskGlass;
                var oldBedtime = EmiState.Current.BedtimeUntil; EmiDeskWindow? w = null;
                ChaosGifCascadeOverlay? rain = null;
                try
                {
                    CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.EmiDeskOffers = true; CoreSettings.Current.EmiDeskGlass = true;
                    EmiState.Current.BedtimeUntil = DateTime.MinValue;
                    ChaosGifCascadeOverlay.CloseActive(); ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    w = new EmiDeskWindow(); w.Show(); Dispatcher.UIThread.RunJobs();
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m");
                    Assert.False(w.RainAskLive);
                    var dir = Directory.CreateDirectory(Path.Combine(root, "images"));
                    File.WriteAllBytes(Path.Combine(dir.FullName, "a.gif"), Convert.FromBase64String(Gif));
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m");
                    Assert.True(w.RainAskLive);
                    Assert.False(w.TryShowRainChannel());
                    var border = Assert.IsType<Border>(Assert.Single(w.BubbleHost.Children));
                    var panel = Assert.IsType<StackPanel>(border.Child);
                    var chips = Assert.IsType<StackPanel>(panel.Children[1]);
                    Assert.IsType<Button>(chips.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(w.RainAskLive);
                    Assert.NotNull(EmiGifRain.LastPick);
                    await EmiGifRain.LastPick!; Dispatcher.UIThread.RunJobs();
                    Assert.True(ChaosGifCascadeOverlay.IsRaining);
                    rain = ChaosGifCascadeOverlay.Active;
                    await StopRainAndDrainAsync(rain);
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m");
                    Assert.True(w.RainAskLive);
                    var lastPick = EmiGifRain.LastPick;
                    ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
                    border = Assert.IsType<Border>(Assert.Single(w.BubbleHost.Children));
                    panel = Assert.IsType<StackPanel>(border.Child); chips = Assert.IsType<StackPanel>(panel.Children[1]);
                    Assert.IsType<Button>(chips.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(w.RainAskLive); Assert.Null(ChaosGifCascadeOverlay.Active);
                    Assert.Same(lastPick, EmiGifRain.LastPick);
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m"); Assert.False(w.RainAskLive);
                    ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m"); Assert.True(w.RainAskLive);
                    ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
                    typeof(EmiDeskWindow).GetMethod("RainTick", Private)!.Invoke(w, new object?[] { null, EventArgs.Empty });
                    Assert.False(w.RainAskLive);
                    ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
                    w.FireRainOffer(EmiLineEngine.FromJson(Lines), "m");
                    Assert.True(w.RainAskLive);
                    w.RunDismiss();
                    Assert.False(w.RainAskLive);
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                }
                finally
                {
                    ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
                    rain ??= ChaosGifCascadeOverlay.Active;
                    try
                    {
                        w?.ShutDown(); EmiGifRain.Stop(); ChaosGifCascadeOverlay.CloseActive();
                        if (rain != null) await rain.PendingDecodes.WaitAsync(TimeSpan.FromSeconds(10));
                        Dispatcher.UIThread.RunJobs();
                    }
                    finally
                    {
                        ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false; ChaosGifCascadeOverlay.ManualFramesForTests = false;
                        CorePaths.EffectiveAssetsProvider = oldPath; CoreSettings.Current.EmiDeskOffers = oldOffers; CoreSettings.Current.EmiDeskGlass = oldGlass; EmiState.Current.BedtimeUntil = oldBedtime;
                    }
                }
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task StopRainAndDrainAsync(ChaosGifCascadeOverlay? rain)
    {
        EmiGifRain.Stop();
        if (rain != null) await rain.PendingDecodes.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RainStopWaitsForInFlightDecodeBeforeDeletingImages()
    {
        var root = Directory.CreateTempSubdirectory("emi-drain-").FullName;
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "images")).FullName, "a.gif");
        File.WriteAllBytes(path, Convert.FromBase64String(Gif));
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                Setup();
                var oldPath = CorePaths.EffectiveAssetsProvider;
                var oldHook = ChaosGifCascadeOverlay.BeforeDecodeForTests;
                var oldSkip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
                var oldManual = ChaosGifCascadeOverlay.ManualFramesForTests;
                var oldPacks = ConditioningControlPanel.Services.ContentPackStore.Current;
                ChaosGifCascadeOverlay? rain = null;
                try
                {
                    EmiGifRain.Stop(); ChaosGifCascadeOverlay.CloseActive();
                    CorePaths.EffectiveAssetsProvider = () => root;
                    ConditioningControlPanel.Services.ContentPackStore.Current = null;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    ChaosGifCascadeOverlay.BeforeDecodeForTests = file =>
                    {
                        using var held = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
                        started.TrySetResult();
                        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("decode was not released");
                    };
                    Assert.True(EmiGifRain.Start(null));
                    await EmiGifRain.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    rain = Assert.IsType<ChaosGifCascadeOverlay>(ChaosGifCascadeOverlay.Active);
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    var drained = StopRainAndDrainAsync(rain);
                    Assert.False(drained.IsCompleted);
                    release.Set();
                    await drained.WaitAsync(TimeSpan.FromSeconds(10));
                    File.Delete(path);
                    Assert.False(File.Exists(path));
                }
                finally
                {
                    release.Set();
                    try
                    {
                        EmiGifRain.Stop(); ChaosGifCascadeOverlay.CloseActive();
                        if (rain != null) await rain.PendingDecodes.WaitAsync(TimeSpan.FromSeconds(10));
                        Dispatcher.UIThread.RunJobs();
                    }
                    finally
                    {
                        ChaosGifCascadeOverlay.BeforeDecodeForTests = oldHook;
                        ChaosGifCascadeOverlay.SkipPlatformChecksForTests = oldSkip;
                        ChaosGifCascadeOverlay.ManualFramesForTests = oldManual;
                        ConditioningControlPanel.Services.ContentPackStore.Current = oldPacks;
                        CorePaths.EffectiveAssetsProvider = oldPath;
                    }
                }
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
