using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DashboardDecorationTests
{
    [Fact]
    public Task MysteryDoorbellFlipReversalAndTeardown() => WithShell((shell, tab, time) =>
    {
        var badge = tab.FindControl<Border>("MysteryBadge")!;
        var glow = tab.FindControl<Border>("MysteryGlow")!;
        var face = tab.FindControl<Border>("MysteryRevealFace")!;
        var host = tab.FindControl<Grid>("MysteryFlipHost")!;
        var plate = Assert.IsType<Grid>(Assert.Single(host.Children));
        var scale = (ScaleTransform)((TransformGroup)plate.RenderTransform!).Children[0];
        tab.ApplyDashboardDecoration(true, true);
        Assert.True(badge.IsVisible);
        Assert.Null(badge.Effect);
        time.Advance(TimeSpan.FromSeconds(.9)); BeatLoop.StepAll();
        Assert.Equal(.95, glow.Opacity, 3);
        Assert.Equal(1.12, ((ScaleTransform)((TransformGroup)badge.RenderTransform!).Children[0]).ScaleX, 3);
        host.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, host,
            new Pointer(1, PointerType.Mouse, true), shell, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.False(badge.IsVisible);
        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), CoreSettings.Current.DailyGiftLastRevealDate);
        time.Advance(TimeSpan.FromSeconds(.075)); BeatLoop.StepAll();
        Assert.InRange(scale.ScaleX, .8, .99);
        double before = scale.ScaleX;
        host.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, host,
            new Pointer(1, PointerType.Mouse, true), shell, default, 0, PointerPointProperties.None, KeyModifiers.None)); // reverse before crossing
        Assert.Equal(before, scale.ScaleX);
        time.Advance(TimeSpan.FromSeconds(.15)); BeatLoop.StepAll();
        Assert.Equal(1, scale.ScaleX); Assert.False(face.IsVisible);
        tab.RequestMysteryFace(true);
        time.Advance(TimeSpan.FromSeconds(.15)); BeatLoop.StepAll();
        Assert.True(face.IsVisible); Assert.Equal(0, scale.ScaleX);
        time.Advance(TimeSpan.FromSeconds(.15)); BeatLoop.StepAll();
        Assert.Equal(1, scale.ScaleX);
        tab.RequestMysteryFace(false);
        tab.ApplyDashboardDecoration(false, false); // motion off during turn settles wanted face
        Assert.False(face.IsVisible); Assert.Equal(1, scale.ScaleX);
        tab.RequestMysteryFace(true); Assert.True(face.IsVisible); // still reveals without motion
        shell.ShowTab("appsettings"); Dispatcher.UIThread.RunJobs();
        Assert.False(face.IsVisible);
        shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
        Assert.False(badge.IsVisible); // tab bounce never re-arms an opened gift
        CoreSettings.Current.DailyGiftLastRevealDate = "";
        tab.ApplyDashboardDecoration(false, false);
        Assert.True(badge.IsVisible); Assert.Equal(.55, glow.Opacity, 3);
    });

    [Fact]
    public Task MysteryRevealUsesDailyFeatureAndPremiumTierRules() => WithShell((shell, tab, time) =>
    {
        var oldDaily = CoreEntitlement.DailyFreeKeyProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.DailyFreeKeyProvider = () => "dtrh";
            CoreEntitlement.HasPremiumProvider = () => false;
            tab.ApplyDashboardDecoration(false, false);
            Assert.Equal("Down the Rabbit Hole", tab.FindControl<TextBlock>("MysteryRevealName")!.Text);
            var badge = tab.FindControl<TierBadge>("MysteryRevealBadge")!;
            Assert.Equal(2, badge.Tier); Assert.True(badge.FreeToday);
            CoreEntitlement.HasPremiumProvider = () => true;
            tab.ApplyDashboardDecoration(false, false);
            Assert.Equal(0, badge.Tier); Assert.False(badge.FreeToday);
        }
        finally
        {
            CoreEntitlement.DailyFreeKeyProvider = oldDaily;
            CoreEntitlement.HasPremiumProvider = oldPremium;
        }
    });

    // WPF MainWindow.Presets.cs:1086, :1171-1208: reveal and click follow TodayKey.
    [Fact]
    public Task MysterySelectionIgnoresOtherEligibleGrants() => WithShell((shell, tab, time) =>
    {
        CoreEntitlement.DailyFreeKeyProvider = () => "dtrh";
        CoreEntitlement.IsFreeTodayProvider = key => key is "takeover" or "awareness" or "dtrh";
        CoreEntitlement.HasPremiumProvider = () => false;
        var artRequests = new List<string>();
        CoreModArt.OverridePathProvider = path => { artRequests.Add(path); return null; };
        tab.ApplyDashboardDecoration(false, false);
        Assert.Equal("Down the Rabbit Hole", tab.FindControl<TextBlock>("MysteryRevealName")!.Text);
        var badge = tab.FindControl<TierBadge>("MysteryRevealBadge")!;
        Assert.Equal(2, badge.Tier);
        Assert.True(badge.FreeToday);
        Assert.Contains("features/dtrh.png", artRequests);
        Assert.DoesNotContain("features/takeover.png", artRequests);
        var box = tab.FindControl<ConditioningControlPanel.Avalonia.Views.Features.FeatureCard>("CardMystery")!;
        box.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(
            ConditioningControlPanel.Avalonia.Views.Features.FeatureCard.ClickEvent, box));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("play", shell.CurrentTab);
    });

    [Fact]
    public Task StillLogoDriftsAndSheensOnlyWhileAllowed() => WithShell((shell, tab, time) =>
    {
        var logo = tab.FindControl<AnimatedLogoImage>("ImgLogo")!;
        using var stream = AssetLoader.Open(new Uri(AnimatedLogoImage.ArtworkUri));
        logo.SetArtwork(new Bitmap(stream));
        var face = tab.FindControl<Border>("LogoFaceLogo")!;
        var scale = (ScaleTransform)face.RenderTransform!;
        var sheen = tab.FindControl<Rectangle>("LogoSheen")!;
        tab.ApplyDashboardDecoration(true, true);
        time.Advance(TimeSpan.FromSeconds(20)); BeatLoop.StepAll();
        Assert.Equal(1.02, scale.ScaleX, 3);
        // Every random gap is an integer in [25,40]; sample both edges of each possible pass.
        bool swept = false;
        for (int i = 0; i < 84; i++)
        {
            time.Advance(TimeSpan.FromSeconds(.25)); BeatLoop.StepAll();
            swept |= sheen.Opacity > .1;
        }
        Assert.True(swept);
        tab.ApplyDashboardDecoration(false, false);
        Assert.Equal(1, scale.ScaleX); Assert.Equal(0, sheen.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == face);
        tab.ApplyDashboardDecoration(true, true);
        shell.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, scale.ScaleX); Assert.Equal(0, sheen.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == face);
        shell.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        logo.SetArtwork(null);
        tab.ApplyDashboardDecoration(true, true);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == face);
    });

    [Fact]
    public Task BrowserUnavailableShowsFailureAndAnyCompletedNavigationShowsConnected() => WithShell((shell, tab, time) =>
    {
        var status = tab.FindControl<TextBlock>("TxtBrowserStatus")!;
        var host = tab.FindControl<WebHost>("BrowserWebHost")!;
        tab.SyncBrowserPrompt();
        Assert.Equal(Loc.Get(host.FailureStatusKey), status.Text);
        Assert.Equal(1, status.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == status);
        status.Text = "Offline";
        var offline = status.Text;
        tab.SyncBrowserPrompt();
        Assert.Equal(offline, status.Text);
        status.Text = Loc.Get("label_loading");
        // WPF writes Connected for every completed navigation, failures included
        // (MainWindow.Browser.cs:139-144, BrowserService.cs:1450-1458); that is what stops the pulse.
        host.OnNavigationCompleted(new Uri("https://example.invalid/"), false);
        Assert.Equal(Loc.Get("label_connected_2"), status.Text);
        Assert.Equal(1, status.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == status);
        Assert.Equal("label_missing_dll", WebHost.FailureKey(new DllNotFoundException()));
        Assert.Equal("label_com_error", WebHost.FailureKey(new System.Runtime.InteropServices.COMException()));
        Assert.Equal("label_not_installed", WebHost.FailureKey(new InvalidOperationException()));
        Assert.Equal("label_error_2", WebHost.FailureKey(new Exception()));
    });

    private static Task WithShell(Action<MainShellWindow, SettingsTabView, SteppedTime> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        CoreSettings.Current.DailyGiftLastRevealDate = "";
        var time = new SteppedTime(); BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            check(shell, shell.FindControl<SettingsTabView>("SettingsTab")!, time);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
