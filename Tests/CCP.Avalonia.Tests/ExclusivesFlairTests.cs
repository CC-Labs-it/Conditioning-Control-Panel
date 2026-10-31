using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF owned-card aura and daily-free repaint regressions.</summary>
public sealed class ExclusivesFlairTests
{
    private static readonly SteppedClock Clock = new();

    [Fact]
    public Task OwnedCardsWearABreathingAuraThatRestsWithThePage() => WithPremiumPage((shell, view) =>
    {
        var auras = Parts<VaultCardAura>(view);
        Assert.Contains(auras, a => ((ExclusiveCardRow)a.DataContext!).IsMine && a.IsVisible);
        Assert.All(auras.Where(a => !((ExclusiveCardRow)a.DataContext!).IsMine), a => Assert.False(a.IsVisible));
        var mine = auras.First(a => a.IsVisible);
        Assert.Equal(((ExclusiveCardRow)mine.DataContext!).GroupHue, mine.Hue);
        bool glinted = false;
        for (int i = 0; i < 200; i++)
        {
            Step(view, 0.05);
            glinted |= auras.Any(a => a.IsVisible && a.GlintOpacity > 0);
        }
        Assert.True(mine.Breathing);
        Assert.InRange(mine.Opacity, 0.32, 0.95);
        Assert.NotEqual(VaultCardAura.StaticOpacity, mine.Opacity);
        Assert.True(glinted);

        view.RefreshVault(); Settle();
        Assert.DoesNotContain(mine, Parts<VaultCardAura>(view));
        view.MotionFrame();
        Assert.False(mine.Breathing);
        var replacement = Parts<VaultCardAura>(view).First(a => a.IsVisible);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Step(view, 0.1);
        Assert.False(replacement.Breathing);
        Assert.Equal(VaultCardAura.StaticOpacity, replacement.Opacity);
        shell.ShowTab("settings"); Settle();
        Assert.False(mine.Breathing);
        Assert.Equal(VaultCardAura.StaticOpacity, mine.Opacity);
        Assert.Equal(0, mine.GlintOpacity);
    });

    [Fact]
    public Task ReducedAuraBreathesWithoutAmbientLoopsAndCleansUp() => WithPremiumPage((shell, view) =>
    {
        var aura = Parts<VaultCardAura>(view).First(a => a.IsVisible);
        aura.Seed = 0; // exact WPF period, without seed jitter
        aura.Frame(0.74);
        Assert.Equal(1, aura.GlintOpacity, 6);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(view.MotionRunning);
        Assert.True(view.ReducedAuraRunning);
        Assert.True(aura.Breathing);
        Assert.Equal(0, aura.GlintOpacity);
        Assert.Equal(0.5, aura.Opacity, 6);
        Step(view, 1.3);
        Assert.Equal(0.532218, aura.Opacity, 6);
        Step(view, 3.9);
        Assert.Equal(0.72, aura.Opacity, 6);
        Step(view, 5.2);
        Assert.Equal(0.5, aura.Opacity, 6);
        Assert.Equal(0, aura.GlintOpacity);

        aura.Seed = 3; // jitter 0.39, direction 5.59, phase advanced 1.17 seconds
        aura.Frame(0, full: false);
        Assert.Equal(0.522935, aura.Opacity, 6);
        aura.Frame(4.42, full: false);
        Assert.Equal(0.72, aura.Opacity, 6);

        // A rebuilt shelf releases the old aura and the new one joins the Reduced clock.
        view.RefreshVault(); Settle();
        Assert.False(aura.Breathing);
        aura = Parts<VaultCardAura>(view).First(a => a.IsVisible);
        Step(view, 0.2);
        Assert.True(aura.Breathing);
        shell.ShowTab("settings"); Settle();
        Assert.False(view.ReducedAuraRunning);
        Assert.False(aura.Breathing);
        Assert.Equal(0.6, aura.Opacity);
        shell.ShowTab("premium"); Settle();
        Assert.True(view.ReducedAuraRunning);
        aura = Parts<VaultCardAura>(view).First(a => a.IsVisible);
        Step(view, 0.2);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(view.ReducedAuraRunning);
        Assert.False(aura.Breathing);
        Assert.Equal(0.6, aura.Opacity);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(view.ReducedAuraRunning);
        shell.Content = null; Settle();
        Assert.False(view.ReducedAuraRunning);
        Assert.False(aura.Breathing);
        Assert.Equal(0.6, aura.Opacity);
    });

    [Fact]
    public Task HoverLiftsAndBloomsTheCard() => WithPremiumPage((shell, view) =>
    {
        var card = Parts<Border>(view).First(b => b.Name == "ExCard");
        ExclusivesTabView.HoverCard(card, true);
        var scale = (ScaleTransform)card.RenderTransform!;
        Assert.NotNull(scale.Transitions);
        Assert.Equal(1, card.BoxShadow.Count);
        Assert.Equal(24, card.BoxShadow[0].Blur);
        Assert.Equal(0x8C, card.BoxShadow[0].Color.A);
        ExclusivesTabView.HoverCard(card, false);
        Assert.Equal(0, card.BoxShadow.Count);

        // Motion Off: the lift snaps, no clock.
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        ExclusivesTabView.HoverCard(card, true);
        Assert.Null(scale.Transitions);
        Assert.Equal(1.02, scale.ScaleX);
        ExclusivesTabView.HoverCard(card, false);
        Assert.Equal(1.0, scale.ScaleX);
    });

    [Fact]
    public void GlintFollowsWpfKeyframes()
    {
        Assert.Equal(1, VaultCardAura.GlintAt(140).Opacity, 6);
        Assert.Equal(0, VaultCardAura.GlintAt(620).Opacity, 6);
        Assert.Equal(1.15, VaultCardAura.GlintAt(180).Scale, 6);
        Assert.Equal(45, VaultCardAura.GlintAt(310).Turn, 6);
    }

    [Fact]
    public Task EveryRepaintChecksDailyOverrideThroughCoreGates() => WithPremiumPage((shell, view) =>
    {
        var service = new ConditioningControlPanel.Services.DailyFreeService();
        var handler = new OverrideHandler();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ConditioningControlPanel.Services.DailyFreeService).GetField("_http", flags)!
            .SetValue(service, new System.Net.Http.HttpClient(handler));
        var previous = global::ConditioningControlPanel.Avalonia.App.DailyFree;
        try
        {
            global::ConditioningControlPanel.Avalonia.App.DailyFree = service;
            CoreEntitlement.IsFreeTodayProvider = service.IsFreeToday;
            view.RefreshVault();
            Assert.Equal(1, handler.Calls);
            Assert.Equal("voice", service.TodayKey);
            Assert.True(view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.OfType<ExclusiveCardRow>()
                .Single(r => r.Feature.DailyFreeKey == "voice").FreeToday);
            view.RefreshVault();
            Assert.Equal(1, handler.Calls); // the Core six-hour gate still applies
            typeof(ConditioningControlPanel.Services.DailyFreeService).GetField("_lastFetchUtc", flags)!
                .SetValue(service, DateTime.MinValue);
            typeof(ConditioningControlPanel.Services.DailyFreeService).GetField("_lastAttemptUtc", flags)!
                .SetValue(service, DateTime.MinValue);
            view.RefreshVault();
            Assert.Equal(2, handler.Calls);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.App.DailyFree = previous;
            ((System.Threading.Timer)typeof(ConditioningControlPanel.Services.DailyFreeService)
                .GetField("_dayRollTimer", flags)!.GetValue(service)!).Dispose();
        }
    });

    private sealed class OverrideHandler : System.Net.Http.HttpMessageHandler
    {
        public int Calls;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("{\"key\":\"voice\"}") });
        }
    }

    private static Task WithPremiumPage(Action<MainShellWindow, ExclusivesTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var (premium, lab, free) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider);
        FxAdorner.Time = Clock;
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.HasLabProvider = null;
            CoreEntitlement.IsFreeTodayProvider = key => key == "haptics";
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("premium"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            Assert.True(view.MotionRunning);
            body(shell, view);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = (premium, lab, free);
            (s.MotionLevel, s.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private static T[] Parts<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>().ToArray();

    /// <summary>Advances the shared clock and runs one frame of each clock the page owns.</summary>
    private static void Step(ExclusivesTabView view, double seconds)
    {
        Clock.Now += (long)(seconds * TimeSpan.TicksPerSecond);
        view.MotionFrame();
        view.ReducedAuraFrame();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
