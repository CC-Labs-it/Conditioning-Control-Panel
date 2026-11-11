using Avalonia.Input.Platform;
using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            Assert.Null(w.Named<TextBlock>(name)!.Effect);
        Assert.NotNull(w.Named<Image>("BannerSecondaryGlow")!.Source);
        Assert.NotNull(w.Named<SafeLink>("BannerSupportLink"));
        Assert.NotNull(w.Named<SafeLink>("BannerWebLink"));
    });

    [Fact]
    public Task BannerSheenStaysInsideCurvedEnds() => InShell(w =>
    {
        w.UpdateLayout();
        var host = w.Named<Border>("HeaderBannerHost")!;
        var sheen = w.Named<Grid>("BannerSheenHost")!;
        Assert.Equal(new Thickness(14, 0), sheen.Margin);
        Assert.True(sheen.ClipToBounds);
        var start = sheen.TranslatePoint(new Point(0, 0), host)!.Value;
        var end = sheen.TranslatePoint(new Point(sheen.Bounds.Width, 0), host)!.Value;
        Assert.Equal(15, start.X, 1);
        Assert.Equal(host.Bounds.Width - 15, end.X, 1);
    });

    [Fact]
    public Task PlainBannerGlowsBakeOnTextAndAccentChangeAndFollowOpacity() => InShell(w =>
    {
        foreach (var (textName, imageName) in new[] { ("TxtBannerSecondary", "BannerSecondaryGlow"), ("TxtBannerPool", "BannerPoolGlow") })
        {
            var text = w.Named<TextBlock>(textName)!;
            var image = w.Named<Image>(imageName)!;
            text.Transitions = null;
            text.Text = "A short line";
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var first = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
            Assert.False(image.IsHitTestVisible);
            var firstWidth = first.PixelSize.Width;
            text.Text = "A longer banner line";
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var second = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
            Assert.NotSame(first, second);
            Assert.True(second.PixelSize.Width > firstWidth);
            using var beforeAccent = new System.IO.MemoryStream();
            second.Save(beforeAccent);
            w.Resources["PinkBrush"] = new global::Avalonia.Media.SolidColorBrush(
                textName == "TxtBannerSecondary" ? global::Avalonia.Media.Colors.Blue : global::Avalonia.Media.Colors.Red);
            Dispatcher.UIThread.RunJobs();
            var recolored = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
            Assert.NotSame(second, recolored);
            using var afterAccent = new System.IO.MemoryStream();
            recolored.Save(afterAccent);
            Assert.False(beforeAccent.ToArray().SequenceEqual(afterAccent.ToArray()));
            text.Opacity = 0.37;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.37, image.Opacity, 2);
            text.Text = "";
            Assert.Null(image.Source);
        }
    });

    [Fact]
    public Task PlainBannerGlowRendersTrimmedTextAtEachWidth() => InShell(w =>
    {
        var host = w.Named<Border>("HeaderBannerHost")!;
        foreach (var (textName, imageName) in new[] { ("TxtBannerSecondary", "BannerSecondaryGlow"), ("TxtBannerPool", "BannerPoolGlow") })
        {
            var text = w.Named<TextBlock>(textName)!;
            var image = w.Named<Image>(imageName)!;
            text.Text = string.Concat(Enumerable.Repeat("A long welcome line with more words ", 8));
            int priorWidth = 0;
            foreach (var width in new[] { 180d, 300d })
            {
                host.MaxWidth = width;
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
                var actual = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
                Assert.InRange(actual.PixelSize.Width, text.Bounds.Width + 15, text.Bounds.Width + 17);
                Assert.True(actual.PixelSize.Width > priorWidth);
                priorWidth = actual.PixelSize.Width;

                var copy = new TextBlock
                {
                    Text = text.Text, Width = text.Bounds.Width, FontSize = text.FontSize,
                    FontFamily = text.FontFamily, FontWeight = text.FontWeight, FontStyle = text.FontStyle,
                    FontFeatures = text.FontFeatures, Foreground = text.Foreground,
                    TextAlignment = text.TextAlignment, TextTrimming = text.TextTrimming, TextWrapping = text.TextWrapping,
                    Effect = new global::Avalonia.Media.DropShadowEffect
                    {
                        Color = Assert.IsType<global::Avalonia.Media.Color>(image.Tag), BlurRadius = 8, Opacity = 0.6, OffsetX = 0, OffsetY = 0
                    }
                };
                var reference = new Border { Padding = new Thickness(8), Child = copy };
                reference.Measure(Size.Infinity);
                reference.Arrange(new Rect(reference.DesiredSize));
                Assert.Contains(copy.TextLayout.TextLines, line => line.HasCollapsed);
                using var frame = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(
                    (int)Math.Ceiling(reference.DesiredSize.Width), (int)Math.Ceiling(reference.DesiredSize.Height)));
                frame.Render(reference);
                using var expectedPng = new System.IO.MemoryStream();
                using var actualPng = new System.IO.MemoryStream();
                frame.Save(expectedPng);
                actual.Save(actualPng);
                Assert.Equal(expectedPng.ToArray(), actualPng.ToArray());
            }
        }
    });

    [Fact]
    public Task PlainBannerGlowUsesPinkColorWhenForegroundDiffers() => InShell(w =>
    {
        w.Resources["PinkBrush"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Blue);
        w.Resources["PinkColor"] = global::Avalonia.Media.Colors.Red;
        Dispatcher.UIThread.RunJobs();
        foreach (var (textName, imageName) in new[] { ("TxtBannerSecondary", "BannerSecondaryGlow"), ("TxtBannerPool", "BannerPoolGlow") })
        {
            var text = w.Named<TextBlock>(textName)!;
            var image = w.Named<Image>(imageName)!;
            text.Text = "A test of the banner glow";
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            w.Resources["PinkColor"] = global::Avalonia.Media.Colors.Red;
            Dispatcher.UIThread.RunJobs();
            var before = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
            using var red = new System.IO.MemoryStream();
            before.Save(red);
            w.Resources["PinkColor"] = global::Avalonia.Media.Colors.Lime;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(global::Avalonia.Media.Colors.Blue, Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(global::Avalonia.Media.Colors.Lime, Assert.IsType<global::Avalonia.Media.Color>(image.Tag));
            var after = Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(image.Source);
            Assert.NotSame(before, after);
            using var green = new System.IO.MemoryStream();
            after.Save(green);
            Assert.False(red.ToArray().SequenceEqual(green.ToArray()));
        }
    });

    [Fact]
    public Task BannerLinksReceiveHitTestsThroughDecorations() => InShell(w =>
    {
        foreach (var (beatName, linkName) in new[] { ("TxtBannerPrimary", "BannerSupportLink"), ("TxtBannerWeb", "BannerWebLink") })
        {
            foreach (var name in new[] { "TxtBannerPrimary", "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
            {
                var beat = w.Named<TextBlock>(name)!;
                beat.Transitions = null;
                beat.Opacity = name == beatName ? 1 : 0;
                beat.IsHitTestVisible = name == beatName;
            }
            w.UpdateLayout();
            RenderBannerTree(w);
            var link = w.Named<SafeLink>(linkName)!;
            Assert.True(link.Bounds.Width > 2 && link.Bounds.Height > 2);
            var point = link.TranslatePoint(new Point(link.Bounds.Width / 2, 2), w)!.Value;
            var hit = w.InputHitTest(point) as Visual;
            Assert.True(hit == link || (hit != null && link.IsVisualAncestorOf(hit)), $"{linkName}: hit {hit}");
        }
    });

    private static void RenderBannerTree(MainShellWindow w)
    {
        w.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        var batch = global::Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(w)!
            .Compositor.RequestCompositionBatchCommitAsync();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the live tree");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task BannerDepthFollowsSectionNavigation() => InShell(w =>
    {
        var lastTabs = CoreSettings.Current.NavLastTabBySection;
        try
        {
            var shade = w.Named<Border>("BannerDrumShade")!;
            var lip = w.Named<Border>("BannerDrumLip")!;

            (SkiaSharp.SKColor Shade, SkiaSharp.SKColor Lip) Navigate(string tab, string section)
            {
                w.ShowTab(tab);
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Assert.Equal(tab, w.CurrentTab);
                var hue = ConditioningControlPanel.Services.UI.NavStripPaint.Accent(section);
                var top = global::Avalonia.Media.Color.FromUInt32(ConditioningControlPanel.Services.UI.DepthLaw.ShadowColor(hue, ConditioningControlPanel.Services.UI.DepthLaw.WellTopAlpha));
                var edge = global::Avalonia.Media.Color.FromUInt32(ConditioningControlPanel.Services.UI.DepthLaw.ShadowColor(hue, ConditioningControlPanel.Services.UI.DepthLaw.WellLeftAlpha));
                Assert.Equal(top, Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(shade.Background).GradientStops[0].Color);
                Assert.Equal(edge, Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(lip.Background).GradientStops[0].Color);
                return (Pixel(shade, true), Pixel(lip, false));
            }

            var home = Navigate("settings", ConditioningControlPanel.Services.UI.NavSections.Home);
            var studio = Navigate("presets", ConditioningControlPanel.Services.UI.NavSections.Studio);
            Assert.NotEqual(home.Shade, studio.Shade);
            Assert.NotEqual(home.Lip, studio.Lip);
        }
        finally { CoreSettings.Current.NavLastTabBySection = lastTabs; }
    });

    private static SkiaSharp.SKColor Pixel(Border layer, bool top)
    {
        var width = (int)Math.Floor(layer.Bounds.Width);
        var height = (int)Math.Floor(layer.Bounds.Height);
        Assert.True(width > 10 && height > 4);
        using var frame = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        frame.Render(layer);
        using var png = new System.IO.MemoryStream();
        frame.Save(png);
        using var pixels = SkiaSharp.SKBitmap.Decode(png.ToArray());
        return pixels.GetPixel(top ? width / 2 : 2, top ? 2 : height / 2);
    }

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
