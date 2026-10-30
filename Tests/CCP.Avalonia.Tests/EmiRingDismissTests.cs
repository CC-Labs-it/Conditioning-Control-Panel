using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRingDismissTests
{
    private sealed class Listener : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task DesktopPressKeepsCardsAndBodyHotButFoldsOutsideOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Action<PixelPoint?, bool>? press = null;
        var listener = new Listener();
        var body = new PixelRect(590, 290, 220, 420);
        var win = new EmiRingWindow(body, new PixelPoint(700, 500))
        {
            StartDismissListener = callback => { press = callback; return listener; },
            ReadPointer = () => throw new InvalidOperationException("Event backend must not poll"),
        };
        int closes = 0;
        win.RingClosed += (_, picked) => { Assert.False(picked); closes++; };
        try
        {
            win.OpenRing();
            Assert.NotNull(press);
            press!(new PixelPoint(700, 500), false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(win.IsOpen);
            var card = win.FindControl<Canvas>("Field")!.Children.OfType<Border>().First();
            var point = win.Position + PixelPoint.FromPoint(new Point(Canvas.GetLeft(card) + 20, Canvas.GetTop(card) + 20), win.RenderScaling);
            press(point, false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(win.IsOpen);
            press(new PixelPoint(-5000, -5000), false);
            Assert.True(win.IsOpen); // native callback posts before touching UI
            Dispatcher.UIThread.RunJobs();
            Assert.False(win.IsOpen);
            Assert.Equal(1, closes);
            Assert.Equal(1, listener.Disposals);
            win.CloseRing();
            Assert.Equal(1, closes);
        }
        finally { win.Kill(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OldEscapeCannotFoldReopenAndDirectCloseUnsubscribes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Action<PixelPoint?, bool>? press = null;
        var listener = new Listener();
        var win = new EmiRingWindow(new PixelRect(590, 290, 220, 420), new PixelPoint(700, 500))
        { StartDismissListener = callback => { press = callback; return listener; } };
        try
        {
            win.OpenRing();
            Assert.NotNull(press);
            press!(null, true);
            win.CloseRing();
            win.OpenRing();
            Dispatcher.UIThread.RunJobs();
            Assert.True(win.IsOpen);
            press!(null, true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(win.IsOpen);
            Assert.Equal(2, listener.Disposals);
            win.OpenRing();
            win.Close();
            Assert.False(win.IsOpen);
            Assert.Equal(3, listener.Disposals);
        }
        finally { win.Kill(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PollIgnoresOpeningPressAndNullThenClosesOnNewEscape() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        (PixelPoint, bool, bool)? now = null;
        PixelRect[]? inputs = null;
        var win = new EmiRingWindow(new PixelRect(590, 290, 220, 420), new PixelPoint(700, 500))
        {
            StartDismissListener = _ => null,
            ReadPointer = () => now,
            ApplyInputRects = rects => inputs = rects,
        };
        try
        {
            win.OpenRing();
            Assert.Equal(6, inputs!.Length);
            Assert.All(inputs, r => { Assert.Equal(136, r.Width); Assert.Equal(102, r.Height); });
            Assert.DoesNotContain(inputs, r => r.Contains(new PixelPoint(700, 500) - win.Position));
            now = (new PixelPoint(-5000, -5000), true, false);
            win.PollClickAway();
            Assert.True(win.IsOpen);
            now = null; win.PollClickAway();
            Assert.True(win.IsOpen);
            now = (new PixelPoint(-5000, -5000), false, false); win.PollClickAway();
            now = (new PixelPoint(-5000, -5000), false, true); win.PollClickAway();
            Assert.False(win.IsOpen);
        }
        finally { win.Kill(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void FanUsesWpfSmallSettleWithoutChangingFade()
    {
        var ease = Assert.IsType<global::ConditioningControlPanel.Avalonia.Theme.BackEaseOutAmplitude>(EmiRingWindow.PopMoveEase);
        Assert.Equal(0.30, ease.Amplitude);
        Assert.Equal(0, ease.Ease(0));
        Assert.Equal(1, ease.Ease(1));
        Assert.InRange(ease.Ease(0.8), 1.02, 1.03);
    }

    [Fact]
    public void ExistingDoorsComposeAndAccountAvailabilityIsRechecked()
    {
        var state = global::ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        var pins = state.Pins.ToArray();
        bool shopOpen = global::ConditioningControlPanel.Services.JustDrop.JustDropService.DoorAvailable;
        try
        {
            state.Pins.Clear();
            state.Pins.AddRange(new[] { "dtrh", "intake", "justdrop" });
            global::ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(false);
            var cards = global::ConditioningControlPanel.Services.EmiDesk.EmiSuggester.Compose(EmiTargets.All);
            Assert.Equal(new[] { "dtrh", "intake" }, cards.Take(2).Select(c => c.Target.Id));
            Assert.All(cards.Take(2), c => Assert.True(c.Pinned));
            Assert.DoesNotContain(cards, c => c.Target.Id == "justdrop");

            // Same catalogue instance: a server answer can reveal the shop mid-session.
            global::ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(true);
            cards = global::ConditioningControlPanel.Services.EmiDesk.EmiSuggester.Compose(EmiTargets.All);
            Assert.Equal("justdrop", cards[2].Target.Id);
            Assert.True(cards[2].Pinned);
            Assert.False(cards[2].Locked);
        }
        finally
        {
            state.Pins.Clear(); state.Pins.AddRange(pins);
            global::ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(shopOpen);
        }
    }

    // WPF Services/EmiDesk/EmiTargets.cs:295-296: both locked doors ask for Lab.
    [Theory]
    [InlineData("dtrh")]
    [InlineData("intake")]
    public void LockedDoorAsksForLabWithoutScoring(string id)
    {
        var state = global::ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        var usage = state.Usage.ToArray();
        var scores = state.OpenScore.ToArray();
        var usageAt = state.UsageAt.ToArray();
        var premium = global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        var lab = global::ConditioningControlPanel.CoreEntitlement.HasLabProvider;
        var daily = global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider;
        var denied = global::ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler;
        global::ConditioningControlPanel.Services.TierVerdict? refusal = null;
        try
        {
            global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => false;
            global::ConditioningControlPanel.CoreEntitlement.HasLabProvider = () => false;
            global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider = _ => false;
            global::ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler = v => refusal = v;
            // A previously used door must not gain usage when its locked card is tapped.
            state.Usage[id] = 7;
            state.OpenScore[id] = 4;
            state.UsageAt[id] = DateTime.UtcNow.AddDays(-1).Ticks;
            // ScoreOf decays with wall time; refusal must preserve the stored usage exactly.
            var before = (state.OpenScore[id], state.UsageAt[id], state.Usage[id]);
            EmiTargets.Find(id)!.Open();
            Assert.NotNull(refusal);
            Assert.Equal(global::ConditioningControlPanel.Models.PatreonTier.Level2, refusal!.Value.Required);
            Assert.Equal(before, (state.OpenScore[id], state.UsageAt[id], state.Usage[id]));
        }
        finally
        {
            state.Usage.Clear(); foreach (var pair in usage) state.Usage[pair.Key] = pair.Value;
            state.OpenScore.Clear(); foreach (var pair in scores) state.OpenScore[pair.Key] = pair.Value;
            state.UsageAt.Clear(); foreach (var pair in usageAt) state.UsageAt[pair.Key] = pair.Value;
            global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = premium;
            global::ConditioningControlPanel.CoreEntitlement.HasLabProvider = lab;
            global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider = daily;
            global::ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler = denied;
        }
    }

    [Fact]
    public Task PanicClosesTheLiveDesksFan() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = EmiDeskService.Instance.BeginPresentation()!;
        try
        {
            desk.Show();
            desk.ToggleRing();
            Assert.True(desk.RingOpen);
            global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "emi-ring").Stop(null);
            Assert.False(desk.RingOpen);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ButtonRoutingCrossesCardBoundaryBeforeDelivery(bool cardFirst) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Action<PixelPoint?, bool>? press = null;
        bool? through = null;
        var win = new EmiRingWindow(new PixelRect(590, 290, 220, 420), new PixelPoint(700, 500))
        {
            StartDismissListener = callback => { press = callback; return new Listener(); },
            ApplyButtonClickThrough = value => through = value,
        };
        try
        {
            win.OpenRing();
            var card = win.FindControl<Canvas>("Field")!.Children.OfType<Border>().First();
            var hot = win.Position + PixelPoint.FromPoint(new Point(Canvas.GetLeft(card) + 20, Canvas.GetTop(card) + 20), win.RenderScaling);
            var gap = new PixelPoint(-5000, -5000);
            foreach (bool onCard in new[] { cardFirst, !cardFirst })
            {
                through = onCard; // stale style from the opposite side of the boundary
                press!(onCard ? hot : gap, false);
                Assert.Equal(!onCard, through); // before any dispatcher job, timer tick or delivery
                Assert.True(win.IsOpen);
            }
        }
        finally { win.Kill(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RingIntakeLaunchesWithoutAiDucksFirstRunAndRefocuses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var state = global::ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        var usage = state.Usage.ToArray();
        var scores = state.OpenScore.ToArray();
        var usageAt = state.UsageAt.ToArray();
        var shellProvider = EmiTargets.ShellProvider;
        var ai = global::ConditioningControlPanel.CoreAi.IsAvailableProvider;
        var login = global::ConditioningControlPanel.CoreAccount.IsLoggedInProvider;
        var paid = global::ConditioningControlPanel.CoreAccount.HasPremiumAccessProvider;
        var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            EmiTargets.ShellProvider = () => shell;
            global::ConditioningControlPanel.CoreAi.IsAvailableProvider = () => false;
            global::ConditioningControlPanel.CoreAccount.IsLoggedInProvider = () => true;
            global::ConditioningControlPanel.CoreAccount.HasPremiumAccessProvider = () => true;
            Assert.False(global::ConditioningControlPanel.Models.IntakePunchCardState.ReadEverCompletedIntake(global::ConditioningControlPanel.CorePaths.UserData));
            shell.Show();
            shell.WindowState = WindowState.Maximized;
            EmiTargets.Find("intake")!.Open();
            var run = Assert.Single(global::ConditioningControlPanel.Avalonia.Views.Windows.IntakeHostWindow.Snapshot());
            Assert.Same(shell, run.DuckTarget);
            Assert.Equal(WindowState.Minimized, shell.WindowState);
            // Count the real platform Activate call: headless emits no native focus event.
            var field = typeof(TopLevel).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(global::Avalonia.Platform.ITopLevelImpl));
            var real = field.GetValue(run)!;
            var proxy = System.Reflection.DispatchProxy.Create<global::Avalonia.Platform.IWindowImpl, ActivationSpy>();
            var spy = (ActivationSpy)(object)proxy;
            spy.Inner = real;
            field.SetValue(run, proxy);
            try
            {
                EmiTargets.Find("intake")!.Open();
                Assert.Same(run, Assert.Single(global::ConditioningControlPanel.Avalonia.Views.Windows.IntakeHostWindow.Snapshot()));
                Assert.Equal(1, spy.Activations);
            }
            finally { field.SetValue(run, real); }
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Views.Windows.IntakeHostWindow.CloseAllForPanic();
            shell.Close();
            EmiTargets.ShellProvider = shellProvider;
            state.Usage.Clear(); foreach (var pair in usage) state.Usage[pair.Key] = pair.Value;
            state.OpenScore.Clear(); foreach (var pair in scores) state.OpenScore[pair.Key] = pair.Value;
            state.UsageAt.Clear(); foreach (var pair in usageAt) state.UsageAt[pair.Key] = pair.Value;
            global::ConditioningControlPanel.CoreAi.IsAvailableProvider = ai;
            global::ConditioningControlPanel.CoreAccount.IsLoggedInProvider = login;
            global::ConditioningControlPanel.CoreAccount.HasPremiumAccessProvider = paid;
        }
        return Task.CompletedTask;
    });

    public class ActivationSpy : System.Reflection.DispatchProxy
    {
        internal object Inner = null!;
        internal int Activations;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "Activate") Activations++;
            return method.Invoke(Inner, args);
        }
    }

}
