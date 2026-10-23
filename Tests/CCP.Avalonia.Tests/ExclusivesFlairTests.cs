using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

/// <summary>WPF MainWindow.Exclusives.Flair.cs + OnExclusiveCardHover, reached through the shell's ShowTab("premium"):
/// the entrance, the owned cards' aura, the sign shimmer, the hover lift/bloom and the click burst.</summary>
public sealed class ExclusivesFlairTests
{
    private static readonly SteppedClock Clock = new();

    [Fact]
    public Task CardsRiseInOnTheirTurnWhenThePageOpens() => WithPremiumPage((shell, view) =>
    {
        Assert.True(view.EntranceRunning);
        var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
        var first = shelf.ContainerFromIndex(0)!;
        var late = shelf.ContainerFromIndex(10)!;
        Assert.Equal(0, late.Opacity);
        Step(view, 0.1);
        Assert.InRange(first.Opacity, 0.1, 0.99);
        Assert.True(((TranslateTransform)first.RenderTransform!).Y > 0);
        Assert.Equal(0, late.Opacity);   // its turn is 380 ms in
        Step(view, 1.0);
        Assert.False(view.EntranceRunning);
        Assert.All(shelf.GetRealizedContainers(), c => Assert.Equal(1, c.Opacity));
        Assert.All(shelf.GetRealizedContainers(), c => Assert.Equal(0, ((TranslateTransform)c.RenderTransform!).Y));
    });

    [Fact]
    public Task OwnedCardsWearABreathingAuraThatRestsWithThePage() => WithPremiumPage((shell, view) =>
    {
        var auras = Parts<VaultCardAura>(view);
        Assert.Contains(auras, a => ((ExclusiveCardRow)a.DataContext!).IsMine && a.IsVisible);
        Assert.All(auras.Where(a => !((ExclusiveCardRow)a.DataContext!).IsMine), a => Assert.False(a.IsVisible));
        var mine = auras.First(a => a.IsVisible);
        Assert.Equal(ExclusiveGroupRow.HueOf(((ExclusiveCardRow)mine.DataContext!).Feature), mine.Hue);
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

        shell.ShowTab("settings"); Settle();
        Assert.False(mine.Breathing);
        Assert.Equal(VaultCardAura.StaticOpacity, mine.Opacity);
        Assert.Equal(0, mine.GlintOpacity);
    });

    [Fact]
    public Task AShimmerCrossesEachTierSignInTurn() => WithPremiumPage((shell, view) =>
    {
        Assert.Equal(2, view.SignSheens.Count);   // Basic and Prime; Free has no sign
        var (basic, prime) = (view.SignSheens[0], view.SignSheens[1]);
        Assert.Equal(0, basic.Opacity);
        Step(view, 0.8);   // the Basic band is 0.3 s into its crossing; Prime starts at 1.2 s
        Assert.Equal(1, basic.Opacity);
        double x = ((TranslateTransform)basic.RenderTransform!).X;
        Assert.InRange(x, -1.2 * basic.Bounds.Width + 1, 1.2 * basic.Bounds.Width - 1);
        Assert.Equal(0, prime.Opacity);

        shell.ShowTab("settings"); Settle();
        Assert.Equal(0, basic.Opacity);
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
    public Task AClickBurstsAndPressesThenOpensTheDoor() => WithPremiumPage((shell, view) =>
    {
        var queued = new List<(TimeSpan Delay, Action Then)>();
        var later = ExclusivesTabView.Later;
        ExclusivesTabView.Later = (d, a) => queued.Add((d, a));
        try
        {
            var card = Parts<Border>(view).First(b => b.Name == "ExCard" && ((ExclusiveCardRow)b.DataContext!).Feature.Key == "haptics");
            var fx = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            view.ClickCard(card, ((ExclusiveCardRow)card.DataContext!).Feature);
            Assert.True(fx.BurstCount > 0);
            Assert.Equal(TimeSpan.FromMilliseconds(140), Assert.Single(queued).Delay);
            Assert.Equal("premium", shell.CurrentTab);   // the door waits for the burst
            queued[0].Then();
            Settle();
            Assert.NotEqual("premium", shell.CurrentTab);

            // An inert card (no host on this head) neither bursts nor queues.
            queued.Clear();
            shell.ShowTab("premium"); Settle();
            var inert = Parts<Border>(view).First(b => b.Name == "ExCard" && ((ExclusiveCardRow)b.DataContext!).Feature.Key == "backroom");
            view.ClickCard(inert, ((ExclusiveCardRow)inert.DataContext!).Feature);
            Assert.Empty(queued);
        }
        finally { ExclusivesTabView.Later = later; }
    });

    [Fact]
    public void GlintAndEntranceFollowWpfKeyframes()
    {
        Assert.Equal(1, VaultCardAura.GlintAt(140).Opacity, 6);
        Assert.Equal(0, VaultCardAura.GlintAt(620).Opacity, 6);
        Assert.Equal(1.15, VaultCardAura.GlintAt(180).Scale, 6);
        Assert.Equal(45, VaultCardAura.GlintAt(310).Turn, 6);
        Assert.Equal((0.0, 18.0), ExclusivesTabView.EntranceAt(3, 3 * 38, true));
        Assert.Equal((1.0, 0.0), ExclusivesTabView.EntranceAt(40, 16 * 38 + 420, true));   // capped at 16 slots
        Assert.Equal((1.0, 0.0), ExclusivesTabView.EntranceAt(2, 40 + 200, false));   // Reduced: fade only
        Assert.Equal(-1.2, ExclusivesTabView.SignSheenAt(0.4, 0));
        Assert.Equal(1.2, ExclusivesTabView.SignSheenAt(0.5 + 1.1, 0), 6);
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
        view.EntranceFrame();
        view.MotionFrame();
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
