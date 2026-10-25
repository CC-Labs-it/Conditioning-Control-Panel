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
    public Task BannerUsesWpfDimensionsAndTheSupportLinkIsClickable() => InShell(w =>
    {
        var host = w.Named<Border>("HeaderBannerHost")!;
        Assert.Equal(31, host.Height);
        Assert.Equal(744, host.MaxWidth);
        // WPF HeaderBannerGlowHost: the halo is a sibling behind the pill, not inside its padding.
        var glow = w.Named<Border>("BannerGlow")!;
        Assert.Same(host.Parent, glow.Parent);
        Assert.True(((Panel)host.Parent!).Children.IndexOf(glow) < ((Panel)host.Parent!).Children.IndexOf(host));
        Assert.Equal(14, w.Named<TextBlock>("TxtBannerPrimary")!.FontSize);
        Assert.NotNull(w.Named<ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton>("BannerSupportLink"));
    });

    [Fact]
    public Task FullRollsAndReducedPopsThenParksAtRest() => InShell(w =>
    {
        w.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsActive);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Tick(w);
        After(w, .1);
        var incoming = w.Named<TextBlock>("TxtBannerSecondary")!;
        var group = Assert.IsType<global::Avalonia.Media.TransformGroup>(incoming.RenderTransform);
        Assert.InRange(((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y, 1, 13);
        Assert.InRange(w.Named<Border>("BannerFlashRing")!.Opacity, .1, .95);
        Assert.InRange(w.Named<Border>("BannerSheen")!.Opacity, .01, .22);
        After(w, 1);
        Assert.Equal(0, ((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y);
        Assert.Equal(1, ((global::Avalonia.Media.ScaleTransform)group.Children[0]).ScaleY);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Tick(w);
        After(w, .1);
        var web = w.Named<TextBlock>("TxtBannerWeb")!;
        group = Assert.IsType<global::Avalonia.Media.TransformGroup>(web.RenderTransform);
        Assert.Equal(0, ((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y);
        After(w, 4);
        Assert.False(Clock(w).IsRunning);
    });

    [Fact]
    public Task SupportSparklesCleanUpAndCloseStopsTheClock() => InShell(w =>
    {
        w.Activate();
        Dispatcher.UIThread.RunJobs();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        // Return to support, regardless of whether the web beat has been spent by another test.
        for (int i = 0; i < 4; i++)
        {
            Tick(w);
            if (w.Named<TextBlock>("TxtBannerPrimary")!.IsHitTestVisible) break;
        }
        Assert.Equal(7, w.Named<Canvas>("BannerSparkleLayer")!.Children.Count);
        After(w, .5);
        Assert.Contains(w.Named<Canvas>("BannerSparkleLayer")!.Children, c => c.Opacity > 0);
        After(w, 2);
        Assert.Empty(w.Named<Canvas>("BannerSparkleLayer")!.Children);
        After(w, 13);
        Assert.Equal(7, w.Named<Canvas>("BannerSparkleLayer")!.Children.Count);
        w.Close();
        Assert.False(Clock(w).IsRunning);
        Assert.Empty(w.Named<Canvas>("BannerSparkleLayer")!.Children);
        Assert.Equal(.35, w.Named<Border>("BannerGlow")!.Opacity);
    });

    [Fact]
    public Task WebFailureCopiesTheLinkAndPanicClosesThePrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var refused = SafeLink.Refused;
        Task? open = null;
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
                Assert.Contains("https://app.cclabs.app", prompt.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Contains(PanicSurfaces.All, s => s.Id == "browser-prompt");
                SafeLink.CloseBrowserPrompts();
                Dispatcher.UIThread.RunJobs();
                Assert.False(prompt.IsVisible);
            });
            await open!;
            Assert.Empty(Prompts());
        }
        finally { SafeLink.Refused = refused; }
    });

    [Fact]
    public Task ASandboxRefusalSkipsTheClipboardAndPrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Task? open = null;
        await InShell(w =>
        {
            Assert.True(ConditioningControlPanel.Services.SandboxNet.Active);   // tests always run sandboxed
            open = OpenLink(w.Named<SafeLink>("BannerWebLink")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Prompts());
        });
        await open!;
        Assert.Empty(Prompts());
    });

    private static Task OpenLink(SafeLink link) =>
        (Task)typeof(SafeLink).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, new object[] { link.NavigateUri! })!;
    private static System.Collections.Generic.HashSet<Window> Prompts() =>
        (System.Collections.Generic.HashSet<Window>)typeof(SafeLink)
            .GetField("Prompts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static ConditioningControlPanel.Avalonia.Helpers.BeatLoop Clock(MainShellWindow w) =>
        (ConditioningControlPanel.Avalonia.Helpers.BeatLoop)typeof(MainShellWindow)
            .GetField("_bannerFxClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    /// <summary>Steps to <paramref name="dt"/> after the last beat change: the real frame clock may have run the FX
    /// time on before the Tick (CI load), so absolute times would land before the beat and read as rest.</summary>
    private static void After(MainShellWindow w, double dt) => Step(w, (double)typeof(MainShellWindow)
        .GetField("_bannerBeatAt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)! + dt);
    private static void Step(MainShellWindow w, double seconds) => typeof(MainShellWindow)
        .GetMethod("StepBannerFx", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, new object[] { seconds });

    internal static Task InShell(Action<MainShellWindow> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var motion = CoreSettings.Current.MotionLevel;
        var seen = CoreSettings.Current.SeenFeatureIntros.ToArray();
        CoreSettings.Current.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); action(w); }
        finally
        {
            w.Close();
            CoreSettings.Current.MotionLevel = motion;
            CoreSettings.Current.SeenFeatureIntros.Clear();
            CoreSettings.Current.SeenFeatureIntros.AddRange(seen);
        }
        return Task.CompletedTask;
    });

    internal static void Tick(MainShellWindow w) => typeof(MainShellWindow)
        .GetMethod("BannerRotationTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(w, new object?[] { null, EventArgs.Empty });
}
