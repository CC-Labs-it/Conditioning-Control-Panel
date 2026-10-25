using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class DashboardGiftPresentationTests
{
    private static Task WithView(Action<Window, SettingsTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var view = new SettingsTabView();
        var window = new Window { Content = view, Width = 1200, Height = 1000 };
        try { window.Show(); window.UpdateLayout(); body(window, view); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("takeover", "tab_takeover", 1)]
    [InlineData("awareness", "tab_awareness", 1)]
    [InlineData("haptics", "tab_haptics", 1)]
    [InlineData("voice", "tab_shelistening", 1)]
    [InlineData("fyp", "tab_fyp", 1)]
    [InlineData("remote", "tab_remote_control", 1)]
    [InlineData("dtrh", null, 2)]
    [InlineData("unknown", "mosaic_daily_gift", 0)]
    public Task GiftFacesUseTodaysIdentityAndPremiumOwnsThePool(string key, string? locKey, int tier) => WithView((_, view) =>
    {
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.IsFreeTodayProvider = k => k == key;
            CoreEntitlement.HasPremiumProvider = () => false;
            view.RefreshMysteryGift();
            Assert.Equal(Loc.Get("mosaic_free_today"), view.CardMystery.Title);
            Assert.Null(view.CardMystery.TierBadge);
            Assert.Equal(locKey == null ? "Down the Rabbit Hole" : Loc.Get(locKey), view.MysteryRevealName.Text);
            Assert.NotNull(((ImageBrush)view.MysteryRevealArt.Background!).Source);
            Assert.Equal(tier, view.MysteryRevealBadge.Tier);
            Assert.Equal(tier > 0, view.MysteryRevealBadge.FreeToday);
            CoreEntitlement.HasPremiumProvider = () => true;
            view.RefreshMysteryGift();
            Assert.Equal(0, view.MysteryRevealBadge.Tier);
            Assert.False(view.MysteryRevealBadge.FreeToday);
        }
        finally { CoreEntitlement.IsFreeTodayProvider = oldFree; CoreEntitlement.HasPremiumProvider = oldPremium; }
    });

    [Fact]
    public Task HoverSpendsOnlyTodaysNoticeAndHideSettlesThePlate() => WithView((window, view) =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.DailyGiftLastRevealDate, settings.MotionLevel);
        try
        {
            settings.DailyGiftLastRevealDate = "";
            settings.MotionLevel = MotionLevel.Off;
            view.RefreshMysteryGift();
            Assert.True(view.MysteryBadge.IsVisible);
            var host = view.MysteryFlipHost;
            host.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host,
                new Pointer(1, PointerType.Mouse, true), host, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), settings.DailyGiftLastRevealDate);
            Assert.False(view.MysteryBadge.IsVisible);
            Assert.True(view.MysteryRevealFace.IsVisible);
            Assert.False(view.CardMystery.IsVisible);
            view.IsVisible = false;
            Assert.False(view.MysteryRevealFace.IsVisible);
            Assert.Equal(1, ((ScaleTransform)((TransformGroup)host.RenderTransform!).Children[0]).ScaleX);
            view.IsVisible = true;
            Assert.False(view.MysteryBadge.IsVisible);
        }
        finally { (settings.DailyGiftLastRevealDate, settings.MotionLevel) = saved; }
    });

    [Fact]
    public Task TurnReversesFromCurrentScaleAndSettlesAtEachHalfBoundary() => WithView((window, view) =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.MotionLevel, settings.PerformanceMode, settings.DailyGiftLastRevealDate);
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        try
        {
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            BeatLoop.Time = time;
            var host = view.MysteryFlipHost;
            var scale = (ScaleTransform)((TransformGroup)host.RenderTransform!).Children[0];
            void Enter()
            {
                var point = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), window)!.Value;
                host.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host,
                    new Pointer(3, PointerType.Mouse, true), window, point, 0, new PointerPointProperties(), KeyModifiers.None));
            }
            void Step(int ms) { time.Advance(TimeSpan.FromMilliseconds(ms)); BeatLoop.StepAll(); }
            Enter();
            Step(75);
            Assert.Equal(0.875, scale.ScaleX, 3); // cubic EaseIn at halfway through close
            host.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, host,
                new Pointer(3, PointerType.Mouse, true), window, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Step(20); // reversed opening has only 0.125 of a half left
            Assert.Equal(1, scale.ScaleX);
            Assert.True(view.CardMystery.IsVisible);
            Enter();
            Step(150);
            Assert.Equal(0, scale.ScaleX, 3);
            Assert.True(view.MysteryRevealFace.IsVisible);
            Step(150);
            Assert.Equal(1, scale.ScaleX);
            view.ResetGift(); // same teardown the panic registry calls
            Assert.True(view.CardMystery.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, host));
        }
        finally
        {
            view.ResetGift();
            BeatLoop.Time = oldTime;
            (settings.MotionLevel, settings.PerformanceMode, settings.DailyGiftLastRevealDate) = saved;
        }
    });

    [Fact]
    public Task ExitSwallowedMidTurnIsCorrectedByTheNextMoveOverTheView() => WithView((window, view) =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.MotionLevel, settings.PerformanceMode, settings.DailyGiftLastRevealDate);
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        try
        {
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            BeatLoop.Time = time;
            var host = view.MysteryFlipHost;
            var pointer = new Pointer(4, PointerType.Mouse, true);
            var inside = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), window)!.Value;
            var outside = host.TranslatePoint(new Point(host.Bounds.Width + 40, host.Bounds.Height / 2), window)!.Value;
            void Step(int ms) { time.Advance(TimeSpan.FromMilliseconds(ms)); BeatLoop.StepAll(); }
            host.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host,
                pointer, window, inside, 0, new PointerPointProperties(), KeyModifiers.None));
            Step(75);
            // The thin plate drops out of the hit-test under a still pointer: that exit is ignored.
            host.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, host,
                pointer, window, inside, 0, new PointerPointProperties(), KeyModifiers.None));
            // The pointer then leaves over the rest of the view, never over the plate.
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view,
                pointer, window, outside, 0, new PointerPointProperties(), KeyModifiers.None));
            for (int i = 0; i < 12; i++) Step(50);
            Assert.True(view.CardMystery.IsVisible);
            Assert.False(view.MysteryRevealFace.IsVisible);
        }
        finally
        {
            view.ResetGift();
            BeatLoop.Time = oldTime;
            (settings.MotionLevel, settings.PerformanceMode, settings.DailyGiftLastRevealDate) = saved;
        }
    });

    [Fact]
    public Task GrayArtPreservesAlphaCachesAndEachHalfMutesIndependently() => WithView((window, view) =>
    {
        using var pixels = new SkiaSharp.SKBitmap(1, 1);
        pixels.SetPixel(0, 0, new SkiaSharp.SKColor(200, 80, 20, 128));
        using var image = SkiaSharp.SKImage.FromBitmap(pixels);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = encoded.AsStream();
        using var source = new global::Avalonia.Media.Imaging.Bitmap(stream);
        var gray = CardArtMute.Gray(source)!;
        Assert.Same(gray, CardArtMute.Gray(source));
        using var saved = new System.IO.MemoryStream();
        gray.Save(saved);
        using var decoded = SkiaSharp.SKBitmap.Decode(saved.ToArray());
        var color = decoded.GetPixel(0, 0);
        Assert.Equal(color.Red, color.Green);
        Assert.Equal(color.Red, color.Blue);
        Assert.Equal(128, color.Alpha);
        using var widePixels = new SkiaSharp.SKBitmap(640, 10);
        using var wideImage = SkiaSharp.SKImage.FromBitmap(widePixels);
        using var widePng = wideImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var wideStream = widePng.AsStream();
        using var wide = new global::Avalonia.Media.Imaging.Bitmap(wideStream);
        Assert.Equal(new PixelSize(320, 5), CardArtMute.Gray(wide)!.PixelSize); // WPF shrinks to 320 first
        var oldMotion = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        try
        {
        var combo = new SplitFeatureCard { IconA = source, IconB = source, DashboardDepth = true,
            IsActiveA = true, IsActiveB = false };
        window.Content = combo;
        window.UpdateLayout();
        Assert.Equal(0, combo.FindControl<Border>("HalfMuteA")!.Opacity);
        Assert.Equal(1, combo.FindControl<Border>("HalfMuteB")!.Opacity);
        combo.IsActiveA = false;
        combo.IsActiveB = true;
        Assert.Equal(1, combo.FindControl<Border>("HalfMuteA")!.Opacity);
        Assert.Equal(0, combo.FindControl<Border>("HalfMuteB")!.Opacity);
        }
        finally { CoreSettings.Current.MotionLevel = oldMotion; }
    });

    [Fact]
    public Task SingleCardRestingStateRespectsLocksHoverPreviewAndOptOut() => WithView((window, view) =>
    {
        var card = new FeatureCard { DimWhenInactive = true };
        window.Content = card;
        window.UpdateLayout();
        var content = card.FindControl<Grid>("ContentRoot")!;
        var title = card.FindControl<TextBlock>("TxtTitle")!;
        Assert.Equal(0.62, content.Opacity);
        Assert.Equal(0.72, title.Opacity);
        Assert.Equal(1, card.FindControl<Border>("ImgIconMute")!.Opacity);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
            new Pointer(2, PointerType.Mouse, true), card, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.Equal(1, content.Opacity);
        card.IsLocked = true;
        card.IsActive = true;
        Assert.Equal(0.35, content.Opacity);
        Assert.Equal(1, title.Opacity);
        card.IsLocked = false;
        Assert.Equal(1, content.Opacity);
        card.DashboardDepth = true;
        card.IsActive = false;
        Assert.Equal(0.62, content.Opacity);
        card.SetTutorialPreview(true);
        Assert.Equal(1, content.Opacity);
        card.SetTutorialPreview(false);
        Assert.Equal(0.62, content.Opacity);
        card.DimWhenInactive = false;
        Assert.Equal(1, content.Opacity);
    });
}
