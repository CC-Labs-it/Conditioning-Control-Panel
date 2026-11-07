using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;
using BindingFlags = System.Reflection.BindingFlags;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BillboardCardHost.Fx.cs + .Chips.cs motion, stepped on the host's animation clock:
/// the push between cards (owner 2026-11-06: "we got no transitions (slide)"), the glint, the words'
/// rise, the button thud, the dot opening, the hover fades, the snooze fold and the motion gates.</summary>
public sealed class BillboardMotionTests
{
    private const double W = 640;

    [Fact]
    public Task FullMotionPushesTheNewCardInFromTheRightOverTheOld() => WithHost(MotionLevel.Full, (host, step) =>
    {
        var old = host.CurrentSlide!;
        double w = old.Bounds.Width;
        Assert.Equal(W, w, 0);
        host.Advance();
        Dispatcher.UIThread.RunJobs();
        var next = host.CurrentSlide!;
        Assert.NotSame(old, next);
        Assert.Equal(2, host.Slides.Count); // the old card stays under the push (it used to vanish at once)
        var (oldZoom, oldMove) = BillboardCardHost.SlideParts(old);
        var (zoom, move) = BillboardCardHost.SlideParts(next);

        // WPF Fx.cs:46-76: 450 ms ease-out cubic; new from 40% of the width at 1.05, old to -18%.
        step(0);
        Assert.Equal(w * DashboardBillboard.PushNewStart, move.X, 3);
        Assert.Equal(DashboardBillboard.PushNewScale, zoom.ScaleX, 5);
        Assert.Equal(DashboardBillboard.PushNewScale, zoom.ScaleY, 5);
        Assert.Equal(0, oldMove.X, 5);
        Assert.Equal(1, host.Glint.Opacity, 5);
        step(225); // half time, cubic out = 0.875
        Assert.Equal(w * 0.40 * 0.125, move.X, 3);
        Assert.Equal(1.05 - 0.05 * 0.875, zoom.ScaleX, 5);
        Assert.Equal(-w * 0.18 * 0.875, oldMove.X, 3);
        Assert.Equal(1, oldZoom.ScaleX, 5);
        Assert.Equal(0.5, host.Glint.Opacity, 3);
        step(450);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, move.X, 5);
        Assert.Equal(1, zoom.ScaleX, 5);
        Assert.Equal(0, host.Glint.Opacity, 5);
        Assert.Same(next, Assert.Single(host.Slides)); // the old card leaves when its slide ends
    });

    [Fact]
    public Task WordsRiseInStaggerAndTheButtonThudsAfterThem() => WithHost(MotionLevel.Full, (host, step) =>
    {
        host.Advance();
        Dispatcher.UIThread.RunJobs();
        var title = host.TitleText;
        var rise = (TranslateTransform)title.RenderTransform!;
        var thud = (TransformGroup)host.Cta.RenderTransform!;
        var thudScale = (ScaleTransform)thud.Children[0];
        step(0);
        // Eyebrow at 60 ms, title at 120 ms (WPF TextStaggerMs); both start 14 px low and clear.
        step(100);
        Assert.Equal(0, title.Opacity, 5);
        Assert.Equal(DashboardBillboard.TextRisePx, rise.Y, 5);
        Assert.Equal(0, host.Cta.Opacity, 5);
        Assert.Equal(0.6, thudScale.ScaleX, 5);
        step(120 + 210); // half of the 420 ms rise, on the thud curve
        double k = BillboardTiming.Thud(0.5);
        Assert.Equal(k, title.Opacity, 3);
        Assert.Equal(DashboardBillboard.TextRisePx * (1 - k), rise.Y, 3);
        step(280 + 170); // half of the 340 ms thud, 280 ms in
        Assert.Equal(0.6 + 0.4 * k, thudScale.ScaleX, 3);
        step(1000);
        Assert.Equal(1, title.Opacity, 5);
        Assert.Equal(0, rise.Y, 5);
        Assert.Equal(1, host.Cta.Opacity, 5);
        Assert.Equal(1, thudScale.ScaleX, 5);
    });

    [Fact]
    public Task TheCurrentDotOpensIntoItsCapsule() => WithHost(MotionLevel.Full, (host, step) =>
    {
        host.Advance();
        Dispatcher.UIThread.RunJobs();
        var face = Face((Button)host.ChipButtons[1]);
        step(0);
        Assert.Equal(BillboardCardHost.DotPx, face.Width, 5);
        step(130);
        Assert.Equal(8 + 18 * BillboardTiming.Thud(0.5), face.Width, 3);
        step(260);
        Assert.Equal(BillboardCardHost.DotCurrentPx, face.Width, 5);
        Assert.Equal(BillboardCardHost.DotPx, Face((Button)host.ChipButtons[0]).Width, 5);
    });

    [Fact]
    public Task ReducedMotionHalvesThePush() => WithHost(MotionLevel.Reduced, (host, step) =>
    {
        var old = host.CurrentSlide!;
        host.Advance();
        Dispatcher.UIThread.RunJobs();
        var (_, move) = BillboardCardHost.SlideParts(host.CurrentSlide!);
        step(0);
        Assert.Equal(old.Bounds.Width * 0.40, move.X, 3);
        step(90); // 40% of 225 ms: (1 - 0.4)^3 of the way left
        Assert.Equal(old.Bounds.Width * 0.40 * 0.216, move.X, 3);
        step(225);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, move.X, 5);
        Assert.Single(host.Slides);
    });

    [Fact]
    public Task MotionOffChangesCardsStill() => WithHost(MotionLevel.Off, (host, step) =>
    {
        host.Advance();
        Dispatcher.UIThread.RunJobs();
        step(0);
        var slide = Assert.Single(host.Slides);
        var (zoom, move) = BillboardCardHost.SlideParts(slide);
        Assert.Equal((0d, 1d), (move.X, zoom.ScaleX));
        Assert.Equal(1, host.TitleText.Opacity);
        Assert.Equal(1, host.Cta.Opacity);
        Assert.Equal(0, host.Glint.Opacity);
        Assert.Equal(BillboardCardHost.DotCurrentPx, Face((Button)host.ChipButtons[1]).Width);
        // Hover: the snooze x shows at once; the pill never does at Motion Off (WPF :285).
        Enter(host, true);
        Assert.Equal(1, host.SnoozeButton.Opacity);
        Assert.Equal(0, host.PausedPill.Opacity);
    });

    [Fact]
    public Task HoverFadesThePausedPillAndTheSnoozeInTwoHundredMs() => WithHost(MotionLevel.Full, (host, step) =>
    {
        step(0);
        Enter(host, true);
        step(1);
        step(101);
        Assert.Equal(0.5, host.PausedPill.Opacity, 3);
        Assert.Equal(0.5, host.SnoozeButton.Opacity, 3);
        Assert.False(host.HoldRunning); // hover holds the deck
        step(201);
        Assert.Equal(1, host.PausedPill.Opacity, 5);
        Enter(host, false);
        step(300);
        step(400);
        Assert.Equal(0.5, host.PausedPill.Opacity, 3);
        step(500);
        Assert.Equal(0, host.SnoozeButton.Opacity, 5);
    });

    [Fact]
    public Task SnoozeFoldsTheWordsThenTheNextCardLandsWithTheToast() => WithHost(MotionLevel.Full, (host, step) =>
    {
        step(0);
        host.SnoozeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        step(1);
        step(101); // half of the 200 ms fade, quadratic in
        Assert.Equal(0.75, host.Meta.Opacity, 3);
        step(1 + 125); // half of the 250 ms drop
        Assert.Equal(10 * 0.25, ((TranslateTransform)host.Meta.RenderTransform!).Y, 3);
        // The dot pops off the row in the same 320 ms (scale and fade, quadratic in).
        var popped = (Button)host.ChipButtons[0];
        Assert.Equal(1 - Math.Pow(125.0 / DashboardBillboard.SnoozeFoldMs, 2), popped.Opacity, 3); // 125 of 320 ms
        // The next card lands SnoozeFoldMs later (a dispatcher timer, as in WPF).
        var land = Assert.Single(Due);
        Assert.Equal(TimeSpan.FromMilliseconds(DashboardBillboard.SnoozeFoldMs), land.After);
        Assert.Equal("a", host.CurrentCard!.Spec.Id);
        Due.Clear();
        land.Run();
        Assert.Equal("b", host.CurrentCard!.Spec.Id);
        Assert.Equal(1, host.Meta.Opacity, 5);
        Assert.Equal(0, ((TranslateTransform)host.Meta.RenderTransform!).Y, 5);
        Assert.Equal(string.Format(Loc.Get("billboard_deck_snoozed"), "Card A"), host.ToastText);
        // The toast rises 8 px and fades in over 250 ms, then fades out 1.8 s later in 250 ms.
        step(2000);
        step(2125);
        Assert.Equal(0.5, host.ToastPill.Opacity, 3);
        Assert.Equal(4, ((TranslateTransform)host.ToastPill.RenderTransform!).Y, 3);
        step(2250);
        Assert.Equal(1, host.ToastPill.Opacity, 5);
        var hide = Assert.Single(Due, d => d.After == TimeSpan.FromMilliseconds(1800));
        Assert.Contains(Due, d => d.After == TimeSpan.FromMilliseconds(DashboardBillboard.MotesDelayMs));
        hide.Run();
        step(2300);
        step(2425);
        Assert.Equal(0.5, host.ToastPill.Opacity, 3);
    });

    /// <summary>Owner decision 1b (fix3 pop): a press is the user's own action, so the CPU path's lighter tier keeps
    /// its sparks; the user's own Performance mode drops them, as WPF.</summary>
    [Theory]
    [InlineData(false, 26)]
    [InlineData(true, 0)]
    public Task PressSparksOnTheCpuPath(bool userPerformance, int sparks) => WithHost(MotionLevel.Full, (host, step) =>
    {
        try
        {
            CpuTier.On(userPerformance);
            host.Cta.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(sparks, host.SparkCount);
        }
        finally { CpuTier.Off(); }
    });

    [Fact]
    public Task PressThrowsSparksThenActsAfterTheDelay() => WithHost(MotionLevel.Full, (host, step) =>
    {
        var asked = new List<string>();
        host.ActionRequested += c => asked.Add(c.Spec.Id);
        host.Cta.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(asked); // WPF Press: the sparks first, the action PressActionDelayMs later
        Assert.Equal(26, host.SparkCount);
        var act = Assert.Single(Due);
        Assert.Equal(TimeSpan.FromMilliseconds(DashboardBillboard.PressActionDelayMs), act.After);
        act.Run();
        Assert.Equal(new[] { "a" }, asked);
        // Particles need ambient motion: Reduced presses without sparks, still after the delay.
        Due.Clear();
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        int before = host.SparkCount;
        host.Cta.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(before, host.SparkCount);
        Assert.Single(Due).Run();
        Assert.Equal(new[] { "a", "a" }, asked);
    });

    // ---- harness -----------------------------------------------------------------------------

    /// <summary>The host's delayed steps (WPF After timers), in the order they were asked for.</summary>
    private static readonly List<(TimeSpan After, Action Run)> Due = new();

    private static Border Face(Button chip) =>
        (Border)((Panel)((Panel)chip.Content!).Children[0]).Children[1];

    private static void Enter(BillboardCardHost host, bool on) =>
        host.Stage.RaiseEvent(new PointerEventArgs(on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, host.Stage,
            new Pointer(7, PointerType.Mouse, true), host.Stage, default, 0, default, default));

    private static Task WithHost(MotionLevel level, Action<BillboardCardHost, Action<double>> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = level;
        CoreSettings.Current.PerformanceMode = CoreSettings.Current.AutoPerformanceMode = false;
        var deck = new BillboardDeck(() => new[] { new Cards() },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now), new Dictionary<string, DateTime>(), () => { });
        var host = new BillboardCardHost(deck);
        var window = new Window { Width = W, Height = 320, Content = host };
        var oldSchedule = BillboardCardHost.Schedule;
        Due.Clear();
        BillboardCardHost.Schedule = (after, run) => Due.Add((after, run));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            host.Begin();
            Dispatcher.UIThread.RunJobs();
            // Same stepped ClockBase pattern as LockdownTests.EmergencyExitPress...: the host's
            // clock is inherited by every slide, word, dot and pill under it.
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
            var clockType = typeof(global::Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
            var clock = Activator.CreateInstance(clockType, nonPublic: true)!;
            var pulse = clockType.GetMethod("Pulse", any | BindingFlags.Instance)!;
            var clockProperty = (AvaloniaProperty)typeof(global::Avalonia.Animation.Animatable)
                .GetField("ClockProperty", any | BindingFlags.Static)!.GetValue(null)!;
            host.SetValue(clockProperty, clock);
            void Step(double ms) => pulse.Invoke(clock, new object[] { TimeSpan.FromMilliseconds(10_000 + ms) });
            Step(0);
            check(host, Step);
        }
        finally
        {
            try { window.Close(); host.ReleaseArt(); }
            finally { CoreSettings.ServiceProvider = old; BillboardCardHost.Schedule = oldSchedule; Due.Clear(); }
        }
        return Task.CompletedTask;
    });

    private sealed class Cards : IBillboardProvider
    {
        public string Id => "test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext c)
        {
            var card = new BillboardCardSpec("a", BillboardCardKind.House, 0, "eyebrow", "Card A", "line", "#ff4fa8", "", null,
                new BillboardAction(BillboardActionKind.Tab, "play", "Open"));
            return new[] { card, card with { Id = "b", Title = "Card B", Priority = 1 } };
        }
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
