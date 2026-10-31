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
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using ConditioningControlPanel.Services;

namespace CCP.Avalonia.Tests;

public sealed class DashboardGiftPresentationTests
{
    private static Task WithView(Action<Window, SettingsTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var view = new SettingsTabView();
        var window = new Window { Content = view, Width = 1200, Height = 1000 };
        try { window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); body(window, view); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    internal static DailyFreeService GiftFor(string key)
    {
        var gift = new DailyFreeService(fetchOverride: false);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(DailyFreeService).GetField("_serverKey", flags)!.SetValue(gift, key);
        typeof(DailyFreeService).GetField("_serverKeyForDate", flags)!.SetValue(gift, DateTime.Now.ToString("yyyy-MM-dd"));
        ((System.Threading.Timer)typeof(DailyFreeService).GetField("_dayRollTimer", flags)!.GetValue(gift)!).Dispose();
        return gift;
    }

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
        var oldDaily = AvApp.DailyFree;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            AvApp.DailyFree = GiftFor(key);
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
        finally { AvApp.DailyFree = oldDaily; CoreEntitlement.HasPremiumProvider = oldPremium; }
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
            Assert.Equal(0.75, ((SkewTransform)((TransformGroup)host.RenderTransform!).Children[1]).AngleY, 3);
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
    public Task UnopenedBreathUsesSharedTimeAndParksOnMotionGateAndDetach() => WithView((window, view) =>
    {
        var settings = CoreSettings.Current;
        settings.DailyGiftLastRevealDate = "";
        settings.MotionLevel = MotionLevel.Full;
        settings.PerformanceMode = false;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        try
        {
            BeatLoop.Time = time;
            settings.GentleVisuals = false;
            settings.AutoPerformanceMode = false;
            ((Action)typeof(global::Avalonia.Platform.IWindowBaseImpl)
                .GetProperty("Activated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(window.PlatformImpl)!).Invoke();
            Assert.True(window.IsActive);
            view.ResetGift();
            view.SyncMysteryGiftFx();
            Assert.Contains(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, view.MysteryBadge));
            time.Advance(TimeSpan.FromMilliseconds(900));
            BeatLoop.StepAll();
            Assert.Equal(0.95, view.MysteryGlow.Opacity, 3);
            var scale = (ScaleTransform)((TransformGroup)view.MysteryBadge.RenderTransform!).Children[0];
            Assert.Equal(1.12, scale.ScaleX, 3);
            settings.MotionLevel = MotionLevel.Off;
            view.SyncMysteryGiftFx();
            Assert.Equal(0.55, view.MysteryGlow.Opacity);
            Assert.Equal(1, scale.ScaleX);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, view.MysteryBadge));
            window.Content = null;
            settings.MotionLevel = MotionLevel.Full;
            view.RefreshMysteryGift(); // a queued repaint after detach must stay parked
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, view.MysteryBadge));
            Assert.True(view.CardMystery.IsVisible);
            Assert.False(view.MysteryRevealFace.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, view.MysteryFlipHost));
        }
        finally { view.ResetGift(); BeatLoop.Time = oldTime; }
    });
    [Fact]
    public Task PanicRegistryClosesTheRevealedGift() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            shell.Show();
            var view = shell.SettingsPage!;
            var host = view.MysteryFlipHost;
            host.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host,
                new Pointer(5, PointerType.Mouse, true), host, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.True(view.MysteryRevealFace.IsVisible);
            var stop = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All,
                surface => surface.Id == "dashboard-gift");
            stop.Stop(shell);
            Assert.False(view.MysteryRevealFace.IsVisible);
            Assert.True(view.CardMystery.IsVisible);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}
