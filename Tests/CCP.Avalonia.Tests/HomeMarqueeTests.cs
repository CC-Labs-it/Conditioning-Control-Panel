using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>
/// cloud-home-marquee: the Home actions, the browser card, the scrolling marquee and the server banners, through the
/// real shell. The server is a fake feed on the Core client (no network); WPF references are in each test.
/// </summary>
public sealed class HomeMarqueeTests
{
    private sealed class FakeFeed(string marquee, string banner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                r.RequestUri!.AbsolutePath == "/config/marquee" ? marquee : r.RequestUri.AbsolutePath == "/config/update-banner" ? banner : "{}") });
    }

    /// <summary>A shown shell on the sandbox profile, Home in front; the touched settings are put back after.</summary>
    private static Task WithShell(Func<MainShellWindow, SettingsTabView, Task> body, string marquee = "{}", string banner = "{}") =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.MarqueeMessage, s.BrowserVideoMuted, s.ForceShowBambiCloud, s.DiscordRichPresenceEnabled,
                s.HasLinkedDiscord, s.MotionLevel, s.PerformanceMode, s.OfflineMode);
            var (oldClient, oldFree, oldShell, oldTime) =
                (MainShellWindow.AnnouncementClient, CoreEntitlement.IsFreeTodayProvider, ExternalOpener.Shell, BeatLoop.Time);
            MainShellWindow.AnnouncementClient = () => new V2AuthService(() => s, new FakeFeed(marquee, banner));
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                await body(w, w.Named<SettingsTabView>("SettingsTab")!);
            }
            finally
            {
                w.Close();
                MainShellWindow.AnnouncementClient = oldClient;
                CoreEntitlement.IsFreeTodayProvider = oldFree;
                ExternalOpener.Shell = oldShell;
                BeatLoop.Time = oldTime;
                (s.MarqueeMessage, s.BrowserVideoMuted, s.ForceShowBambiCloud, s.DiscordRichPresenceEnabled,
                    s.HasLinkedDiscord, s.MotionLevel, s.PerformanceMode, s.OfflineMode) = saved;
                Dispatcher.UIThread.RunJobs();
            }
        });

    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // ---------------------------------------------------------------- server banners

    /// <summary>WPF MainWindow.Marquee.cs:799-826: a newer build lights the pill urgent; the same build leaves it alone.</summary>
    [Fact]
    public Task UpdateBannerShowsForANewerBuild() => WithShell(async (w, _) =>
    {
        var pill = w.Named<Button>("BtnUpdateAvailable")!;
        await w.CheckServerUpdateBannerAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("UrgentUpdate", pill.Tag);
        Assert.Equal(string.Format(Loc.Get("btn_update_to_version"), "99.0.0"), pill.Content);
        Assert.Equal(string.Format(Loc.Get("tooltip_update_to_version_download"), "99.0.0"), ToolTip.GetTip(pill));
        Assert.Equal("http://127.0.0.1/dl", w.ServerUpdateUrl);
    }, banner: "{\"enabled\":true,\"version\":\"99.0.0\",\"url\":\"http://127.0.0.1/dl\"}");

    [Fact]
    public Task UpdateBannerStaysHiddenForAnOlderBuild() => WithShell(async (w, _) =>
    {
        var pill = w.Named<Button>("BtnUpdateAvailable")!;
        var tag = pill.Tag;
        await w.CheckServerUpdateBannerAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(tag, pill.Tag);
        Assert.Null(w.ServerUpdateUrl);
    }, banner: "{\"enabled\":true,\"version\":\"0.0.0\"}");   // never newer than this build, seeded or not

    /// <summary>WPF MainWindow.AccountShell.cs:482-497: with a banner URL the pill opens it and stops there.</summary>
    [Fact]
    public Task UpdatePillOpensTheBannerUrlFirst() => WithShell(async (w, _) =>
    {
        var opened = new List<string>();
        ExternalOpener.Shell = t => { opened.Add(t); return true; };
        w.ApplyServerUpdateBanner("99.0.0", "http://127.0.0.1/dl");
        Click(w.Named<Button>("BtnUpdateAvailable")!);
        for (var i = 0; i < 50 && opened.Count == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.Equal(new[] { "http://127.0.0.1/dl" }, opened);
    });

    /// <summary>WPF RefreshMarqueeFromSettings (:740): a new server line is saved and the Home strip rebuilds on it.</summary>
    [Fact]
    public Task ServerMarqueeLineReachesTheHomeStrip() => WithShell(async (w, dash) =>
    {
        CoreSettings.Current.MarqueeMessage = "old line";
        await w.RefreshMarqueeFromServerAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Spiral night", CoreSettings.Current.MarqueeMessage);
        Assert.Equal("SPIRAL NIGHT", dash.Marquee.CurrentMessage);
        Assert.StartsWith("SPIRAL NIGHT" + MarqueeScroller.Separator, dash.FindControl<TextBlock>("MarqueeText")!.Text);
    }, marquee: "{\"message\":\"Spiral night\"}");

    // ---------------------------------------------------------------- the marquee strip

    /// <summary>WPF StartMarqueeAnimation (:1139): 80 px/s on the beat, looping one segment; the ambient gate parks it.</summary>
    [Fact]
    public Task MarqueeScrollsOnTheBeatAndParksUnderTheGate() => WithShell((w, dash) =>
    {
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var s = CoreSettings.Current;
        (s.MotionLevel, s.PerformanceMode, s.MarqueeMessage) = (MotionLevel.Full, false, "Good night");
        dash.StartMarqueeAnimation();
        var strip = dash.Marquee;
        Assert.True(strip.IsScrolling);
        time.Advance(TimeSpan.FromSeconds(0.5));
        BeatLoop.StepAll();
        Assert.Equal(-40, strip.Transform.X, 3);
        time.Advance(TimeSpan.FromSeconds(strip.SegmentWidth / MarqueeScroller.PixelsPerSecond));
        BeatLoop.StepAll();
        Assert.Equal(-40, strip.Transform.X, 3);   // one full segment later: the same frame, a seamless loop

        s.MotionLevel = MotionLevel.Off;
        dash.StartMarqueeAnimation();
        Assert.False(strip.IsScrolling);
        Assert.Equal(0, strip.Transform.X);
        Assert.Equal("GOOD NIGHT" + MarqueeScroller.Separator + "GOOD NIGHT" + MarqueeScroller.Separator,
            dash.FindControl<TextBlock>("MarqueeText")!.Text);
        return Task.CompletedTask;
    });

    /// <summary>P01: the shell hides Home with IsVisible, so the strip's clock must stop while another tab is in front.</summary>
    [Fact]
    public Task MarqueeStopsTickingWhileHomeIsHidden() => WithShell((w, dash) =>
    {
        var s = CoreSettings.Current;
        (s.MotionLevel, s.PerformanceMode, s.MarqueeMessage) = (MotionLevel.Full, false, "Good night");
        dash.StartMarqueeAnimation();
        Assert.True(dash.Marquee.IsTicking);
        w.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        Assert.False(dash.Marquee.IsTicking);
        w.ShowTab("settings");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dash.Marquee.IsTicking);
        return Task.CompletedTask;
    });

    /// <summary>WPF MainWindow.xaml:1366: the urgent pill is crimson with a 3 px border, not just new text.</summary>
    [Fact]
    public Task UrgentUpdatePillIsRed() => WithShell((w, _) =>
    {
        var pill = w.Named<Button>("BtnUpdateAvailable")!;
        w.ApplyServerUpdateBanner("99.0.0", "http://127.0.0.1/dl");
        Dispatcher.UIThread.RunJobs();
        var border = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "border");
        Assert.Equal(Color.Parse("#DC143C"), ((ISolidColorBrush)border.Background!).Color);
        Assert.Equal(new Thickness(3), border.BorderThickness);
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------- Home actions

    /// <summary>WPF CardMystery_Click (Presets.cs:1084): the ? box goes to today's free feature's page.</summary>
    [Fact]
    public Task MysteryBoxOpensTodaysFreeFeature() => WithShell((w, dash) =>
    {
        CoreEntitlement.IsFreeTodayProvider = k => k == "voice";
        var box = dash.FindControl<ConditioningControlPanel.Avalonia.Views.Features.FeatureCard>("CardMystery")!;
        box.RaiseEvent(new RoutedEventArgs(ConditioningControlPanel.Avalonia.Views.Features.FeatureCard.ClickEvent, box));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("shelistening", w.CurrentTab);
        Assert.Equal("play", SettingsTabView.MysteryTabFor("dtrh"));
        return Task.CompletedTask;
    });

    /// <summary>WPF CardDeeperEditor_Click (Presets.cs:1121; SettingsTabView.xaml:601-636): the tile that replaced the
    /// retired tease tile is a door to the Deeper page - localized title, no dimming, and a click navigates.</summary>
    [Fact]
    public Task DeeperEditorTileOpensTheDeeperPage() => WithShell((w, dash) =>
    {
        var card = dash.FindControl<ConditioningControlPanel.Avalonia.Views.Features.FeatureCard>("CardDeeperEditor")!;
        Assert.Equal(Loc.Get("dash_deeper_editor_title"), card.Title);
        Assert.False(card.DimWhenInactive);
        Assert.Null(dash.FindControl<Control>("CardJustDrop"));
        card.RaiseEvent(new RoutedEventArgs(ConditioningControlPanel.Avalonia.Views.Features.FeatureCard.ClickEvent, card));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("deeper", w.CurrentTab);
        return Task.CompletedTask;
    });

    /// <summary>WPF IntakePassFace_MouseLeftButtonDown (UiUpdates.cs:1911): the face launches through the intake page's
    /// gates (no login here: its "Login Required" note); its "(?)" belongs to the help popover.</summary>
    [Fact]
    public Task IntakePassFaceLaunchesThroughTheIntakeGates() => WithShell((w, dash) =>
    {
        int before = w.OwnedWindows.Count;
        dash.LaunchIntakeFromFace(dash.FindControl<Button>("BtnIntakePassHelp"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, w.OwnedWindows.Count);
        dash.LaunchIntakeFromFace(dash.FindControl<Border>("IntakePassFace"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, w.OwnedWindows.Count);
        foreach (var d in w.OwnedWindows.ToArray()) d.Close();
        return Task.CompletedTask;
    });

    /// <summary>WPF VelvetBtnWebcam_Click (:1475) -> Settings > Devices; VelvetBtnAppInfo_Click (:1482) -> one App Info popup.</summary>
    [Fact]
    public Task QuickPillsRouteLikeWpf() => WithShell((w, dash) =>
    {
        Click(dash.FindControl<Button>("VelvetBtnWebcam")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("appsettings", w.CurrentTab);
        Assert.Equal("devices", w.Named<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);

        w.ShowTab("settings");
        Click(dash.FindControl<Button>("VelvetBtnAppInfo")!);
        var first = dash.AppInfoPopup;
        Assert.NotNull(first);
        Click(dash.FindControl<Button>("VelvetBtnAppInfo")!);   // a second click replaces the first
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(first, dash.AppInfoPopup);
        Assert.False(first!.IsVisible);
        dash.AppInfoPopup!.Close();
        return Task.CompletedTask;
    });

    /// <summary>WPF ChkDiscordRichPresence_Changed (AccountShell.cs:279): refused without a linked Discord, written with one.</summary>
    [Fact]
    public Task RichPresenceQuickSwitchNeedsALinkedDiscord() => WithShell((w, dash) =>
    {
        var s = CoreSettings.Current;
        var box = dash.FindControl<CheckBox>("ChkQuickDiscordRichPresence")!;
        (s.HasLinkedDiscord, s.DiscordRichPresenceEnabled) = (false, false);
        box.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.IsChecked);
        Assert.False(s.DiscordRichPresenceEnabled);
        foreach (var d in w.OwnedWindows.ToArray()) d.Close();   // the "not linked" note

        s.HasLinkedDiscord = true;
        box.IsChecked = true;
        Assert.True(s.DiscordRichPresenceEnabled);
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------- browser card

    /// <summary>WPF BtnMuteBrowser_Click / SyncBrowserMuteIcon (Browser.cs:2603, :2613): the flag flips, the glyph follows,
    /// and every navigation re-sends it to the page.</summary>
    [Fact]
    public Task BrowserMuteFlipsSavesAndReappliesOnNavigation() => WithShell((w, dash) =>
    {
        var s = CoreSettings.Current;
        s.BrowserVideoMuted = false;
        dash.SyncBrowserCard();
        var glyph = dash.FindControl<IconGlyph>("TxtBrowserMute")!;
        Assert.Equal(IconKind.Speaker2, glyph.Kind);
        Click(dash.FindControl<Button>("BtnMuteBrowser")!);
        Assert.True(s.BrowserVideoMuted);
        Assert.Equal(IconKind.SpeakerMute, glyph.Kind);
        var applies = dash.BrowserMuteApplies;
        dash.FindControl<WebHost>("BrowserWebHost")!.OnNavigationCompleted(new Uri("http://127.0.0.1/next"));
        Assert.Equal(applies + 1, dash.BrowserMuteApplies);
        return Task.CompletedTask;
    });

    /// <summary>WPF ChkForceShowBambiCloud_Changed (DeeperTab.cs:505) saves; BrowserLoadingText_Click (Browser.cs:280) loads
    /// the selected site, never past the offline gate.</summary>
    [Fact]
    public Task BambiCloudOverrideSavesAndThePromptLoadsTheSite() => WithShell((w, dash) =>
    {
        var s = CoreSettings.Current;
        s.ForceShowBambiCloud = false;
        dash.FindControl<CheckBox>("ChkForceShowBambiCloud")!.IsChecked = true;
        Assert.True(s.ForceShowBambiCloud);

        Assert.True(SettingsTabView.BrowserPromptShown(hasEngine: true, loaded: false));
        Assert.False(SettingsTabView.BrowserPromptShown(hasEngine: false, loaded: false));   // the host's own panel says why

        var host = dash.FindControl<WebHost>("BrowserWebHost")!;
        var before = host.NavigationRequests;
        s.OfflineMode = true;
        dash.OnBrowserPromptClick();
        Assert.Equal(before, host.NavigationRequests);
        s.OfflineMode = false;
        dash.OnBrowserPromptClick();
        Assert.Equal(before + 1, host.NavigationRequests);
        Assert.Equal(dash.FindControl<RadioButton>("RbHypnoTube")!.IsChecked == true ? "hypnotube.com" : "bambicloud.com", host.Source!.Host);
        return Task.CompletedTask;
    });
}
