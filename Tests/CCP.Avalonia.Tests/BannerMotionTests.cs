using Avalonia.Input.Platform;
using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using SafeLink = ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton;

namespace CCP.Avalonia.Tests;

public sealed class BannerMotionTests
{
    [Fact]
    public Task OpacityTransitionsFollowMotionWithoutOpeningAWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var original = CoreSettings.Current.MotionLevel;
        try
        {
            var beat = new TextBlock();
            var ensure = typeof(MainShellWindow).GetMethod("EnsureFadeTransition", BindingFlags.Static | BindingFlags.NonPublic)!;
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            ensure.Invoke(null, new object[] { beat });
            Assert.Single(beat.Transitions!);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            ensure.Invoke(null, new object[] { beat });
            Assert.Null(beat.Transitions);
        }
        finally { CoreSettings.Current.MotionLevel = original; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffSwapsWithoutAnOpacityTransition() => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Tick(w);
        var secondary = w.Named<TextBlock>("TxtBannerSecondary")!;
        Assert.True(secondary.Transitions is null || secondary.Transitions.Count == 0);
        Assert.Equal(1, secondary.Opacity);
        Assert.True(secondary.IsHitTestVisible);
        Assert.False(w.Named<TextBlock>("TxtBannerPrimary")!.IsHitTestVisible);
    });

    [Fact]
    public Task BannerUsesWpfDimensions() => InShell(w =>
    {
        var host = w.Named<Border>("HeaderBannerHost")!;
        Assert.Equal(31, host.Height);
        Assert.Equal(744, host.MaxWidth);
        Assert.Equal(14, w.Named<TextBlock>("TxtBannerPrimary")!.FontSize);
    });

    [Fact]
    public Task BannerDepthTracksSectionAndKeepsLinksClear() => InShell(w =>
    {
        var shade = w.Named<Border>("BannerDrumShade")!;
        var lip = w.Named<Border>("BannerDrumLip")!;
        w.PaintBannerDepth(global::Avalonia.Media.Colors.Red);
        var first = Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(shade.Background).GradientStops[0].Color;
        w.PaintBannerDepth(global::Avalonia.Media.Colors.Blue);
        Assert.NotEqual(first, Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(shade.Background).GradientStops[0].Color);
        Assert.Equal(4, ((global::Avalonia.Media.LinearGradientBrush)shade.Background!).GradientStops.Count);
        Assert.False(lip.IsHitTestVisible);
        Assert.False(shade.IsHitTestVisible);
        Assert.False(w.Named<Border>("BannerDrumFoot")!.IsHitTestVisible);
        Assert.False(w.Named<Border>("BannerGlass")!.IsHitTestVisible);
        Assert.True(w.Named<Grid>("BannerDrum")!.ClipToBounds);
        foreach (var name in new[] { "TxtBannerPrimary", "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
            Assert.IsType<global::Avalonia.Media.DropShadowEffect>(w.Named<TextBlock>(name)!.Effect);
        Assert.NotNull(w.Named<SafeLink>("BannerSupportLink"));
        Assert.NotNull(w.Named<SafeLink>("BannerWebLink"));
    });

    [Fact]
    public Task SupportLinkUsesTheGuardedOpener() => InShell(w =>
    {
        var link = Assert.IsType<SafeLink>(w.Named<SafeLink>("BannerSupportLink"));
        var shell = ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
        string? target = null;
        try
        {
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = url => { target = url; return true; };
            // The sandbox must still refuse the click before it reaches the shell.
            typeof(SafeLink).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(link, null);
            Assert.Equal("https://linktr.ee/CodeBambi", link.NavigateUri!.AbsoluteUri);
            Assert.Null(target);
        }
        finally { ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = shell; }
    });

    [Theory]
    [InlineData(1661, 1002, 744, false)]
    [InlineData(1131, 700, 440, true)]
    public Task SupportLinkFitsAndEllipsizesInTheRenderedShell(int width, int height, int cap, bool narrow) => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        ((DispatcherTimer)typeof(MainShellWindow).GetField("_bannerRotationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!).Stop();
        w.Width = width; w.Height = height;
        var host = w.Named<Border>("HeaderBannerHost")!;
        host.MaxWidth = cap;
        var primary = w.Named<TextBlock>("TxtBannerPrimary")!;
        primary.Transitions = null; primary.Opacity = 1;
        foreach (var name in new[] { "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
        { var beat = w.Named<TextBlock>(name)!; beat.Transitions = null; beat.Opacity = 0; }
        Dispatcher.UIThread.RunJobs();
        var link = w.Named<SafeLink>("BannerSupportLink")!;
        var text = Assert.IsType<TextBlock>(link.Content);
        var end = text.TranslatePoint(new Point(text.TextLayout.WidthIncludingTrailingWhitespace, 0), primary);
        Assert.True(end.HasValue, "Support link must stay attached to the displayed beat.");
        Assert.InRange(end.GetValueOrDefault().X, 1, primary.Bounds.Width + .5);
        Assert.True(text.Bounds.Width > 0);
        if (narrow) Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Stretch, primary.HorizontalAlignment);
        var dir = Environment.GetEnvironmentVariable("CCP_SUPPORT_PROOF_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
            using var frame = w.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(System.IO.Path.Combine(dir, narrow ? "support-narrow.png" : "support-full.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    });

    [Theory]
    [InlineData("unified")]
    [InlineData("provider")]
    [InlineData("signed-in")]
    [InlineData("stale-id")]
    public Task WelcomeFollowsIdentityChangesAndProviderFallback(string source) => InShell(w =>
    {
        var s = CoreSettings.Current;
        s.OfflineMode = false;
        var property = typeof(ConditioningControlPanel.Avalonia.Platform.AccountSeed).GetProperty("Patreon", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        var account = (CoreAccount.IsLoggedInProvider, CoreAccount.UnifiedUserId);
        using var provider = new ConditioningControlPanel.Services.ProviderSubscription("patreon");
        try
        {
            property.SetValue(null, provider);
            s.UserDisplayName = source == "unified" ? "Bambi" : " ";
            provider.DisplayName = source == "provider" ? "Provider" : null;
            CoreAccount.IsLoggedInProvider = () => !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);
            CoreAccount.UnifiedUserId = source == "stale-id" ? null : "signed-in";
            s.UnifiedId = source == "stale-id" ? "stale-id" : null;
            Assert.Equal(source != "stale-id", CoreAccount.IsLoggedIn);
            w.UpdateQuickLoginUI(accountChanged: true);
            var expected = source == "stale-id"
                ? ConditioningControlPanel.Localization.Loc.Get("label_welcome_consider_logging_in_with_patreon_for")
                : source == "signed-in"
                ? ConditioningControlPanel.Localization.Loc.Get("banner_signed_in_no_name")
                : ConditioningControlPanel.Localization.Loc.GetF("label_welcome_back_0", source == "unified" ? "Bambi" : "Provider");
            Assert.Equal(expected, w.Named<TextBlock>("TxtBannerSecondary")!.Text);
            Assert.Equal(source == "stale-id", w.SettingsPage!.FindControl<Button>("BtnUnifiedLogin")!.IsVisible);
        }
        finally
        {
            property.SetValue(null, previous);
            CoreAccount.UnifiedUserId = account.UnifiedUserId;
            CoreAccount.IsLoggedInProvider = account.IsLoggedInProvider;
        }
    });

    [Fact]
    public Task WebFailureCopiesTheLinkAndPanicClosesThePrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var refused = SafeLink.Refused;
        Task? open = null;
        Task<string?>? clipboard = null;
        try
        {
            // A browser failure, not a sandbox refusal: only then does WPF's BrowserLauncher fallback apply.
            SafeLink.Refused = _ => false;
            await InShell(w =>
            {
                var link = w.Named<SafeLink>("BannerWebLink")!;
                open = OpenLink(link);
                Dispatcher.UIThread.RunJobs();
                var prompt = Assert.Single(Prompts());
                Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("msg_browser_no_default_for", "open the CC Labs web app")
                    + ConditioningControlPanel.Localization.Loc.GetF("msg_browser_link_copied", "https://app.cclabs.app/"),
                    prompt.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Contains(PanicSurfaces.All, s => s.Id == "browser-prompt");
                clipboard = w.Clipboard!.TryGetTextAsync();
                PanicSurfaces.StopAll("banner-test", w);
                Dispatcher.UIThread.RunJobs();
                Assert.False(prompt.IsVisible);
            });
            await open!;
            Assert.Equal("https://app.cclabs.app/", await clipboard!);
            Assert.Empty(Prompts());
        }
        finally { SafeLink.Refused = refused; }
    });

    [Fact]
    public Task ASandboxRefusalSkipsTheClipboardAndPrompt() => InShell(async w =>
    {
        Assert.True(ConditioningControlPanel.Services.SandboxNet.Active);
        await w.Clipboard!.SetTextAsync("unchanged");
        await OpenLink(w.Named<SafeLink>("BannerWebLink")!);
        Assert.Empty(Prompts());
        Assert.Equal("unchanged", await w.Clipboard.TryGetTextAsync());
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task PendingBrowserFailureStopsOnPanicOrOwnerClose(bool delayClipboard, bool closeOwner) => InShell(async w =>
    {
        var refused = SafeLink.Refused;
        var release = new TaskCompletionSource<bool>();
        Task? pending = null;
        try
        {
            SafeLink.Refused = _ => false;
            var link = w.Named<SafeLink>("BannerWebLink")!;
            var copied = 0;
            var shown = 0;
            using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog) shown++;
            });
            link.LaunchLink = (_, _) => delayClipboard ? Task.FromResult(false) : release.Task;
            link.CopyLink = async (_, _) => { copied++; if (delayClipboard) await release.Task; };
            pending = OpenLink(link);
            Assert.False(pending.IsCompleted);
            Assert.Equal(delayClipboard ? 1 : 0, copied);
            if (closeOwner) w.Close();
            else PanicSurfaces.StopAll("pending-browser-test", w);
            release.SetResult(false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pending.IsCompleted);
            await pending;
            Assert.Empty(Prompts());
            Assert.Equal(0, shown);
            Assert.False(link.IsVisited);
            Assert.Equal(delayClipboard ? 1 : 0, copied);
        }
        finally
        {
            release.TrySetResult(false);
            SafeLink.CloseBrowserPrompts();
            try { if (pending is not null) await pending; }
            finally { SafeLink.Refused = refused; }
        }
    });

    private static Task OpenLink(SafeLink link) =>
        (Task)typeof(SafeLink).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, new object[] { link.NavigateUri! })!;
    private static System.Collections.Generic.HashSet<Window> Prompts() =>
        (System.Collections.Generic.HashSet<Window>)typeof(SafeLink)
            .GetField("Prompts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task BeatPopFollowsMotionAndCleansUp(MotionLevel motion) => InShell(w =>
    {
        var previous = ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        var clock = new BannerClock();
        ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = clock;
        try
        {
            CoreSettings.Current.MotionLevel = motion;
            Tick(w);
            var first = w.Named<TextBlock>("TxtBannerPrimary")!;
            var next = w.Named<TextBlock>("TxtBannerSecondary")!;
            Assert.False(first.IsHitTestVisible);
            Assert.True(next.IsHitTestVisible);
            if (motion == MotionLevel.Off) Assert.Null(next.RenderTransform);
            else
            {
                var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(next.RenderTransform);
                Assert.Equal(.96, scale.ScaleX, 3);
                clock.Now += TimeSpan.FromMilliseconds(260).Ticks;
                ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
                Assert.Null(next.RenderTransform);
                Tick(w);
                var web = w.Named<TextBlock>("TxtBannerWeb")!;
                Assert.NotNull(web.RenderTransform);
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
                Assert.Null(web.RenderTransform);
                CoreSettings.Current.MotionLevel = motion;
                Tick(w); // support returns despite the sheen throttle
                Assert.True(w.BannerSheenSweeping);
                Assert.NotNull(first.RenderTransform);
                w.Hide();
                Assert.Null(first.RenderTransform);
                typeof(MainShellWindow).GetMethod("PopBannerBeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object[] { first });
                Assert.Null(first.RenderTransform);
            }
        }
        finally { ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = previous; }
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task SupportSparklesAreFiniteAndParkOnHide(MotionLevel motion) => InShell(w =>
    {
        var previous = ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        var clock = new BannerClock();
        ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = clock;
        try
        {
            CoreSettings.Current.MotionLevel = motion;
            var layer = w.Named<Canvas>("BannerSparkleLayer")!;
            var primary = w.Named<TextBlock>("TxtBannerPrimary")!;
            var pop = typeof(MainShellWindow).GetMethod("PopBannerBeat", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Arrive() => pop.Invoke(w, new object[] { primary });
            Arrive();
            if (motion == MotionLevel.Off) { Assert.Empty(layer.Children); return; }
            Assert.Equal(7, layer.Children.Count);
            var oldSprite = layer.Children[0];
            var oldLoop = typeof(MainShellWindow).GetField("_bannerSparkles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            Arrive(); // superseding a run replaces its sprites
            Assert.Equal(7, layer.Children.Count);
            Assert.DoesNotContain(oldSprite, layer.Children);
            clock.Now += TimeSpan.FromSeconds(2).Ticks;
            oldLoop.GetType().GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(oldLoop, null);
            Assert.Equal(7, layer.Children.Count); // a queued tick from the old run cannot clear the new one
            clock.Now -= TimeSpan.FromSeconds(2).Ticks;
            clock.Now += TimeSpan.FromSeconds(.5).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.Contains(layer.Children, child => child.Opacity > 0);
            clock.Now += TimeSpan.FromSeconds(2).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.Empty(layer.Children);
            Arrive();
            Assert.Equal(7, layer.Children.Count);
            w.Hide();
            Assert.Empty(layer.Children);
        }
        finally { ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = previous; }
    });

    [Fact]
    public Task SupportSparklesShowHaloTwinkleSpinAndVariedFlights() => InShell(w =>
    {
        var previous = ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        var clock = new BannerClock();
        ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = clock;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            typeof(MainShellWindow).GetMethod("PopBannerBeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { w.Named<TextBlock>("TxtBannerPrimary")! });
            var layer = w.Named<Canvas>("BannerSparkleLayer")!;
            var stars = layer.Children.Cast<Grid>().ToArray();
            Assert.Equal(7, stars.Length);
            var halo = Assert.IsType<global::Avalonia.Controls.Shapes.Ellipse>(stars[0].Children[0]);
            var brush = Assert.IsType<global::Avalonia.Media.RadialGradientBrush>(halo.Fill);
            Assert.Equal(200, brush.GradientStops[0].Color.A);
            Assert.Equal(0, brush.GradientStops[2].Color.A);
            Assert.IsType<global::Avalonia.Controls.Shapes.Path>(stars[0].Children[1]);
            double start0 = Canvas.GetLeft(stars[0]) + stars[0].Width / 2;
            clock.Now += TimeSpan.FromSeconds(.09).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            double start1 = Canvas.GetLeft(stars[1]) + stars[1].Width / 2;
            Assert.NotEqual(start0, start1);
            clock.Now += TimeSpan.FromSeconds(.21).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            var group = Assert.IsType<global::Avalonia.Media.TransformGroup>(stars[0].RenderTransform);
            var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(group.Children[0]);
            var rotate = Assert.IsType<global::Avalonia.Media.RotateTransform>(group.Children[1]);
            double before = scale.ScaleX, angle = rotate.Angle, x = Canvas.GetLeft(stars[0]);
            Assert.True(before > 0); Assert.NotEqual(0, angle);
            clock.Now += TimeSpan.FromSeconds(.3).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.NotEqual(before, scale.ScaleX); Assert.NotEqual(angle, rotate.Angle);
            Assert.True(Canvas.GetLeft(stars[0]) > x);
            clock.Now += TimeSpan.FromSeconds(.65).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            double end0 = Canvas.GetLeft(stars[0]) + stars[0].Width / 2;
            clock.Now += TimeSpan.FromSeconds(.09).Ticks;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            double end1 = Canvas.GetLeft(stars[1]) + stars[1].Width / 2;
            Assert.NotEqual(end0, end1);
        }
        finally { ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = previous; }
    });

    [Fact]
    public Task BannerAmbientBreathAndSupportRepeatFollowTheGate() => InShell(w =>
    {
        var previous = ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        var clock = new BannerClock();
        ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = clock;
        var settings = CoreSettings.Current;
        var performance = (settings.PerformanceMode, settings.AutoPerformanceMode);
        try
        {
            settings.PerformanceMode = settings.AutoPerformanceMode = false;
            void Gate(MotionLevel level) { settings.MotionLevel = level; ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged(); }
            void Advance(double seconds) { clock.Now += TimeSpan.FromSeconds(seconds).Ticks; ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll(); }
            var layer = w.Named<Canvas>("BannerSparkleLayer")!;
            var glowHost = w.Named<Border>("HeaderBannerGlowHost")!;
            var glow = Assert.IsType<global::Avalonia.Media.DropShadowEffect>(glowHost.Effect);
            Assert.Equal(w.Named<Border>("HeaderBannerHost")!.Bounds.Width, glowHost.Bounds.Width, 1);
            Gate(MotionLevel.Reduced); Assert.Equal(.35, glow.Opacity, 3);
            Gate(MotionLevel.Full); Assert.Equal(.25, glow.Opacity, 3);
            Advance(3.2); Assert.Equal(.6, glow.Opacity, 3);
            Advance(8.8); Assert.Equal(7, layer.Children.Count);
            Advance(2); Assert.Empty(layer.Children);
            Gate(MotionLevel.Reduced); Assert.Equal(.35, glow.Opacity, 3);
            Advance(24); Assert.Empty(layer.Children); // support remains, Reduced does not repeat
            Gate(MotionLevel.Full);
            Tick(w); // support is no longer on screen
            Advance(12); Assert.Empty(layer.Children);
            Gate(MotionLevel.Off); Advance(12); Assert.Empty(layer.Children);
            Gate(MotionLevel.Full);
            settings.PerformanceMode = true;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(.35, glow.Opacity, 3); Advance(12); Assert.Empty(layer.Children);
            settings.PerformanceMode = false;
            Gate(MotionLevel.Full);
            typeof(MainShellWindow).GetMethod("PopBannerBeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { w.Named<TextBlock>("TxtBannerPrimary")! });
            Assert.Equal(7, layer.Children.Count);
            w.WindowState = WindowState.Minimized; Assert.Equal(.35, glow.Opacity, 3); Assert.Empty(layer.Children);
            w.WindowState = WindowState.Normal;
            w.Activate(); Dispatcher.UIThread.RunJobs();
            typeof(MainShellWindow).GetMethod("PopBannerBeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { w.Named<TextBlock>("TxtBannerPrimary")! });
            Assert.Equal(7, layer.Children.Count);
            typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null);
            Assert.False(w.IsActive); Assert.Equal(.35, glow.Opacity, 3); Assert.Empty(layer.Children);
            w.Hide(); Assert.Equal(.35, glow.Opacity, 3); Assert.Empty(layer.Children);
        }
        finally
        {
            (settings.PerformanceMode, settings.AutoPerformanceMode) = performance;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = previous;
        }
    });

    private sealed class BannerClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }

    internal static Task InShell(Action<MainShellWindow> action) => InShell(w => { action(w); return Task.CompletedTask; });

    private static Task InShell(Func<MainShellWindow, Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var motion = CoreSettings.Current.MotionLevel;
        var seen = CoreSettings.Current.SeenFeatureIntros.ToArray();
        CoreSettings.Current.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); await action(w); }
        finally
        {
            w.Close();
            CoreSettings.Current.MotionLevel = motion;
            CoreSettings.Current.SeenFeatureIntros.Clear();
            CoreSettings.Current.SeenFeatureIntros.AddRange(seen);
        }
    });

    internal static void Tick(MainShellWindow w) => typeof(MainShellWindow)
        .GetMethod("BannerRotationTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(w, new object?[] { null, EventArgs.Empty });
}
