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
using ConditioningControlPanel.Services.Banner;
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                    .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                    .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
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

    // ---------------------------------------------------------------- the interludes

    /// <summary>A scrolling strip with an eligible read (rd_001: streak >= 3) and the cadence on a stepped clock;
    /// the touched settings are put back by <see cref="WithShell"/> and here.</summary>
    private static Task WithReads(Func<MainShellWindow, SettingsTabView, MarqueeInterlude, SteppedTime, Action<TimeSpan>, Task> body) =>
        WithShell(async (w, dash) =>
        {
            var s = CoreSettings.Current;
            var saved = (s.BannerPoolEnabled, s.CurrentStreak, s.BannerSeenIds.ToList());
            try
            {
                var time = new SteppedTime();
                BeatLoop.Time = time;
                (s.MotionLevel, s.PerformanceMode, s.MarqueeMessage, s.BannerPoolEnabled, s.CurrentStreak) =
                    (MotionLevel.Full, false, "Good night", true, 3);
                dash.StartMarqueeAnimation();
                var reads = dash.Marquee.Reads!;
                var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                reads.Cadence = new MarqueeReadCadence(() => now);
                await body(w, dash, reads, time, by => now += by);
            }
            finally { (s.BannerPoolEnabled, s.CurrentStreak, s.BannerSeenIds) = saved; }
        });

    /// <summary>WPF MainWindow.MarqueeReads.cs: the 4th loop (past the launch delay) plays a read; the ticker pauses,
    /// fades out 280 ms, and after the act comes back where it stopped with the loop clock re-armed.</summary>
    [Fact]
    public Task MarqueeReadTakesTheStripAndGivesItBack() => WithReads((w, dash, reads, time, wall) =>
    {
        var strip = dash.Marquee;
        var canvas = dash.FindControl<Canvas>("MarqueeCanvas")!;
        var stage = dash.FindControl<Grid>("MarqueeRead")!;
        Assert.Equal(TimeSpan.FromSeconds(strip.SegmentWidth / MarqueeScroller.PixelsPerSecond), reads.LoopInterval);
        time.Advance(TimeSpan.FromSeconds(0.5));
        BeatLoop.StepAll();

        for (var i = 0; i < 3; i++) reads.OnLoop();
        Assert.False(reads.IsPlaying);                  // three loops: not yet
        wall(TimeSpan.FromMinutes(2));
        reads.OnLoop();
        Assert.True(reads.IsPlaying);                   // the fourth, two minutes after launch
        Assert.Null(reads.LoopInterval);
        Assert.False(strip.IsScrolling);
        Assert.False(string.IsNullOrWhiteSpace(reads.StageText));
        Assert.NotEmpty(stage.GetVisualDescendants().OfType<TextBlock>());

        time.Advance(TimeSpan.FromMilliseconds(140));
        BeatLoop.StepAll();
        Assert.InRange(canvas.Opacity, 0.01, 0.99);      // the ticker is fading out
        time.Advance(TimeSpan.FromMilliseconds(reads.ActMs));
        BeatLoop.StepAll();
        Assert.Equal(0, canvas.Opacity);
        Assert.Equal(1, stage.Opacity);
        time.Advance(TimeSpan.FromSeconds(1));
        BeatLoop.StepAll();

        Assert.False(reads.IsPlaying);
        Assert.Equal(1, canvas.Opacity);
        Assert.Equal(0, stage.Opacity);
        Assert.Empty(stage.Children);
        Assert.True(strip.IsScrolling);
        Assert.Equal(-40, strip.Transform.X, 3);        // resumed in phase, not from zero
        Assert.NotNull(reads.LoopInterval);
        reads.OnLoop();
        Assert.False(reads.IsPlaying);                  // the next one waits four loops and three minutes
        return Task.CompletedTask;
    });

    /// <summary>WPF MarqueeRead_IsVisibleChanged: leaving Home cancels the act and hands the ticker back; the pool
    /// switch (BannerPoolEnabled) keeps the act off the strip.</summary>
    [Fact]
    public Task MarqueeReadStopsOnHideAndHonoursThePoolSwitch() => WithReads((w, dash, reads, time, wall) =>
    {
        var canvas = dash.FindControl<Canvas>("MarqueeCanvas")!;
        wall(TimeSpan.FromMinutes(3));
        CoreSettings.Current.BannerPoolEnabled = false;
        for (var i = 0; i < 4; i++) reads.OnLoop();
        Assert.False(reads.IsPlaying);

        CoreSettings.Current.BannerPoolEnabled = true;
        reads.OnLoop();
        Assert.True(reads.IsPlaying);
        time.Advance(TimeSpan.FromMilliseconds(140));
        BeatLoop.StepAll();
        w.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        Assert.False(reads.IsPlaying);
        Assert.Equal(1, canvas.Opacity);
        Assert.Empty(dash.FindControl<Grid>("MarqueeRead")!.Children);
        // WPF MarqueeRead_IsVisibleChanged: the loop clock stops on hide and restarts on show.
        Assert.Null(reads.LoopInterval);
        w.ShowTab("settings");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(reads.LoopInterval);
        return Task.CompletedTask;
    });

    /// <summary>Four loops past the three minute floor with the act table narrowed to <paramref name="act"/>.</summary>
    private static void PlayAct(MarqueeInterlude reads, Action<TimeSpan> wall, string act)
    {
        reads.Acts = new[] { act };
        wall(TimeSpan.FromMinutes(4));
        for (var i = 0; i < 4 && !reads.IsPlaying; i++) reads.OnLoop();
        Assert.True(reads.IsPlaying);
        Assert.Equal(act, reads.ActId);
    }

    private static void At(SteppedTime time, ref double now, double ms)
    {
        time.Advance(TimeSpan.FromMilliseconds(ms - now));
        now = ms;
        BeatLoop.StepAll();
    }

    /// <summary>The text actually showing: every TextBlock whose own and ancestors' opacity up to the stage is > 0.5.</summary>
    private static string Drawn(Grid stage) =>
        string.Join(" ", stage.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.GetSelfAndVisualAncestors().TakeWhile(v => v != stage).Aggregate(1.0, (o, v) => o * v.Opacity) > 0.5)
            .Select(t => t.Text)).ToUpperInvariant();

    /// <summary>WPF MarqueeReadEffects.cs: each of the five acts takes the stage, draws the whole line by its end,
    /// hands the ticker back, and the return sheen (WPF RestoreTicker -> SweepBannerSheen) crosses once.</summary>
    [Theory]
    [InlineData(MarqueeReadActs.GlitchHeal)]
    [InlineData(MarqueeReadActs.NumberFirst)]
    [InlineData(MarqueeReadActs.Teletype)]
    [InlineData(MarqueeReadActs.NeonWarmUp)]
    [InlineData(MarqueeReadActs.Redaction)]
    public Task EveryActStartsAndEnds(string act) => WithReads((w, dash, reads, time, wall) =>
    {
        var canvas = dash.FindControl<Canvas>("MarqueeCanvas")!;
        var stage = dash.FindControl<Grid>("MarqueeRead")!;
        var sheen = w.Named<Border>("BannerSheen")!;
        PlayAct(reads, wall, act);
        double now = 0, end = MarqueeInterlude.FadeStepMs + reads.ActMs;
        At(time, ref now, MarqueeInterlude.FadeStepMs + 1);
        Assert.NotEmpty(stage.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal(1, stage.Opacity);
        if (act == MarqueeReadActs.NeonWarmUp)
        {
            At(time, ref now, end - 1202);   // the tag has warmed up, its 1.2 s breath not yet begun
            Assert.EndsWith(reads.StageText.Split(' ')[^1].ToUpperInvariant(), Drawn(stage));
        }
        At(time, ref now, end - 1);
        Dispatcher.UIThread.RunJobs();
        var drawn = Drawn(stage);
        foreach (var word in reads.StageText.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(word, drawn);
        if (act == MarqueeReadActs.Redaction)
            Assert.All(stage.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>(), r => Assert.Equal(0, r.Opacity));
        Assert.NotEmpty(stage.GetVisualDescendants().OfType<Image>().Where(i => i.Source != null));   // the baked glow

        At(time, ref now, end + MarqueeInterlude.FadeStepMs + 1);
        Assert.False(reads.IsPlaying);
        Assert.Empty(stage.Children);
        Assert.Equal(1, canvas.Opacity);
        Assert.True(dash.Marquee.IsScrolling);
        Assert.True(w.BannerSheenSweeping);   // over the support banner, as WPF RestoreTicker
        At(time, ref now, now + MainShellWindow.BannerSheenMs * 0.5);
        Assert.Equal(MainShellWindow.BannerSheenPeak, sheen.Opacity, 3);
        At(time, ref now, now + MainShellWindow.BannerSheenMs);
        Assert.False(w.BannerSheenSweeping);
        Assert.Equal(0, sheen.Opacity);
        return Task.CompletedTask;
    });

    /// <summary>WPF MarqueeReadStage.AllowFlicker: NeonWarmUp's words flicker on discrete 1 / 0.15 frames and the
    /// glitch jitters 3 px, but Photosafe swaps the flicker for one ease and drops the jitter.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FlickerAndJitterStopUnderPhotosafe(bool photosafe) => WithReads((w, dash, reads, time, wall) =>
    {
        var s = CoreSettings.Current;
        var saved = s.LockdownPhotosafe;
        try
        {
            s.LockdownPhotosafe = photosafe;
            var stage = dash.FindControl<Grid>("MarqueeRead")!;
            PlayAct(reads, wall, MarqueeReadActs.NeonWarmUp);
            double now = 0;
            // The first word warms over 720 ms (WarmMs * 0.4); 20 % in is mid-flicker, or 0.49 on the ease.
            At(time, ref now, MarqueeInterlude.FadeStepMs + 144);
            var first = stage.GetVisualDescendants().OfType<Grid>().First(g => g.Children.OfType<TextBlock>().Any());
            if (photosafe) Assert.Equal(MarqueeInterlude.Ease(0.2), first.Opacity, 3);
            else Assert.Contains(first.Opacity, new[] { 1, 0.15 });
            reads.Cancel();

            PlayAct(reads, wall, MarqueeReadActs.GlitchHeal);
            now = 0;
            At(time, ref now, MarqueeInterlude.FadeStepMs + 2200 * 0.23);   // the -3 px jitter frame
            Assert.Equal(photosafe ? 0 : -3, ((TranslateTransform)stage.RenderTransform!).X);
            reads.Cancel();
        }
        finally { s.LockdownPhotosafe = saved; }
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
