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
        Step(w, .1);
        var incoming = w.Named<TextBlock>("TxtBannerSecondary")!;
        var group = Assert.IsType<global::Avalonia.Media.TransformGroup>(incoming.RenderTransform);
        Assert.InRange(((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y, 1, 13);
        Assert.InRange(w.Named<Border>("BannerFlashRing")!.Opacity, .1, .95);
        Assert.InRange(w.Named<Border>("BannerSheen")!.Opacity, .01, .22);
        Step(w, 1);
        Assert.Equal(0, ((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y);
        Assert.Equal(1, ((global::Avalonia.Media.ScaleTransform)group.Children[0]).ScaleY);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Tick(w);
        Step(w, 1.1);
        var web = w.Named<TextBlock>("TxtBannerWeb")!;
        group = Assert.IsType<global::Avalonia.Media.TransformGroup>(web.RenderTransform);
        Assert.Equal(0, ((global::Avalonia.Media.TranslateTransform)group.Children[1]).Y);
        Step(w, 4);
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
        Step(w, .5);
        Assert.Contains(w.Named<Canvas>("BannerSparkleLayer")!.Children, c => c.Opacity > 0);
        Step(w, 2);
        Assert.Empty(w.Named<Canvas>("BannerSparkleLayer")!.Children);
        Step(w, 13);
        Assert.Equal(7, w.Named<Canvas>("BannerSparkleLayer")!.Children.Count);
        w.Close();
        Assert.False(Clock(w).IsRunning);
        Assert.Empty(w.Named<Canvas>("BannerSparkleLayer")!.Children);
        Assert.Equal(.35, w.Named<Border>("BannerGlow")!.Opacity);
    });

    [Fact]
    public Task WebFailureCopiesTheLinkAndPanicClosesThePrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await InShell(w =>
        {
            var link = w.Named<ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton>("BannerWebLink")!;
            var task = (Task)link.GetType().GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(link, new object[] { link.NavigateUri! })!;
            Dispatcher.UIThread.RunJobs();
            var prompts = (System.Collections.Generic.HashSet<Window>)link.GetType()
                .GetField("Prompts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var prompt = Assert.Single(prompts);
            Assert.Contains("https://app.cclabs.app", prompt.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Contains(PanicSurfaces.All, s => s.Id == "browser-prompt");
            ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton.CloseBrowserPrompts();
            Dispatcher.UIThread.RunJobs();
            Assert.False(prompt.IsVisible);
        });
    });

    private static ConditioningControlPanel.Avalonia.Helpers.BeatLoop Clock(MainShellWindow w) =>
        (ConditioningControlPanel.Avalonia.Helpers.BeatLoop)typeof(MainShellWindow)
            .GetField("_bannerFxClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
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
