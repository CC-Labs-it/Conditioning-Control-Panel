using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BannerFinishTests
{
    [Fact]
    public Task SupportBeatCarriesAnActionableLinkAndPanicParksItsFx() => InShell(w =>
    {
        var link = w.Named<global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton>("BannerSupportLink")!;
        Assert.Equal("https://linktr.ee/CodeBambi", link.NavigateUri!.AbsoluteUri);
        Assert.True(link.Focus());
        // Sandbox refuses the external site; this keyboard click must never launch a real browser.
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        var stop = System.Linq.Enumerable.Single(PanicSurfaces.All, s => s.Id == "banner-fx");
        stop.Stop(w);
        Assert.False(Get<FrameClock>(w, "_bannerFxClock").IsEnabled);
        Assert.Empty(w.Named<Canvas>("BannerSparkleLayer")!.Children);
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffSwapsWithoutTransitions() => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Call(w, "BannerRotationTimer_Tick", null, EventArgs.Empty);
        var outgoing = w.Named<TextBlock>("TxtBannerPrimary")!;
        var incoming = w.Named<TextBlock>("TxtBannerSecondary")!;
        Assert.Null(outgoing.Transitions);
        Assert.Null(incoming.Transitions);
        Assert.Equal(0, outgoing.Opacity);
        Assert.Equal(1, incoming.Opacity);
        Assert.False(outgoing.IsHitTestVisible);
        Assert.True(incoming.IsHitTestVisible);
        w.StepBannerFx(100);
        Assert.Null(incoming.RenderTransform);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    public Task DrumAndPopFollowMotionAndReturnToRest(MotionLevel level, bool drum) => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = level;
        w.Activate();
        var outgoing = w.Named<TextBlock>("TxtBannerPrimary")!;
        var incoming = w.Named<TextBlock>("TxtBannerSecondary")!;
        Call(w, "OnBannerBeatChanged", outgoing, incoming);
        Set(w, "_bannerBeatAt", 100d);
        w.StepBannerFx(100.1);
        var transforms = Assert.IsType<TransformGroup>(incoming.RenderTransform);
        Assert.Equal(drum, Assert.IsType<TranslateTransform>(transforms.Children[1]).Y > 0);
        Assert.InRange(w.Named<Border>("BannerFlashRing")!.Opacity, .01, 1);
        w.StepBannerFx(101);
        Assert.Null(incoming.RenderTransform);
        Assert.Null(outgoing.RenderTransform);
        Assert.Equal(0, w.Named<Border>("BannerFlashRing")!.Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SheenThrottlesButAnnouncementsForceItAndCloseStopsTheClock() => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        w.Activate();
        w.SweepBannerSheen(true);
        var at = Get<double>(w, "_bannerSheenAt");
        w.SweepBannerSheen();
        Assert.Equal(at, Get<double>(w, "_bannerSheenAt"));
        w.StepBannerFx(at + .375);
        Assert.InRange(w.Named<Border>("BannerSheen")!.Opacity, .219, .221);
        Set(w, "_bannerSheenAt", -10d);
        w.SetBannerAnnouncement("new line");
        Assert.True(Get<double>(w, "_bannerSheenAt") >= 0);
        Assert.Equal("new line", w.Named<TextBlock>("TxtBannerSecondary")!.Text);
        w.Close();
        Assert.False(Get<FrameClock>(w, "_bannerFxClock").IsEnabled);
        Assert.Equal(0, w.Named<Border>("BannerSheen")!.Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SupportSparklesStaggerCleanUpAndRepeatOnlyAtFullMotion() => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        w.Activate();
        var primary = w.Named<TextBlock>("TxtBannerPrimary")!;
        Call(w, "OnBannerBeatChanged", w.Named<TextBlock>("TxtBannerSecondary")!, primary);
        Set(w, "_bannerSparkleAt", 100d);
        var layer = w.Named<Canvas>("BannerSparkleLayer")!;
        Assert.Equal(7, layer.Children.Count);
        w.StepBannerFx(100.1);
        Assert.True(layer.Children[0].Opacity > 0);
        Assert.Equal(0, layer.Children[6].Opacity);
        w.StepBannerFx(102);
        Assert.Empty(layer.Children);
        w.StepBannerFx(113);
        Assert.Empty(layer.Children);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        w.StepBannerFx(114);
        Assert.Equal(7, layer.Children.Count);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Call(w, "EvaluateBannerFx");
        Assert.Empty(layer.Children);
        Assert.Equal(.35, w.Named<Border>("BannerHalo")!.Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BrowserFailurePromptsOnceAndSuccessDoesNotPrompt() => InShell(async w =>
    {
        var shell = ExternalOpener.Shell;
        var prompt = ExternalOpener.BrowserPrompt;
        var fallback = ExternalOpener.BrowserFallback;
        int prompts = 0;
        try
        {
            ExternalOpener.BrowserFallback = (_, _) => false;
            ExternalOpener.BrowserPrompt = (_, url, purpose) =>
            {
                Assert.Equal("http://localhost:12345/", url);
                Assert.Equal("open the CC Labs web app", purpose);
                prompts++;
                return Task.CompletedTask;
            };
            ExternalOpener.Shell = _ => false;
            Assert.False(await ExternalOpener.OpenBrowserOrPromptAsync(w, "http://localhost:12345/", "open the CC Labs web app"));
            Assert.Equal(1, prompts);
            ExternalOpener.Shell = _ => true;
            Assert.True(await ExternalOpener.OpenBrowserOrPromptAsync(w, "http://localhost:12345/", "open the CC Labs web app"));
            Assert.Equal(1, prompts);
        }
        finally { ExternalOpener.Shell = shell; ExternalOpener.BrowserPrompt = prompt; ExternalOpener.BrowserFallback = fallback; }
    });

    private static Task InShell(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        MainShellWindow? w = null;
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            await body(w);
        }
        finally { w?.Close(); (s.MotionLevel, s.PerformanceMode) = old; }
    });
    private static FieldInfo Field(string name) => typeof(MainShellWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static T Get<T>(MainShellWindow w, string name) => (T)Field(name).GetValue(w)!;
    private static void Set(MainShellWindow w, string name, object value) => Field(name).SetValue(w, value);
    private static void Call(MainShellWindow w, string name, params object?[] args) =>
        typeof(MainShellWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
}
